using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class CommentTools(BoardDbContext db, McpAuthService auth)
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool(Name = "add_comment", Destructive = false)]
    [Description("Add a comment to a card.")]
    public async Task<string> AddCommentAsync(
        [Description("The ID (guid) of the card to comment on")] Guid cardId,
        [Description("The comment text (Markdown supported)")] string content,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(ct);
        if (error is not null)
        {
            return error;
        }

        if (!await db.Cards.AnyAsync(c => c.Id == cardId, ct))
        {
            return "Error: Card not found.";
        }

        var comment = new CardComment
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            UserId = user!.Id,
            ContentMarkdown = content,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return JsonSerializer.Serialize(comment, _jsonOptions);
    }

    [McpServerTool(Name = "get_comments", ReadOnly = true, Destructive = false)]
    [Description("Get all comments on a card.")]
    public async Task<string> GetCommentsAsync(
        [Description("The ID (guid) of the card")] Guid cardId,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(ct);
        if (error is not null)
        {
            return error;
        }

        if (!await db.Cards.AnyAsync(c => c.Id == cardId, ct))
        {
            return "Error: Card not found.";
        }

        var comments = await db.Comments
            .Where(c => c.CardId == cardId)
            .OrderBy(c => c.LastUpdatedAtUtc)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(comments, _jsonOptions);
    }
}
