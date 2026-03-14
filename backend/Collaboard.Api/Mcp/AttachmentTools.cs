using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class AttachmentTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool(Name = "get_attachments", ReadOnly = true, Destructive = false)]
    [Description("Get all attachments on a card (metadata only, no file content). To upload attachments, use the REST API: POST /api/v1/cards/{cardId}/attachments with multipart/form-data. To download, GET /api/v1/attachments/{id} with X-User-Key header.")]
    public async Task<string> GetAttachmentsAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card")] Guid cardId,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        if (!await db.Cards.AnyAsync(c => c.Id == cardId, ct))
        {
            return "Error: Card not found.";
        }

        var attachments = await db.Attachments
            .Where(a => a.CardId == cardId)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.AddedByUserId, a.AddedAtUtc })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(attachments, _jsonOptions);
    }

    [McpServerTool(Name = "delete_attachment", Destructive = true)]
    [Description("Delete an attachment you added. Administrators can delete any attachment.")]
    public async Task<string> DeleteAttachmentAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the attachment to delete")] Guid attachmentId,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var attachment = await db.Attachments.FindAsync([attachmentId], ct);
        if (attachment is null)
        {
            return "Error: Attachment not found.";
        }

        if (attachment.AddedByUserId != user!.Id && user.Role != UserRole.Administrator)
        {
            return "Error: You can only delete your own attachments.";
        }

        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        broadcaster.Publish("board-updated");
        return $"Attachment '{attachment.FileName}' deleted.";
    }
}
