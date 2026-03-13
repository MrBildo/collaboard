using Collaboard.Api.Models;

using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Auth;

public static class AuthExtensions
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string UserKeyHeader = "X-User-Key";

    public static async Task<BoardUser?> ResolveUserAsync(this HttpContext context, BoardDbContext db)
    {
        var apiKey = context.Request.Headers[ApiKeyHeader].ToString();
        var expectedApiKey = context.RequestServices.GetRequiredService<IConfiguration>().GetValue<string>("Security:ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey) || !string.Equals(apiKey, expectedApiKey, StringComparison.Ordinal))
        {
            return null;
        }

        var userKey = context.Request.Headers[UserKeyHeader].ToString();
        return string.IsNullOrWhiteSpace(userKey) ? null : await db.Users.SingleOrDefaultAsync(x => x.AuthKey == userKey);
    }

    public static async Task<IResult?> RequireRoleAsync(this HttpContext context, BoardDbContext db, params UserRole[] roles)
    {
        var user = await context.ResolveUserAsync(db);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        context.Items[nameof(BoardUser)] = user;
        return roles.Contains(user.Role) ? null : Results.Forbid();
    }

    public static BoardUser CurrentUser(this HttpContext context) => (BoardUser)context.Items[nameof(BoardUser)]!;
}
