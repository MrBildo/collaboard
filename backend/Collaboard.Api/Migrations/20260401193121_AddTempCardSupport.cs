using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations;

/// <inheritdoc />
public partial class AddTempCardSupport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Cards_BoardId_Number",
            table: "Cards");

        migrationBuilder.AddColumn<bool>(
            name: "IsTemp",
            table: "Cards",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_Cards_BoardId_Number",
            table: "Cards",
            columns: ["BoardId", "Number"],
            unique: true,
            filter: "\"Number\" > 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Cards_BoardId_Number",
            table: "Cards");

        migrationBuilder.DropColumn(
            name: "IsTemp",
            table: "Cards");

        migrationBuilder.CreateIndex(
            name: "IX_Cards_BoardId_Number",
            table: "Cards",
            columns: ["BoardId", "Number"],
            unique: true);
    }
}
