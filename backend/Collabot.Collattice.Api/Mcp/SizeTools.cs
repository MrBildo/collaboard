using System.ComponentModel;
using System.Text.Json;
using Collabot.Collattice.Api.Endpoints;
using Collabot.Collattice.Api.Events;
using Collabot.Collattice.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collabot.Collattice.Api.Mcp;

// Admin-level MCP tools for card-size CRUD. Mirrors the REST
// surface in SizeEndpoints.cs (POST /boards/{boardId}/sizes, PATCH /sizes/{id},
// DELETE /sizes/{id}). All three gate via RequireAdminLevelAsync.
// reorder_sizes added, mirroring reorder_lanes.
[McpServerToolType]
public sealed class SizeTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    [McpServerTool(Name = "create_size", Destructive = false)]
    [Description("Create a card size on a board. Requires Administrator or AgentAdministrator role. If ordinal is omitted, it is auto-assigned to one greater than the board's current highest ordinal.")]
    public async Task<string> CreateSizeAsync
    (
        [Description("Your auth key")] string authKey,
        [Description("The board ID to create the size on")] Guid boardId,
        [Description("The size name (e.g. 'S', 'M', 'L', 'XL')")] string name,
        [Description("The size's ordinal (ordering value). Auto-assigned if omitted.")] int? ordinal = null,
        CancellationToken ct = default
    )
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
    public async Task<string> UpdateSizeAsync
    (
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the size to update")] Guid sizeId,
        [Description("The new size name (optional)")] string? name = null,
        [Description("The new ordinal (optional)")] int? ordinal = null,
        CancellationToken ct = default
    )
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
    public async Task<string> DeleteSizeAsync
    (
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the size to delete")] Guid sizeId,
        CancellationToken ct = default
    )
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

    [McpServerTool(Name = "reorder_sizes", Destructive = false)]
    [Description("Reorder all of a board's sizes in one call. Requires Administrator or AgentAdministrator role. Pass orderedSizeIds as a CSV of size GUIDs giving the complete desired order — it must be exactly the board's current size set (no missing, extra, duplicate, or unknown ids), else the call fails loud with no changes. The server assigns dense ordinals 0,1,2,… in that order. Returns the reordered sizes.")]
    public async Task<string> ReorderSizesAsync
    (
        [Description("Your auth key")] string authKey,
        [Description("The board ID whose sizes to reorder")] Guid boardId,
        [Description("CSV of size GUIDs in the complete desired order")] string orderedSizeIds,
        CancellationToken ct = default
    )
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

        var parts = (orderedSizeIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new Guid[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!Guid.TryParse(parts[i], out var parsed))
            {
                return $"Error: Invalid size ID format: '{parts[i]}'. Expected a GUID.";
            }

            ids[i] = parsed;
        }

        var (sizes, validationError) = await SizeReorderHelper.ValidateAsync(db, boardId, ids, ct);
        if (validationError is not null)
        {
            return $"Error: {validationError}";
        }

        var ordered = await SizeReorderHelper.ReorderAsync(db, sizes!, ids, ct);
        broadcaster.PublishBoardUpdated(boardId);
        return JsonSerializer.Serialize(ordered, JsonSerializerOptions.Web);
    }
}
