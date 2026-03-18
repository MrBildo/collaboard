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
    [Description("Add a comment to a card. Provide either cardId or cardNumber to identify the card.")]
    public async Task<string> AddCommentAsync(
        [Description("Your auth key")] string authKey,
        [Description("The comment text (Markdown supported)")] string content,
        [Description("The ID (guid) of the card to comment on (provide this or cardNumber)")] Guid? cardId = null,
        [Description("The card number (provide this or cardId). Requires boardId or boardSlug.")] long? cardNumber = null,
        [Description("Board ID (required when using cardNumber)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId when using cardNumber)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var (resolvedCardId, resolveError) = await McpCardResolver.ResolveCardIdAsync(db, cardId, cardNumber, boardId, boardSlug, ct);
        if (resolveError is not null)
        {
            return resolveError;
        }

        var card = await db.Cards.FindAsync([resolvedCardId!.Value], ct);
        if (card is null)
        {
            return "Error: Card not found.";
        }

        var comment = new CardComment
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            UserId = user!.Id,
            ContentMarkdown = content,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        await db.PublishForCardAsync(card.Id, broadcaster);
        return JsonSerializer.Serialize(comment, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "delete_comment", Destructive = true)]
    [Description("Delete a comment you wrote. Administrators can delete any comment.")]
    public async Task<string> DeleteCommentAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the comment to delete")] Guid commentId,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var comment = await db.Comments.FindAsync([commentId], ct);
        if (comment is null)
        {
            return "Error: Comment not found.";
        }

        if (comment.UserId != user!.Id && user.Role != UserRole.Administrator)
        {
            return "Error: You can only delete your own comments.";
        }

        var cardId = comment.CardId;
        db.Comments.Remove(comment);
        await db.SaveChangesAsync(ct);
        await db.PublishForCardAsync(cardId, broadcaster);
        return "Comment deleted.";
    }
}
