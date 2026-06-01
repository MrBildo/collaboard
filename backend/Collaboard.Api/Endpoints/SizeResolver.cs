using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

// Shared card-size resolution for the REST and MCP write paths. Resolves a size
// against a board in precedence order:
//   1. by id    — validate the id belongs to the board
//   2. by name  — match on name within the board
//   3. default  — the board's lowest-ordinal size
//
// Folds the triplicated MCP ResolveSizeAsync (CardTools + BulkCardTools) and gives
// REST card-create the by-name capability it previously lacked (#267 D2/D4). The
// error message is bare (no "Error: " prefix) so each front door applies its own
// idiom: REST maps it to Results.BadRequest verbatim; MCP prefixes "Error: " at the
// call site, matching the rest of the MCP surface. Callers that always supply an
// explicit size (the bulk update path) never reach the default branch.
internal static class SizeResolver
{
    public static async Task<(Guid? SizeId, string? Error)> ResolveAsync
    (
        BoardDbContext db,
        Guid boardId,
        Guid? sizeId,
        string? sizeName,
        CancellationToken ct
    )
    {
        if (sizeId.HasValue)
        {
            if (!await db.CardSizes.AnyAsync(s => s.Id == sizeId.Value && s.BoardId == boardId, ct))
            {
                return (null, "Size not found or does not belong to this board.");
            }

            return (sizeId.Value, null);
        }

        if (!string.IsNullOrWhiteSpace(sizeName))
        {
            var size = await db.CardSizes.FirstOrDefaultAsync(s => s.BoardId == boardId && s.Name == sizeName, ct);
            if (size is null)
            {
                return (null, $"Size '{sizeName}' not found on this board.");
            }

            return (size.Id, null);
        }

        var defaultSize = await db.CardSizes
            .Where(s => s.BoardId == boardId)
            .OrderBy(s => s.Ordinal)
                .FirstOrDefaultAsync(ct);
        if (defaultSize is null)
        {
            return (null, "Board has no sizes configured.");
        }

        return (defaultSize.Id, null);
    }
}
