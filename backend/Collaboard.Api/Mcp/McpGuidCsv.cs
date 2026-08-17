using System.Text.Json;

namespace Collaboard.Api.Mcp;

// Shared GUID-CSV token splitting for the MCP tool surface. MCP tool params don't
// bind native arrays cleanly via the SDK, so GUID lists arrive as a string in one
// of two shapes (permissiveness):
//   1. Comma-separated GUIDs ("guid1,guid2") — the documented contract.
//   2. JSON-string array ("[\"guid1\",\"guid2\"]") — what some MCP-host clients
//      forward when the LLM emits an array literal for a string parameter.
//
// The token-split (JSON-array detection + CSV fallback) was byte-identical in
// McpLabelParsing and PruneTools (TryParseJsonStringArray copied verbatim across
// both). Extracted here on the McpLabelParsing precedent so the split lives
// in one place. Each caller keeps its own GUID-parse loop because the two surfaces
// emit different per-token error wording ("Invalid label ID format" vs "Invalid ID
// format") that is asserted by their tests — a deliberate per-surface contract, not
// drift, so it stays caller-side.
internal static class McpGuidCsv
{
    // Splits value into its raw string tokens, interpreting a JSON-string array if
    // value looks like one, otherwise splitting on commas (empty entries removed,
    // entries trimmed). Callers parse each token to a GUID with their own error
    // wording.
    public static string[] SplitTokens(string value) =>
        TryParseJsonStringArray(value, out var jsonArrayParts)
            ? jsonArrayParts
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Attempts to interpret value as a JSON string array. Returns true and populates
    // parts on success; returns false (parts = []) if value is not a well-formed JSON
    // array or fails to deserialize.
    private static bool TryParseJsonStringArray(string value, out string[] parts)
    {
        parts = [];
        var trimmed = value.AsSpan().Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']')
        {
            return false;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<string[]>(value);
            if (deserialized is null)
            {
                return false;
            }

            parts = [.. deserialized
                .Where(static s => !string.IsNullOrWhiteSpace(s))
                .Select(static s => s.Trim())];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
