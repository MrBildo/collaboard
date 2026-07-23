using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json.Serialization;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

// Read projection for card field history, shared by REST GET /cards/{id}/history and the MCP
// get_card_history tool so the two surfaces return the identical shape by construction.
//
// Diffs are computed here on read, never stored. Rows hold whole values, so any pair of revisions
// can be compared (which the from/to read needs) and there is no cached representation to fall out
// of step with the values it describes. Card descriptions are small and the trail is short.
internal static class CardHistoryBuilder
{
    public const string FormatError = "format must be one of: diff, full, both.";

    private static readonly FrozenDictionary<string, CardHistoryFormat> _formatNames =
        new Dictionary<string, CardHistoryFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["diff"] = CardHistoryFormat.Diff,
            ["full"] = CardHistoryFormat.Full,
            ["both"] = CardHistoryFormat.Both,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static async Task<CardHistoryResult> BuildTrailAsync
    (
        BoardDbContext db,
        Guid cardId,
        string field,
        CardHistoryFormat format,
        CancellationToken ct = default
    )
    {
        var rows = await db.CardFieldHistories
            .Where(h => h.CardId == cardId && h.Field == field)
            .OrderBy(h => h.Revision)
                .ToListAsync(ct);

        var editorNames = await ResolveEditorNamesAsync(db, rows, ct);

        List<CardHistoryEntry> entries = [];

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            // The oldest row has nothing older to diff against. Empty string, not null: a consumer
            // can tell "nothing to show here" apart from "diffs were not requested."
            var previousValue = index == 0 ? null : rows[index - 1].Value;

            entries.Add(BuildEntry(row, previousValue, editorNames, format));
        }

        // Newest first — the question a reader almost always has is "what changed most recently?"
        entries.Reverse();

        return new CardHistoryResult(cardId, field, entries);
    }

    public static async Task<(CardHistoryPairResult? Result, string? Error)> BuildPairAsync
    (
        BoardDbContext db,
        Guid cardId,
        string field,
        CardHistoryFormat format,
        int from,
        int to,
        CancellationToken ct = default
    )
    {
        var rows = await db.CardFieldHistories
            .Where(h => h.CardId == cardId && h.Field == field && (h.Revision == from || h.Revision == to))
                .ToListAsync(ct);

        var fromRow = rows.FirstOrDefault(r => r.Revision == from);
        if (fromRow is null)
        {
            return (null, MissingRevisionError(from, field));
        }

        var toRow = rows.FirstOrDefault(r => r.Revision == to);
        if (toRow is null)
        {
            return (null, MissingRevisionError(to, field));
        }

        // from and to are compared in the order given, so asking for a later-to-earlier pair
        // yields the diff that would undo the change rather than an error.
        var diff = IncludesDiff(format) ? UnifiedDiff.Render(fromRow.Value, toRow.Value) : null;
        var fromValue = IncludesValue(format) ? fromRow.Value : null;
        var toValue = IncludesValue(format) ? toRow.Value : null;

        return (new CardHistoryPairResult(cardId, field, from, to, diff, fromValue, toValue), null);
    }

    public static bool TryParseFormat(string? requested, CardHistoryFormat fallback, out CardHistoryFormat format)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            format = fallback;
            return true;
        }

        if (_formatNames.TryGetValue(requested.Trim(), out var parsed))
        {
            format = parsed;
            return true;
        }

        format = fallback;
        return false;
    }

    private static CardHistoryEntry BuildEntry
    (
        CardFieldHistory row,
        string? previousValue,
        IReadOnlyDictionary<Guid, string> editorNames,
        CardHistoryFormat format
    )
    {
        string? editorName = null;
        if (row.EditedByUserId is not null)
        {
            editorName = editorNames.GetValueOrDefault(row.EditedByUserId.Value);
        }

        string? value = null;
        if (IncludesValue(format))
        {
            value = row.Value;
        }

        string? diff = null;
        if (IncludesDiff(format))
        {
            diff = previousValue is null ? string.Empty : UnifiedDiff.Render(previousValue, row.Value);
        }

        return new CardHistoryEntry
        (
            row.Revision,
            row.EditedByUserId,
            editorName,
            row.EditedAtUtc,
            value,
            diff
        );
    }

    private static async Task<Dictionary<Guid, string>> ResolveEditorNamesAsync
    (
        BoardDbContext db,
        List<CardFieldHistory> rows,
        CancellationToken ct
    )
    {
        var editorIds = rows
            .Where(r => r.EditedByUserId is not null)
            .Select(r => r.EditedByUserId!.Value)
            .Distinct()
                .ToList();

        // A trail whose only row is the un-attributed oldest revision needs no user lookup at all.
        return editorIds.Count == 0
            ? []
            : await db.Users
                .Where(u => editorIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }

    private static string MissingRevisionError(int revision, string field) =>
        string.Create(CultureInfo.InvariantCulture, $"Revision {revision} not found in this card's {field} history.");

    private static bool IncludesValue(CardHistoryFormat format) =>
        format is CardHistoryFormat.Full or CardHistoryFormat.Both;

    private static bool IncludesDiff(CardHistoryFormat format) =>
        format is CardHistoryFormat.Diff or CardHistoryFormat.Both;
}

internal enum CardHistoryFormat
{
    Diff,
    Full,
    Both,
}

// One revision in a card field's trail. Value and Diff drop off the wire entirely when the caller's
// format did not ask for them, so format=diff (the MCP default) carries no null padding. The
// attribution fields stay on the wire even when null — "who wrote this is unknown" is information a
// reader needs, and only the trail's oldest revision carries it.
internal record CardHistoryEntry
(
    int Revision,
    Guid? EditedByUserId,
    string? EditedByName,
    DateTimeOffset? EditedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Diff
);

internal record CardHistoryResult(Guid CardId, string Field, List<CardHistoryEntry> Entries);

internal record CardHistoryPairResult
(
    Guid CardId,
    string Field,
    int From,
    int To,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Diff,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FromValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToValue
);
