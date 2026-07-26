using Collaboard.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Collaboard.Api.Endpoints;

// The write-path capture seam for card field history. Both description write paths — the REST
// PATCH /cards/{id} handler and the MCP update_card tool — stage through here, so neither surface
// can drift from the other on what gets recorded (the parallel-surface drift this codebase's
// REST-and-MCP shape is most prone to).
//
// Staging only: rows are added to the change tracker and committed by SaveWithRevisionRetryAsync,
// which replaces the caller's SaveChangesAsync so the card mutation and its history entry land in
// one transaction. A card can never be left holding a new description with no record of the old one.
internal static class CardHistoryHelper
{
    public const string DescriptionField = "description";

    // The store is field-general, but only description is captured today. An unrecognised field is
    // rejected rather than answered with an empty trail: on an audit surface, a typo that reads as
    // "this card has no history" is worse than an error.
    private static readonly string[] _supportedFields = [DescriptionField];

    public static (string? Field, string? Error) ResolveField(string? requestedField)
    {
        if (string.IsNullOrWhiteSpace(requestedField))
        {
            return (DescriptionField, null);
        }

        var match = _supportedFields.FirstOrDefault(f => string.Equals(f, requestedField, StringComparison.OrdinalIgnoreCase));

        return match is not null
            ? (match, null)
            : (null, $"Unknown field '{requestedField}'. Supported fields: {string.Join(", ", _supportedFields)}.");
    }

    // One definition of "how many revisions does this card's field hold". The card detail's count
    // and the trail read's totalCount are the same number reported on two surfaces, and a reader
    // who sees them disagree has no way to tell which one lied — so they resolve through here
    // rather than each carrying its own copy of the predicate.
    public static Task<int> CountRevisionsAsync
    (
        BoardDbContext db,
        Guid cardId,
        string field,
        CancellationToken ct = default
    ) =>
        db.CardFieldHistories
            .Where(h => h.CardId == cardId && h.Field == field)
                .CountAsync(ct);

    // Returns the staged change so the caller can hand it to SaveWithRevisionRetryAsync, which
    // needs it to rebuild the rows if another editor wins the race for the revision number. Null
    // means nothing was staged and there is no revision to race for.
    public static async Task<StagedDescriptionChange?> StageDescriptionChangeAsync
    (
        BoardDbContext db,
        Guid cardId,
        string oldValue,
        string newValue,
        Guid editedByUserId,
        DateTimeOffset editedAtUtc,
        CancellationToken ct = default
    )
    {
        // A save that leaves the description exactly as it was is not an edit and records nothing —
        // otherwise a lane move or label change carrying an unchanged description in the same PATCH
        // would pad the trail with revisions that changed nothing.
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return null;
        }

        var change = new StagedDescriptionChange(cardId, oldValue, newValue, editedByUserId, editedAtUtc);
        await StageRowsAsync(db, change, ct);

        return change;
    }

    // Replaces the caller's SaveChangesAsync on the description write paths. The revision ordinal
    // is allocated as max+1 and enforced unique, so two edits of one card that read the same max
    // before either saves collide on insert. The window is brief in wall-clock terms and easy to
    // underestimate for that reason — measured under sustained eight-way concurrent editing of one
    // description, roughly half of the requests hit it. Before history existed those same saves all
    // simply succeeded, with the last one winning.
    //
    // The loser retries rather than being told to reload: the revision number is internal
    // bookkeeping, the caller's intent (set this description) is still fully satisfiable, and the
    // resulting trail records both edits in the order they committed. Reporting a conflict instead
    // would be lost-update protection this product has never had, fired only when two edits land
    // within microseconds of each other while two edits seconds apart still overwrite silently.
    public static async Task SaveWithRevisionRetryAsync
    (
        BoardDbContext db,
        StagedDescriptionChange? change,
        CancellationToken ct = default
    )
    {
        if (change is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        // Five rather than the three the card-number allocator uses, and with a pause between
        // them. That allocator contends over a whole board's card creations; this one contends
        // over repeated edits of a single card's description, where every loser of a collision
        // otherwise wakes at the same instant, re-reads the same head, and collides again in
        // lockstep. Measured under sustained eight-way concurrent editing of one description:
        // no retry at all loses roughly half the edits, immediate lockstep retries still lose a
        // few percent, and pausing a randomized few milliseconds first clears them.
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex)
                when (attempt < maxAttempts - 1
                      && ex.InnerException is SqliteException { SqliteErrorCode: 19 }
                      && HasStagedRows(db, change))
            {
                // Rebuild the rows against the trail's new head rather than renumbering the ones
                // already staged: the winning edit has by now written the seed row holding the
                // pre-history value, and re-adding a second copy of it at a later revision would
                // make the trail read as though the description had reverted.
                DetachStagedRows(db, change);
                await Task.Delay(Random.Shared.Next(2, 15), ct);
                await StageRowsAsync(db, change, ct);
            }
        }

        throw new InvalidOperationException("Failed to allocate a description history revision after retries.");
    }

    private static async Task StageRowsAsync(BoardDbContext db, StagedDescriptionChange change, CancellationToken ct)
    {
        var latestRevision = await db.CardFieldHistories
            .Where(h => h.CardId == change.CardId && h.Field == DescriptionField)
                .MaxAsync(h => (int?)h.Revision, ct) ?? 0;

        // First capture on this card seeds the value that was already in place. It is a real
        // revision (the trail's oldest), but nobody observed it being written — history is not
        // back-filled — so its author and time stay null rather than being attributed to the card's
        // creator or to whoever happened to trigger this first capture.
        if (latestRevision == 0)
        {
            db.CardFieldHistories.Add(new CardFieldHistory
            {
                Id = Guid.NewGuid(),
                CardId = change.CardId,
                Field = DescriptionField,
                Revision = 1,
                Value = change.OldValue,
                EditedByUserId = null,
                EditedAtUtc = null,
            });

            latestRevision = 1;
        }

        db.CardFieldHistories.Add(new CardFieldHistory
        {
            Id = Guid.NewGuid(),
            CardId = change.CardId,
            Field = DescriptionField,
            Revision = latestRevision + 1,
            Value = change.NewValue,
            EditedByUserId = change.EditedByUserId,
            EditedAtUtc = change.EditedAtUtc,
        });
    }

    // Asked of our own change tracker rather than of the exception's entries: a unique-constraint
    // failure somewhere else in the same save must reach the caller as itself, not be retried
    // three times and rethrown as a revision problem it never was.
    private static bool HasStagedRows(BoardDbContext db, StagedDescriptionChange change) =>
        StagedRows(db, change)
            .Any();

    private static void DetachStagedRows(BoardDbContext db, StagedDescriptionChange change)
    {
        // Materialized before mutating: setting an entry's state edits the collection being walked.
        var staged = StagedRows(db, change)
            .ToList();

        foreach (var entry in staged)
        {
            entry.State = EntityState.Detached;
        }
    }

    private static IEnumerable<EntityEntry<CardFieldHistory>> StagedRows(BoardDbContext db, StagedDescriptionChange change) =>
        db.ChangeTracker
            .Entries<CardFieldHistory>()
            .Where(e => e.State == EntityState.Added
                        && e.Entity.CardId == change.CardId
                        && e.Entity.Field == DescriptionField);
}

// The description edit a request has staged but not yet committed. Carried from staging to save so
// the rows can be rebuilt from the trail's current head if the save loses a revision race.
internal record StagedDescriptionChange
(
    Guid CardId,
    string OldValue,
    string NewValue,
    Guid EditedByUserId,
    DateTimeOffset EditedAtUtc
);
