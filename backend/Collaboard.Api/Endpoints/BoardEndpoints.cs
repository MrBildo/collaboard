using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class BoardEndpoints
{
    public static RouteGroupBuilder MapBoardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/board", async (BoardDbContext db, HttpContext http) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var lanes = await db.Lanes.OrderBy(x => x.Position).ToListAsync();
            var cards = await db.Cards.OrderBy(x => x.LaneId).ThenBy(x => x.Position).ToListAsync();
            return Results.Ok(new { lanes, cards });
        });

        return group;
    }
}
