using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CardReorderHelper
{
    internal const int PositionGap = 10;

    public static async Task<int> MoveCardToLaneAsync(
        BoardDbContext db, CardItem card, Guid targetLaneId, int? index, CancellationToken ct = default)
    {
        var sourceLaneId = card.LaneId;

        var targetCards = await db.Cards
            .Where(c => c.LaneId == targetLaneId && c.Id != card.Id)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);

        var resolvedIndex = Math.Clamp(index ?? 0, 0, targetCards.Count);
        targetCards.Insert(resolvedIndex, card);

        for (var i = 0; i < targetCards.Count; i++)
        {
            targetCards[i].Position = i * PositionGap;
        }

        if (sourceLaneId != targetLaneId)
        {
            card.LaneId = targetLaneId;

            var sourceCards = await db.Cards
                .Where(c => c.LaneId == sourceLaneId && c.Id != card.Id)
                .OrderBy(c => c.Position)
                .ToListAsync(ct);

            for (var i = 0; i < sourceCards.Count; i++)
            {
                sourceCards[i].Position = i * PositionGap;
            }
        }

        return resolvedIndex;
    }
}
