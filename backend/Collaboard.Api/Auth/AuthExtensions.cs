using Collaboard.Api.Models;

using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Auth;

public static class AuthExtensions
{
    public const string UserKeyHeader = "X-User-Key";

    public static async Task<BoardUser?> ResolveUserAsync(this HttpContext context, BoardDbContext db)
    {
        var userKey = context.Request.Headers[UserKeyHeader].ToString();
        return string.IsNullOrWhiteSpace(userKey)
            ? null
            : await db.Users.SingleOrDefaultAsync(x => x.AuthKey == userKey && x.IsActive);
    }

    public static async Task<IResult?> RequireRoleAsync(this HttpContext context, BoardDbContext db, params UserRole[] roles)
    {
        var user = await context.ResolveUserAsync(db);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        context.Items[nameof(BoardUser)] = user;
        return roles.Contains(user.Role) ? null : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    public static BoardUser CurrentUser(this HttpContext context) => (BoardUser)context.Items[nameof(BoardUser)]!;
}
