using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Auth;

public class RequireRoleFilter(params UserRole[] allowedRoles) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var db = httpContext.RequestServices.GetRequiredService<BoardDbContext>();

        var userKey = httpContext.Request.Headers[AuthExtensions.UserKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(userKey))
        {
            return Results.Unauthorized();
        }

        var user = await db.Users.SingleOrDefaultAsync(x => x.AuthKey == userKey && x.IsActive);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        httpContext.Items[nameof(BoardUser)] = user;

        return allowedRoles.Length > 0 && !allowedRoles.Contains(user.Role)
            ? Results.StatusCode(StatusCodes.Status403Forbidden)
            : await next(context);
    }
}
