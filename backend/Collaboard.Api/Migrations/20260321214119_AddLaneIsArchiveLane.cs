using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations;

/// <inheritdoc />
public partial class AddLaneIsArchiveLane : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsArchiveLane",
            table: "Lanes",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        // Data migration: create an archive lane for each existing board
        migrationBuilder.Sql("""
            INSERT INTO Lanes (Id, BoardId, Name, Position, IsArchiveLane)
            SELECT
                LOWER(HEX(RANDOMBLOB(4)) || '-' || HEX(RANDOMBLOB(2)) || '-4' ||
                    SUBSTR(HEX(RANDOMBLOB(2)), 2) || '-' ||
                    SUBSTR('89ab', 1 + (ABS(RANDOM()) % 4), 1) ||
                    SUBSTR(HEX(RANDOMBLOB(2)), 2) || '-' ||
                    HEX(RANDOMBLOB(6))),
                b.Id,
                'Archive',
                2147483647,
                1
            FROM Boards b
            WHERE NOT EXISTS (
                SELECT 1 FROM Lanes l WHERE l.BoardId = b.Id AND l.IsArchiveLane = 1
            );
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Remove archive lanes before dropping the column
        migrationBuilder.Sql("DELETE FROM Lanes WHERE IsArchiveLane = 1;");

        migrationBuilder.DropColumn(
            name: "IsArchiveLane",
            table: "Lanes");
    }
}
