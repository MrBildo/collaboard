using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations;

/// <inheritdoc />
public partial class AddLastUpdatedByUserIdFK : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddForeignKey(
            name: "FK_Cards_Users_LastUpdatedByUserId",
            table: "Cards",
            column: "LastUpdatedByUserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Cards_Users_LastUpdatedByUserId",
            table: "Cards");
    }
}
