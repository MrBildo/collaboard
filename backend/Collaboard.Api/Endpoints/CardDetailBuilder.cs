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

        var userIds = comments.Select(c => c.UserId)
            .Append(card.CreatedByUserId)
            .Append(card.LastUpdatedByUserId)
            .Distinct()
            .ToList();
        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var commentsWithUserNames = comments.Select(c => new CardDetailComment(
            c.Id, c.CardId, c.UserId,
            userNames.GetValueOrDefault(c.UserId),
            c.ContentMarkdown, c.LastUpdatedAtUtc
        )).ToList();

        var labels = await db.CardLabels.Where(cl => cl.CardId == card.Id)
            .Join(db.Labels, cl => cl.LabelId, l => l.Id, (_, l) => l)
            .ToListAsync(ct);

        var attachments = await db.Attachments
            .Where(a => a.CardId == card.Id)
            .Select(a => new CardDetailAttachment(a.Id, a.FileName, a.ContentType, a.AddedByUserId, a.AddedAtUtc))
            .ToListAsync(ct);

        var sizeName = await db.CardSizes
            .Where(s => s.Id == card.SizeId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? "?";

        return new CardDetail(
            card,
            sizeName,
            userNames.GetValueOrDefault(card.CreatedByUserId),
            userNames.GetValueOrDefault(card.LastUpdatedByUserId),
            commentsWithUserNames,
            labels,
            attachments
        );
    }
}

internal record CardDetailComment(
    Guid Id,
    Guid CardId,
    Guid UserId,
    string? UserName,
    string ContentMarkdown,
    DateTimeOffset LastUpdatedAtUtc);

internal record CardDetailAttachment(
    Guid Id,
    string FileName,
    string ContentType,
    Guid AddedByUserId,
    DateTimeOffset AddedAtUtc);

internal record CardDetail(
    CardItem Card,
    string SizeName,
    string? CreatedByUserName,
    string? LastUpdatedByUserName,
    List<CardDetailComment> Comments,
    List<Label> Labels,
    List<CardDetailAttachment> Attachments);
