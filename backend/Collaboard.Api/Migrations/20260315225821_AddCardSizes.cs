using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations;

/// <inheritdoc />
public partial class AddCardSizes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Create CardSizes table
        migrationBuilder.CreateTable(
            name: "CardSizes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                BoardId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Ordinal = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CardSizes", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CardSizes_BoardId_Name",
            table: "CardSizes",
            columns: ["BoardId", "Name"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CardSizes_BoardId_Ordinal",
            table: "CardSizes",
            columns: ["BoardId", "Ordinal"],
            unique: true);

        // 2. Seed S/M/L/XL per existing board and migrate Card.Size -> Card.SizeId
        migrationBuilder.Sql("""
            -- Seed default sizes for every existing board
            INSERT INTO CardSizes (Id, BoardId, Name, Ordinal)
            SELECT
                upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                b.Id, s.Name, s.Ordinal
            FROM Boards b
            CROSS JOIN (
                SELECT 'S' AS Name, 0 AS Ordinal
                UNION ALL SELECT 'M', 1
                UNION ALL SELECT 'L', 2
                UNION ALL SELECT 'XL', 3
            ) s;

            -- Add nullable SizeId column to Cards
            ALTER TABLE Cards ADD COLUMN SizeId TEXT NULL;

            -- Populate SizeId by matching Card.Size string to CardSize.Name via lane→board
            UPDATE Cards
            SET SizeId = (
                SELECT cs.Id
                FROM Lanes la
                JOIN CardSizes cs ON cs.BoardId = la.BoardId AND cs.Name = Cards.Size
                WHERE la.Id = Cards.LaneId
                LIMIT 1
            );

            -- Fallback: any unmatched cards get the "M" size of their board
            UPDATE Cards
            SET SizeId = (
                SELECT cs.Id
                FROM Lanes la
                JOIN CardSizes cs ON cs.BoardId = la.BoardId AND cs.Name = 'M'
                WHERE la.Id = Cards.LaneId
                LIMIT 1
            )
            WHERE SizeId IS NULL;
            """);

        // 3. SQLite table rebuild to make SizeId NOT NULL and drop the old Size column
        migrationBuilder.Sql("""
            CREATE TABLE Cards_new (
                Id TEXT NOT NULL CONSTRAINT PK_Cards PRIMARY KEY,
                Number INTEGER NOT NULL,
                Name TEXT NOT NULL,
                DescriptionMarkdown TEXT NOT NULL,
                SizeId TEXT NOT NULL,
                LaneId TEXT NOT NULL,
                Position INTEGER NOT NULL,
                CreatedByUserId TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                LastUpdatedByUserId TEXT NOT NULL,
                LastUpdatedAtUtc TEXT NOT NULL
            );

            INSERT INTO Cards_new (Id, Number, Name, DescriptionMarkdown, SizeId, LaneId, Position, CreatedByUserId, CreatedAtUtc, LastUpdatedByUserId, LastUpdatedAtUtc)
            SELECT Id, Number, Name, DescriptionMarkdown, SizeId, LaneId, Position, CreatedByUserId, CreatedAtUtc, LastUpdatedByUserId, LastUpdatedAtUtc
            FROM Cards;

            DROP TABLE Cards;
            ALTER TABLE Cards_new RENAME TO Cards;

            CREATE UNIQUE INDEX IX_Cards_Number ON Cards (Number);
            CREATE INDEX IX_Cards_LaneId_Position ON Cards (LaneId, Position);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CardSizes");

        migrationBuilder.Sql("""
            CREATE TABLE Cards_new (
                Id TEXT NOT NULL CONSTRAINT PK_Cards PRIMARY KEY,
                Number INTEGER NOT NULL,
                Name TEXT NOT NULL,
                DescriptionMarkdown TEXT NOT NULL,
                Size TEXT NOT NULL DEFAULT 'M',
                LaneId TEXT NOT NULL,
                Position INTEGER NOT NULL,
                CreatedByUserId TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                LastUpdatedByUserId TEXT NOT NULL,
                LastUpdatedAtUtc TEXT NOT NULL
            );

            INSERT INTO Cards_new (Id, Number, Name, DescriptionMarkdown, Size, LaneId, Position, CreatedByUserId, CreatedAtUtc, LastUpdatedByUserId, LastUpdatedAtUtc)
            SELECT Id, Number, Name, DescriptionMarkdown, 'M', LaneId, Position, CreatedByUserId, CreatedAtUtc, LastUpdatedByUserId, LastUpdatedAtUtc
            FROM Cards;

            DROP TABLE Cards;
            ALTER TABLE Cards_new RENAME TO Cards;

            CREATE UNIQUE INDEX IX_Cards_Number ON Cards (Number);
            CREATE INDEX IX_Cards_LaneId_Position ON Cards (LaneId, Position);
            """);
    }
}
