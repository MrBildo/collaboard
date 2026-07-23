using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

// The write-path capture seam for card field history. Both description write paths — the REST
// PATCH /cards/{id} handler and the MCP update_card tool — stage through here, so neither surface
// can drift from the other on what gets recorded (the parallel-surface drift this codebase's
// REST-and-MCP shape is most prone to).
//
// Staging only: rows are added to the change tracker and committed by the caller's existing
// SaveChangesAsync, so the card mutation and its history entry land in one transaction. A card can
// never be left holding a new description with no record of the old one.
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

    public static async Task StageDescriptionChangeAsync
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
            return;
        }

        var latestRevision = await db.CardFieldHistories
            .Where(h => h.CardId == cardId && h.Field == DescriptionField)
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
                CardId = cardId,
                Field = DescriptionField,
                Revision = 1,
                Value = oldValue,
                EditedByUserId = null,
                EditedAtUtc = null,
            });

            latestRevision = 1;
        }

        db.CardFieldHistories.Add(new CardFieldHistory
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            Field = DescriptionField,
            Revision = latestRevision + 1,
            Value = newValue,
            EditedByUserId = editedByUserId,
            EditedAtUtc = editedAtUtc,
        });
    }
}
