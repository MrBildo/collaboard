using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations;

/// <inheritdoc />
public partial class BoardScopedCardNumbers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Cards_Number",
            table: "Cards");

        migrationBuilder.AddColumn<Guid>(
            name: "BoardId",
            table: "Cards",
            type: "TEXT",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        // Populate BoardId from Lane.BoardId for all existing cards
        migrationBuilder.Sql("""
            UPDATE Cards
            SET BoardId = (
                SELECT la.BoardId
                FROM Lanes la
                WHERE la.Id = Cards.LaneId
            )
            WHERE BoardId = '00000000-0000-0000-0000-000000000000';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Cards_BoardId_Number",
            table: "Cards",
            columns: ["BoardId", "Number"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Cards_BoardId_Number",
            table: "Cards");

        migrationBuilder.DropColumn(
            name: "BoardId",
            table: "Cards");

        migrationBuilder.CreateIndex(
            name: "IX_Cards_Number",
            table: "Cards",
            column: "Number",
            unique: true);
    }
}
