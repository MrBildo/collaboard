using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class CommentTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    [McpServerTool(Name = "add_comment", Destructive = false)]
    [Description("Add a comment to a card.")]
    public async Task<string> AddCommentAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card to comment on")] Guid cardId,
        [Description("The comment text (Markdown supported)")] string content,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
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
        await db.PublishForCardAsync(cardId, broadcaster);
        return JsonSerializer.Serialize(comment, JsonSerializerOptions.Web);
    }
}
