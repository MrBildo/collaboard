using Collaboard.Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CardHistoryEndpoints
{
    public static RouteGroupBuilder MapCardHistoryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/cards/{id:guid}/history", async (BoardDbContext db, Guid id, string? field, string? format, int? from, int? to, CancellationToken ct) =>
        {
            // Reading history needs no permission beyond reading the card, and every authenticated
            // user can read every card on this board model — so RequireAuth is the whole gate. If
            // board-level membership ever lands, this read follows the card's rule, not its own.
            if (!await db.Cards.AnyAsync(c => c.Id == id, ct))
            {
                return Results.NotFound();
            }

            var (resolvedField, fieldError) = CardHistoryHelper.ResolveField(field);
            if (fieldError is not null)
            {
                return Results.BadRequest(fieldError);
            }

            // REST defaults to both: a browser client rendering the trail wants the diff to show and
            // the full text one interaction away, and it is not paying MCP's token cost. The MCP
            // tool defaults to diff instead.
            if (!CardHistoryBuilder.TryParseFormat(format, CardHistoryFormat.Both, out var resolvedFormat))
            {
                return Results.BadRequest(CardHistoryBuilder.FormatError);
            }

            // A lone from or to is a caller error, not a half-specified range to guess at.
            if (from.HasValue != to.HasValue)
            {
                return Results.BadRequest("from and to must be supplied together.");
            }

            if (from.HasValue && to.HasValue)
            {
                var (pair, pairError) = await CardHistoryBuilder.BuildPairAsync(db, id, resolvedField!, resolvedFormat, from.Value, to.Value, ct);

                return pairError is not null
                    ? Results.BadRequest(pairError)
                    : Results.Ok(pair);
            }

            var trail = await CardHistoryBuilder.BuildTrailAsync(db, id, resolvedField!, resolvedFormat, ct);
            return Results.Ok(trail);
        }).RequireAuth();

        return group;
    }
}
