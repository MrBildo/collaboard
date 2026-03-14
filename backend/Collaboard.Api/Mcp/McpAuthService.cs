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
}
