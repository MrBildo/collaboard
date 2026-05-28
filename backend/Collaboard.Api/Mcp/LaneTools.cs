using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

// Card #243 Phase 3: admin-level MCP tools for lane CRUD. Mirrors the REST
// surface in LaneEndpoints.cs (POST /boards/{boardId}/lanes, PATCH /lanes/{id},
// DELETE /lanes/{id}). All three gate via RequireAdminLevelAsync.
[McpServerToolType]
public sealed class LaneTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    [McpServerTool(Name = "create_lane", Destructive = false)]
    [Description("Create a lane (column) on a board. Requires administrator privileges. Position is the lane's ordering value; int.MaxValue is reserved for the archive lane and is rejected.")]
    public async Task<string> CreateLaneAsync(
        [Description("Your auth key")] string authKey,
        [Description("The board ID to create the lane on")] Guid boardId,
        [Description("The lane name")] string name,
        [Description("The lane's position (ordering value)")] int position,
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

        if (position == int.MaxValue)
        {
            return "Error: Position value is reserved.";
        }

        var lane = new Lane { Id = Guid.NewGuid(), BoardId = boardId, Name = name, Position = position };
        db.Lanes.Add(lane);
        await db.SaveChangesAsync(ct);
        broadcaster.PublishBoardUpdated(boardId);
        return JsonSerializer.Serialize(lane, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "update_lane", Destructive = false)]
    [Description("Update a lane's name and/or position. Requires administrator privileges. Archive lanes cannot be modified. Position int.MaxValue is reserved; a position already taken by another lane on the board is a conflict.")]
    public async Task<string> UpdateLaneAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the lane to update")] Guid laneId,
        [Description("The new lane name (optional)")] string? name = null,
        [Description("The new position (optional)")] int? position = null,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireAdminLevelAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var lane = await db.Lanes.FindAsync([laneId], ct);
        if (lane is null)
        {
            return "Error: Lane not found.";
        }

        if (lane.IsArchiveLane)
        {
            return "Error: Archive lanes cannot be modified.";
        }

        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Error: Name cannot be empty.";
            }

            lane.Name = name;
        }

        if (position is not null)
        {
            var newPos = position.Value;

            if (newPos == int.MaxValue)
            {
                return "Error: Position value is reserved.";
            }

            if (await db.Lanes.AnyAsync(l => l.BoardId == lane.BoardId && l.Position == newPos && l.Id != laneId, ct))
            {
                return "Error: Position already taken by another lane.";
            }

            lane.Position = newPos;
        }

        await db.SaveChangesAsync(ct);
        broadcaster.PublishBoardUpdated(lane.BoardId);
        return JsonSerializer.Serialize(lane, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "delete_lane", Destructive = true)]
    [Description("Delete a lane. Requires administrator privileges. Archive lanes cannot be deleted, and a lane must be empty (no cards) before it can be deleted.")]
    public async Task<string> DeleteLaneAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the lane to delete")] Guid laneId,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireAdminLevelAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var lane = await db.Lanes.FindAsync([laneId], ct);
        if (lane is null)
        {
            return "Error: Lane not found.";
        }

        if (lane.IsArchiveLane)
        {
            return "Error: Archive lanes cannot be deleted.";
        }

        if (await db.Cards.AnyAsync(c => c.LaneId == laneId, ct))
        {
            return "Error: Lane must be empty.";
        }

        var boardId = lane.BoardId;
        db.Lanes.Remove(lane);
        await db.SaveChangesAsync(ct);
        broadcaster.PublishBoardUpdated(boardId);
        return "Lane deleted.";
    }
}
