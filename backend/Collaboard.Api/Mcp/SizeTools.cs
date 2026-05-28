using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

// Card #243 Phase 3: admin-level MCP tools for card-size CRUD. Mirrors the REST
// surface in SizeEndpoints.cs (POST /boards/{boardId}/sizes, PATCH /sizes/{id},
// DELETE /sizes/{id}). All three gate via RequireAdminLevelAsync.
[McpServerToolType]
public sealed class SizeTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    [McpServerTool(Name = "create_size", Destructive = false)]
    [Description("Create a card size on a board. Requires Administrator or AgentAdministrator role. If ordinal is omitted, it is auto-assigned to one greater than the board's current highest ordinal.")]
    public async Task<string> CreateSizeAsync(
        [Description("Your auth key")] string authKey,
        [Description("The board ID to create the size on")] Guid boardId,
        [Description("The size name (e.g. 'S', 'M', 'L', 'XL')")] string name,
        [Description("The size's ordinal (ordering value). Auto-assigned if omitted.")] int? ordinal = null,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireAdminLevelAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        if (!await db.Boards.AnyAsync(b => b.Id == boardId, ct))
        {
            return "Error: Board not found.";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return "Error: Name is required.";
        }

        var resolvedOrdinal = ordinal ?? 0;
        if (!ordinal.HasValue && await db.CardSizes.AnyAsync(s => s.BoardId == boardId, ct))
        {
            resolvedOrdinal = await db.CardSizes.Where(s => s.BoardId == boardId).MaxAsync(s => s.Ordinal, ct) + 1;
        }

        var size = new CardSize { Id = Guid.NewGuid(), BoardId = boardId, Name = name, Ordinal = resolvedOrdinal };
        db.CardSizes.Add(size);
        await db.SaveChangesAsync(ct);
        broadcaster.PublishBoardUpdated(boardId);
        return JsonSerializer.Serialize(size, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "update_size", Destructive = false)]
    [Description("Update a card size's name and/or ordinal. Requires Administrator or AgentAdministrator role. An ordinal already taken by another size on the board is a conflict.")]
    public async Task<string> UpdateSizeAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the size to update")] Guid sizeId,
        [Description("The new size name (optional)")] string? name = null,
        [Description("The new ordinal (optional)")] int? ordinal = null,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireAdminLevelAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var size = await db.CardSizes.FindAsync([sizeId], ct);
        if (size is null)
        {
            return "Error: Size not found.";
        }

        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Error: Name cannot be empty.";
            }

            size.Name = name;
        }

        if (ordinal is not null)
        {
            var newOrd = ordinal.Value;
            if (await db.CardSizes.AnyAsync(s => s.BoardId == size.BoardId && s.Ordinal == newOrd && s.Id != sizeId, ct))
            {
                return "Error: Ordinal already taken by another size.";
            }

            size.Ordinal = newOrd;
        }

        await db.SaveChangesAsync(ct);
        broadcaster.PublishBoardUpdated(size.BoardId);
        return JsonSerializer.Serialize(size, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "delete_size", Destructive = true)]
    [Description("Delete a card size. Requires Administrator or AgentAdministrator role. A size in use by any card cannot be deleted.")]
    public async Task<string> DeleteSizeAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the size to delete")] Guid sizeId,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireAdminLevelAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var size = await db.CardSizes.FindAsync([sizeId], ct);
        if (size is null)
        {
            return "Error: Size not found.";
        }

        if (await db.Cards.AnyAsync(c => c.SizeId == sizeId, ct))
        {
            return "Error: Size is in use by cards.";
        }

        var boardId = size.BoardId;
        db.CardSizes.Remove(size);
        await db.SaveChangesAsync(ct);
        broadcaster.PublishBoardUpdated(boardId);
        return "Size deleted.";
    }
}
