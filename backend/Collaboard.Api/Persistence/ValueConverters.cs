using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Collaboard.Api.Persistence;

// Value converters shared across more than one entity configuration. A single instance is reused
// for every property it maps so the storage shape stays uniform model-wide.
internal static class ValueConverters
{
    // SQLite's default DateTimeOffset mapping cannot be translated when the comparison appears
    // in a nested query position (correlated sub-query, set operation), which broke the get_cards
    // `since` activity filter. Storing DateTimeOffset as a normalized-UTC round-trippable ISO-8601
    // string keeps the column TEXT (no column-type migration) while making `>=` a plain string
    // comparison SQLite translates in any position. "O" on a UTC DateTimeOffset is fixed-width and
    // lexicographically ordered, so string ordering matches chronological ordering.
    //
    // Applied to every DateTimeOffset column in the model (see the per-entity configurations) — not
    // just the columns the `since` filter touches — so the storage format is uniform and any future
    // nested date predicate translates too. Column stays TEXT: a format change, not a column-type
    // change.
    internal static readonly ValueConverter<DateTimeOffset, string> SortableUtc = new
    (
        v => v.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        v => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    );
}
