using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Mcp;

internal static class McpCardResolver
{
    public static async Task<(Guid? CardId, string? Error)> ResolveCardIdAsync(
        BoardDbContext db, Guid? cardId, long? cardNumber, CancellationToken ct)
    {
        if (cardId.HasValue && cardNumber.HasValue)
        {
            return (null, "Error: Provide either cardId or cardNumber, not both.");
        }

        if (!cardId.HasValue && !cardNumber.HasValue)
        {
            return (null, "Error: Provide either cardId or cardNumber.");
        }

        if (cardId.HasValue)
        {
            return (cardId.Value, null);
        }

        var card = await db.Cards.FirstOrDefaultAsync(c => c.Number == cardNumber, ct);
        return card is not null
            ? (card.Id, null)
            : (null, $"Error: Card #{cardNumber} not found.");
    }
}
