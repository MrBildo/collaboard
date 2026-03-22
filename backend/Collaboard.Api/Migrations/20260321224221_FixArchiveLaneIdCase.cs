using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations;

/// <inheritdoc />
public partial class FixArchiveLaneIdCase : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Fix archive lane IDs that were created with lowercase hex by the
        // AddLaneIsArchiveLane data migration. EF Core stores Guids as uppercase
        // TEXT in SQLite, so lowercase IDs cause FK constraint failures.
        migrationBuilder.Sql("UPDATE Lanes SET Id = UPPER(Id) WHERE IsArchiveLane = 1 AND Id != UPPER(Id);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Not reversible — uppercase IDs are correct
    }
}
