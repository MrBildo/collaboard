using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Mcp;

// Shared label-ID parsing and cross-board validation used by CardTools and
// BulkCardTools. Extracted from the byte-identical private copies that existed
// in both files. Both callers already pass boardId, so there was never
// a "divergent call shapes" reason to keep them separate. The CSV/JSON-array
// token split is shared further via McpGuidCsv.
internal static class McpLabelParsing
{
    // Parses labelIds (comma-separated GUIDs or a JSON array string) and
    // validates that every resolved label belongs to the given board. Returns
    // a populated list on success, or a non-null error string on failure.
    //
    // Accepts two input shapes for labelIds (permissiveness):
    //   1. Comma-separated GUIDs ("guid1,guid2") — the documented contract.
    //   2. JSON-string array ("[\"guid1\",\"guid2\"]") — what some MCP-host
    //      clients forward when the LLM emits an array literal for a string
    //      parameter.
    // The schema stays a string; the handler is permissive about which shape
    // arrives. Null or whitespace input returns an empty list with no error.
    public static async Task<(List<Guid> LabelIds, string? Error)> ParseAndValidateLabelIdsAsync
    (
        BoardDbContext db,
        string? labelIds,
        Guid boardId,
        CancellationToken ct
    )
    {
        List<Guid> parsedIds = [];
        if (string.IsNullOrWhiteSpace(labelIds))
        {
            return (parsedIds, null);
        }

        var parts = McpGuidCsv.SplitTokens(labelIds);

        foreach (var part in parts)
        {
            if (!Guid.TryParse(part, out var parsedId))
            {
                return (parsedIds, $"Error: Invalid label ID format: '{part}'. Expected a GUID.");
            }

            parsedIds.Add(parsedId);
        }

        var validLabels = await db.Labels
            .Where(l => parsedIds.Contains(l.Id) && l.BoardId == boardId)
                .Select(l => l.Id)
                    .ToListAsync(ct);

        var invalidIds = parsedIds.Except(validLabels).ToList();
        if (invalidIds.Count > 0)
        {
            return (parsedIds, $"Error: Labels not found or not on the same board: {string.Join(", ", invalidIds)}");
        }

        return (parsedIds, null);
    }
}
