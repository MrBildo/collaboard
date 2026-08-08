using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CardDetailBuilder
{
    public static async Task<CardDetail> BuildAsync
    (
        BoardDbContext db,
        CardItem card,
        bool includeDescription,
        int commentsOffset,
        int? commentsLimit,
        CancellationToken ct = default
    )
    {
        // totalCount is the whole thread regardless of the page taken, so a capped read can see it was
        // capped rather than mistake the page for the whole. Always resolved; a count-only read
        // (commentsLimit == 0) stops here and loads no comment bodies.
        var commentsTotalCount = await db.Comments
            .CountAsync(c => c.CardId == card.Id, ct);

        List<CardComment> pagedComments = [];
        if (commentsLimit != 0)
        {
            // Newest activity first — the order the SPA and the triage workflow both read comments in,
            // and the order a paged reader wants (page 0 is the freshest). SortableUtc stores the
            // timestamp as a fixed-width, lexically-ordered string, so the ordering and the skip/take
            // translate to SQL and the (CardId, LastUpdatedAtUtc) index serves them. "Newest" is
            // most-recently-touched, not most-recently-created: an edited comment resurfaces, by design.
            var pagedQuery = db.Comments
                .Where(c => c.CardId == card.Id)
                .OrderByDescending(c => c.LastUpdatedAtUtc)
                .Skip(commentsOffset);

            // A null limit is REST's omit-for-all; the MCP surface always passes a capped value.
            if (commentsLimit.HasValue)
            {
                pagedQuery = pagedQuery.Take(commentsLimit.Value);
            }

            pagedComments = await pagedQuery.ToListAsync(ct);
        }

        // Only the paged comments' authors need names — a capped read does not pay to name authors it
        // is not returning — plus the card's own creator and last editor.
        var userIds = pagedComments
            .Select(c => c.UserId)
            .Append(card.CreatedByUserId)
            .Append(card.LastUpdatedByUserId)
            .Distinct()
                .ToList();
        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var commentItems = pagedComments
            .Select(c => new CardDetailComment
            (
                c.Id,
                c.CardId,
                c.UserId,
                userNames.GetValueOrDefault(c.UserId),
                c.ContentMarkdown,
                c.CreatedAtUtc,
                c.LastUpdatedAtUtc
            ))
                .ToList();

        var comments = new PagedResult<CardDetailComment>(commentItems, commentsTotalCount, commentsOffset, commentsLimit);

        var labels = await db.CardLabels
            .Where(cl => cl.CardId == card.Id)
                .Join(db.Labels, cl => cl.LabelId, l => l.Id, (_, l) => l)
                    .ToListAsync(ct);

        var attachments = await db.Attachments
            .Where(a => a.CardId == card.Id)
                .Select(a => new CardDetailAttachment(a.Id, a.FileName, a.ContentType, (long)a.Payload.Length, a.AddedByUserId, a.AddedAtUtc))
                    .ToListAsync(ct);

        var sizeName = await db.CardSizes
            .Where(s => s.Id == card.SizeId)
                .Select(s => s.Name)
                    .FirstOrDefaultAsync(ct) ?? "?";

        var isArchived = await db.Lanes
            .AnyAsync(l => l.Id == card.LaneId && l.IsArchiveLane, ct);

        // Carried on the detail so a consumer can decide whether a history affordance is worth
        // offering without spending a second call to find out the trail is empty — which it is for
        // every card that has not been description-edited since recording began. Present in every
        // projection, including a description-omitted or count-only read: it is the teaser for the
        // one axis this read can drop.
        var descriptionHistoryCount = await CardHistoryHelper.CountRevisionsAsync(db, card.Id, CardHistoryHelper.DescriptionField, ct);

        // A dedicated response record rather than the tracked entity, so a description-omitted read
        // blanks the field on the wire without mutating change-tracked state — nulling it on the
        // entity would risk a later SaveChanges in the same scope persisting the blank. Every other
        // field is carried through unchanged. includeDescription is whole-or-nothing by design: a
        // description is one document, and dropping it is the token saving a heavy-card read wants.
        var cardResponse = new CardResponse
        (
            card.Id,
            card.Number,
            card.BoardId,
            card.Name,
            includeDescription ? card.DescriptionMarkdown : string.Empty,
            card.SizeId,
            card.LaneId,
            card.Position,
            card.CreatedByUserId,
            card.CreatedAtUtc,
            card.LastUpdatedByUserId,
            card.LastUpdatedAtUtc,
            card.IsTemp
        );

        return new CardDetail
        (
            cardResponse,
            sizeName,
            userNames.GetValueOrDefault(card.CreatedByUserId),
            userNames.GetValueOrDefault(card.LastUpdatedByUserId),
            comments,
            labels,
            attachments,
            isArchived,
            descriptionHistoryCount
        );
    }
}

// The card core as it goes on the wire — a projection of CardItem, never the tracked entity, so a
// read can blank the description (includeDescription = false) without touching change-tracked state.
// Field-complete against CardItem otherwise: an included-description read is byte-for-byte the card
// object callers saw before this projection existed.
internal record CardResponse
(
    Guid Id,
    long Number,
    Guid BoardId,
    string Name,
    string DescriptionMarkdown,
    Guid SizeId,
    Guid LaneId,
    int Position,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    Guid LastUpdatedByUserId,
    DateTimeOffset LastUpdatedAtUtc,
    bool IsTemp
);

// CreatedAtUtc is the comment's stamped-once posting time; LastUpdatedAtUtc is bumped on every edit
// and is the key the thread is paged by (newest activity first).
internal record CardDetailComment
(
    Guid Id,
    Guid CardId,
    Guid UserId,
    string? UserName,
    string ContentMarkdown,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastUpdatedAtUtc
);

internal record CardDetailAttachment
(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSize,
    Guid AddedByUserId,
    DateTimeOffset AddedAtUtc
);

// Comments are a paged sub-envelope (PagedResult): a card's one unbounded collection, so a heavy card
// no longer forces the whole thread into a single read. Newest activity first; totalCount is the whole
// thread regardless of the page. commentsLimit = 0 returns an empty page with the true total (count
// only). REST omits the limit for the whole thread; MCP caps by default (it pays per token).
//
// DescriptionHistoryCount is the number of recorded revisions of this card's description — the same
// number the history trail reports as its totalCount, and the length of the trail a caller gets back
// unpaged. Zero means there is nothing to show: either the description has never been edited, or it
// has not been edited since recording began. It is never one — a card's first edit records two
// revisions, the value that was already there and the value that replaced it. Field-qualified rather
// than a bare history count because the store records fields other than description as soon as one is
// lit up, and a name that would have to change its meaning then is a name that misleads now.
internal record CardDetail
(
    CardResponse Card,
    string SizeName,
    string? CreatedByUserName,
    string? LastUpdatedByUserName,
    PagedResult<CardDetailComment> Comments,
    List<Label> Labels,
    List<CardDetailAttachment> Attachments,
    bool IsArchived,
    int DescriptionHistoryCount
);
