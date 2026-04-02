using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Endpoints;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class AttachmentTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster, IOptions<AttachmentSettings> attachmentSettings)
{
    [McpServerTool(Name = "download_attachment", ReadOnly = true, Destructive = false)]
    [Description("Download an attachment's content as base64. Returns the file name, content type, and base64-encoded payload.")]
    public async Task<string> DownloadAttachmentAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the attachment to download")] Guid attachmentId,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var attachment = await db.Attachments.FindAsync([attachmentId], ct);
        return attachment is null
            ? "Error: Attachment not found."
            : JsonSerializer.Serialize(new
            {
                attachment.Id,
                attachment.FileName,
                attachment.ContentType,
                FileSize = (long)attachment.Payload.Length,
                Base64Content = Convert.ToBase64String(attachment.Payload),
            }, JsonSerializerOptions.Web);
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

        if (await ArchiveGuard.IsCardArchivedAsync(db, attachment.CardId))
        {
            return "Archived cards cannot be modified.";
        }

        if (attachment.AddedByUserId != user!.Id && user.Role != UserRole.Administrator)
        {
            return "Error: You can only delete your own attachments.";
        }

        var deleteCardId = attachment.CardId;
        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        await db.PublishForCardAsync(deleteCardId, broadcaster);
        return $"Attachment '{attachment.FileName}' deleted.";
    }

    [McpServerTool(Name = "upload_attachment", Destructive = false)]
    [Description("Upload a file attachment to a card using base64-encoded content. Limited to 5MB. For larger files (up to 50MB), use the REST endpoint POST /api/v1/cards/{cardId}/attachments (multipart/form-data).")]
    public async Task<string> UploadAttachmentAsync(
        [Description("Your auth key")] string authKey,
        [Description("The file name (e.g., 'report.pdf')")] string fileName,
        [Description("The base64-encoded file content")] string base64Content,
        [Description("The ID (guid) of the card to attach the file to (provide this or cardNumber)")] Guid? cardId = null,
        [Description("The card number (provide this or cardId). Requires boardId or boardSlug.")] long? cardNumber = null,
        [Description("The MIME content type (e.g., 'application/pdf', 'image/png'). Defaults to 'application/octet-stream'")] string? contentType = null,
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

        if (await ArchiveGuard.IsCardArchivedAsync(db, resolvedCardId!.Value))
        {
            return "Archived cards cannot be modified.";
        }

        var card = await db.Cards.FindAsync([resolvedCardId.Value], ct);
        if (card is null)
        {
            return "Error: Card not found.";
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(base64Content);
        }
        catch (FormatException)
        {
            return "Error: Invalid base64 content.";
        }

        var maxBytes = attachmentSettings.Value.MaxFileSizeBytes;
        if (payload.Length > maxBytes)
        {
            var maxMb = maxBytes / (1024 * 1024);
            return $"Error: File exceeds {maxMb}MB limit for MCP uploads. Use the REST endpoint POST /api/v1/cards/{{cardId}}/attachments for larger files (up to 50MB).";
        }

        var attachment = new CardAttachment
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            FileName = fileName,
            ContentType = contentType ?? "application/octet-stream",
            Payload = payload,
            AddedByUserId = user!.Id,
            AddedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        await db.PublishForCardAsync(card.Id, broadcaster);
        return JsonSerializer.Serialize(new { attachment.Id, attachment.FileName }, JsonSerializerOptions.Web);
    }
}
