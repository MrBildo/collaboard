using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Mcp;

public class McpAuthService(IHttpContextAccessor httpContextAccessor, BoardDbContext db)
{
    public async Task<(BoardUser? User, string? Error)> RequireUserAsync(CancellationToken ct = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return (null, "Error: No HTTP context available.");
        }

        var userKey = httpContext.Request.Headers[AuthExtensions.UserKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(userKey))
        {
            return (null, "Error: Authentication required. Provide X-User-Key header.");
        }

        var user = await db.Users.SingleOrDefaultAsync(x => x.AuthKey == userKey && x.IsActive, ct);
        if (user is null)
        {
            return (null, "Error: Invalid or inactive user key.");
        }

        return (user, null);
    }
}
