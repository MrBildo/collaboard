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
        [Description("The card number (provide this or cardId)")] long? cardNumber = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var (resolvedCardId, resolveError) = await McpCardResolver.ResolveCardIdAsync(db, cardId, cardNumber, ct);
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
}
