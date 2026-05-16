using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations;

/// <inheritdoc />
public partial class NormalizeDateTimeOffsetStorageFormat : Migration
{
    // #234: DateTimeOffset columns now persist via a value converter that
    // writes the round-trippable "O" UTC format (T-separated). Rows written
    // before this change used the SQLite provider's default format
    // (space-separated). Both parse back correctly, but the get_cards `since`
    // filter compares the stored TEXT lexically in SQL — and a space (0x20)
    // sorts before 'T' (0x54), so an un-normalized old-format row would be
    // mis-ordered against the new-format `since` parameter and silently
    // dropped from / wrongly included in activity results. This migration
    // rewrites every existing timestamp into the new format so ordering is
    // uniform. Every timestamp in this codebase is written as UTC
    // (DateTimeOffset.UtcNow), so the only delta is the date/time separator;
    // the offset is already +00:00. Idempotent — rows already T-separated are
    // left untouched by the WHERE guard.
    private static readonly (string Table, string Column)[] _timestampColumns =
    [
        ("Boards", "CreatedAtUtc"),
        ("Cards", "CreatedAtUtc"),
        ("Cards", "LastUpdatedAtUtc"),
        ("Comments", "LastUpdatedAtUtc"),
        ("Attachments", "AddedAtUtc"),
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var (table, column) in _timestampColumns)
        {
            // Replace the single separator char at position 11 (1-indexed) with
            // 'T', only for rows still in the old space-separated form.
            migrationBuilder.Sql(
                $"""
                UPDATE "{table}"
                SET "{column}" = substr("{column}", 1, 10) || 'T' || substr("{column}", 12)
                WHERE substr("{column}", 11, 1) = ' ';
                """);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Revert to the old space-separated separator so a rollback leaves the
        // store readable by the pre-converter mapping.
        foreach (var (table, column) in _timestampColumns)
        {
            migrationBuilder.Sql(
                $"""
                UPDATE "{table}"
                SET "{column}" = substr("{column}", 1, 10) || ' ' || substr("{column}", 12)
                WHERE substr("{column}", 11, 1) = 'T';
                """);
        }
    }
}
