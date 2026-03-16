using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations;

/// <inheritdoc />
public partial class AddForeignKeysAndIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Clean up orphaned records before adding FK constraints
        migrationBuilder.Sql("DELETE FROM Lanes WHERE BoardId NOT IN (SELECT Id FROM Boards);");
        migrationBuilder.Sql("DELETE FROM CardSizes WHERE BoardId NOT IN (SELECT Id FROM Boards);");
        migrationBuilder.Sql("DELETE FROM Labels WHERE BoardId NOT IN (SELECT Id FROM Boards);");
        migrationBuilder.Sql("DELETE FROM Cards WHERE LaneId NOT IN (SELECT Id FROM Lanes);");
        migrationBuilder.Sql("DELETE FROM Cards WHERE SizeId NOT IN (SELECT Id FROM CardSizes);");
        migrationBuilder.Sql("DELETE FROM Cards WHERE CreatedByUserId NOT IN (SELECT Id FROM Users);");
        migrationBuilder.Sql("DELETE FROM Comments WHERE CardId NOT IN (SELECT Id FROM Cards);");
        migrationBuilder.Sql("DELETE FROM Comments WHERE UserId NOT IN (SELECT Id FROM Users);");
        migrationBuilder.Sql("DELETE FROM CardLabels WHERE CardId NOT IN (SELECT Id FROM Cards);");
        migrationBuilder.Sql("DELETE FROM CardLabels WHERE LabelId NOT IN (SELECT Id FROM Labels);");
        migrationBuilder.Sql("DELETE FROM Attachments WHERE CardId NOT IN (SELECT Id FROM Cards);");
        migrationBuilder.Sql("DELETE FROM Attachments WHERE AddedByUserId NOT IN (SELECT Id FROM Users);");

        migrationBuilder.CreateIndex(
            name: "IX_Comments_UserId",
            table: "Comments",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_Cards_CreatedByUserId",
            table: "Cards",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Cards_LastUpdatedByUserId",
            table: "Cards",
            column: "LastUpdatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Cards_SizeId",
            table: "Cards",
            column: "SizeId");

        migrationBuilder.CreateIndex(
            name: "IX_CardLabels_LabelId",
            table: "CardLabels",
            column: "LabelId");

        migrationBuilder.CreateIndex(
            name: "IX_Attachments_AddedByUserId",
            table: "Attachments",
            column: "AddedByUserId");

        migrationBuilder.AddForeignKey(
            name: "FK_Attachments_Cards_CardId",
            table: "Attachments",
            column: "CardId",
            principalTable: "Cards",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_Attachments_Users_AddedByUserId",
            table: "Attachments",
            column: "AddedByUserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_CardLabels_Cards_CardId",
            table: "CardLabels",
            column: "CardId",
            principalTable: "Cards",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_CardLabels_Labels_LabelId",
            table: "CardLabels",
            column: "LabelId",
            principalTable: "Labels",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_Cards_CardSizes_SizeId",
            table: "Cards",
            column: "SizeId",
            principalTable: "CardSizes",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Cards_Lanes_LaneId",
            table: "Cards",
            column: "LaneId",
            principalTable: "Lanes",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_Cards_Users_CreatedByUserId",
            table: "Cards",
            column: "CreatedByUserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_CardSizes_Boards_BoardId",
            table: "CardSizes",
            column: "BoardId",
            principalTable: "Boards",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_Comments_Cards_CardId",
            table: "Comments",
            column: "CardId",
            principalTable: "Cards",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_Comments_Users_UserId",
            table: "Comments",
            column: "UserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Labels_Boards_BoardId",
            table: "Labels",
            column: "BoardId",
            principalTable: "Boards",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_Lanes_Boards_BoardId",
            table: "Lanes",
            column: "BoardId",
            principalTable: "Boards",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Attachments_Cards_CardId",
            table: "Attachments");

        migrationBuilder.DropForeignKey(
            name: "FK_Attachments_Users_AddedByUserId",
            table: "Attachments");

        migrationBuilder.DropForeignKey(
            name: "FK_CardLabels_Cards_CardId",
            table: "CardLabels");

        migrationBuilder.DropForeignKey(
            name: "FK_CardLabels_Labels_LabelId",
            table: "CardLabels");

        migrationBuilder.DropForeignKey(
            name: "FK_Cards_CardSizes_SizeId",
            table: "Cards");

        migrationBuilder.DropForeignKey(
            name: "FK_Cards_Lanes_LaneId",
            table: "Cards");

        migrationBuilder.DropForeignKey(
            name: "FK_Cards_Users_CreatedByUserId",
            table: "Cards");

        migrationBuilder.DropForeignKey(
            name: "FK_CardSizes_Boards_BoardId",
            table: "CardSizes");

        migrationBuilder.DropForeignKey(
            name: "FK_Comments_Cards_CardId",
            table: "Comments");

        migrationBuilder.DropForeignKey(
            name: "FK_Comments_Users_UserId",
            table: "Comments");

        migrationBuilder.DropForeignKey(
            name: "FK_Labels_Boards_BoardId",
            table: "Labels");

        migrationBuilder.DropForeignKey(
            name: "FK_Lanes_Boards_BoardId",
            table: "Lanes");

        migrationBuilder.DropIndex(
            name: "IX_Comments_UserId",
            table: "Comments");

        migrationBuilder.DropIndex(
            name: "IX_Cards_CreatedByUserId",
            table: "Cards");

        migrationBuilder.DropIndex(
            name: "IX_Cards_LastUpdatedByUserId",
            table: "Cards");

        migrationBuilder.DropIndex(
            name: "IX_Cards_SizeId",
            table: "Cards");

        migrationBuilder.DropIndex(
            name: "IX_CardLabels_LabelId",
            table: "CardLabels");

        migrationBuilder.DropIndex(
            name: "IX_Attachments_AddedByUserId",
            table: "Attachments");
    }
}
