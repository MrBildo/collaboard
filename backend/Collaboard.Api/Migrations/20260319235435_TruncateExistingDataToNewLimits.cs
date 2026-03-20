using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations;

/// <inheritdoc />
public partial class TruncateExistingDataToNewLimits : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE Boards SET Name = SUBSTR(Name, 1, 80) WHERE LENGTH(Name) > 80;");
        migrationBuilder.Sql("UPDATE Boards SET Slug = SUBSTR(Slug, 1, 80) WHERE LENGTH(Slug) > 80;");
        migrationBuilder.Sql("UPDATE Lanes SET Name = SUBSTR(Name, 1, 40) WHERE LENGTH(Name) > 40;");
        migrationBuilder.Sql("UPDATE Cards SET Name = SUBSTR(Name, 1, 120) WHERE LENGTH(Name) > 120;");
        // Labels have a unique constraint on (BoardId, Name).
        // Simple SUBSTR could create duplicates, so append a numeric
        // suffix to collisions by using the rowid as a tiebreaker.
        migrationBuilder.Sql("""
            UPDATE Labels
            SET Name = SUBSTR(Name, 1, 27) || '-' || SUBSTR(HEX(RANDOMBLOB(1)), 1, 2)
            WHERE LENGTH(Name) > 30
              AND EXISTS (
                SELECT 1 FROM Labels AS other
                WHERE other.BoardId = Labels.BoardId
                  AND other.Id != Labels.Id
                  AND SUBSTR(other.Name, 1, 30) = SUBSTR(Labels.Name, 1, 30)
              );
            """);
        migrationBuilder.Sql("UPDATE Labels SET Name = SUBSTR(Name, 1, 30) WHERE LENGTH(Name) > 30;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data truncation is not reversible
    }
}
