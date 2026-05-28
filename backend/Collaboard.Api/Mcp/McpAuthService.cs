using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Mcp;

public class McpAuthService(BoardDbContext db)
{
    public async Task<(BoardUser? User, string? Error)> RequireUserAsync(string authKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authKey))
        {
            return (null, "Error: authKey is required.");
        }

        var user = await db.Users.SingleOrDefaultAsync(x => x.AuthKey == authKey && x.IsActive, ct);
        if (user is null)
        {
            return (null, "Error: Invalid or inactive auth key.");
        }

        return (user, null);
    }

    // Card #243 Phase 2: admin-level role check for MCP tools.
    // Both Administrator and AgentAdministrator are admin-level on MCP;
    // strict-Administrator-only operations (board delete, prune-delete,
    // user CRUD) are intentionally absent from the MCP surface entirely,
    // so no strict-admin variant ships here. If a future tool needs
    // strict admin, add the variant then.
    public async Task<(BoardUser? User, string? Error)> RequireAdminLevelAsync(string authKey, CancellationToken ct = default)
    {
        var (user, error) = await RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return (null, error);
        }

        if (!IsAdminLevel(user!))
        {
            return (null, "Error: This operation requires administrator privileges.");
        }

        return (user, null);
    }

    // Card #243 Phase 2: the own-or-admin checks in CommentTools and
    // AttachmentTools widen from "Administrator" to "Administrator or
    // AgentAdministrator." Centralized here so future admin-level call
    // sites stay consistent.
    public static bool IsAdminLevel(BoardUser user)
        => user.Role is UserRole.Administrator or UserRole.AgentAdministrator;
}
