using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CardDetailBuilder
{
    public static async Task<CardDetail> BuildAsync(BoardDbContext db, CardItem card, CancellationToken ct = default)
    {
        var comments = (await db.Comments
            .Where(c => c.CardId == card.Id)
                .ToListAsync(ct))
            .OrderBy(c => c.LastUpdatedAtUtc)
                .ToList();

        var userIds = comments
            .Select(c => c.UserId)
            .Append(card.CreatedByUserId)
            .Append(card.LastUpdatedByUserId)
            .Distinct()
                .ToList();
        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var commentsWithUserNames = comments
            .Select(c => new CardDetailComment
            (
                c.Id,
                c.CardId,
                c.UserId,
                userNames.GetValueOrDefault(c.UserId),
                c.ContentMarkdown,
                c.LastUpdatedAtUtc
            ))
                .ToList();

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
        // every card that has not been description-edited since recording began.
        var descriptionHistoryCount = await CardHistoryHelper.CountRevisionsAsync(db, card.Id, CardHistoryHelper.DescriptionField, ct);

        return new CardDetail
        (
            card,
            sizeName,
            userNames.GetValueOrDefault(card.CreatedByUserId),
            userNames.GetValueOrDefault(card.LastUpdatedByUserId),
            commentsWithUserNames,
            labels,
            attachments,
            isArchived,
            descriptionHistoryCount
        );
    }
}

internal record CardDetailComment
(
    Guid Id,
    Guid CardId,
    Guid UserId,
    string? UserName,
    string ContentMarkdown,
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

// DescriptionHistoryCount is the number of recorded revisions of this card's description — the
// same number the history trail reports as its totalCount, and the length of the trail a caller
// gets back unpaged. Zero means there is nothing to show: either the description has never been
// edited, or it has not been edited since recording began. It is never one — a card's first edit
// records two revisions, the value that was already there and the value that replaced it.
// Field-qualified rather than a bare history count because the store records fields other than
// description as soon as one is lit up, and a name that would have to change its meaning then is
// a name that misleads now.
internal record CardDetail
(
    CardItem Card,
    string SizeName,
    string? CreatedByUserName,
    string? LastUpdatedByUserName,
    List<CardDetailComment> Comments,
    List<Label> Labels,
    List<CardDetailAttachment> Attachments,
    bool IsArchived,
    int DescriptionHistoryCount
);
