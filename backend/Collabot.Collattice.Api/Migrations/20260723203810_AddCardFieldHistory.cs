using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collabot.Collattice.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCardFieldHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardFieldHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    EditedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EditedAtUtc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardFieldHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardFieldHistories_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardFieldHistories_Users_EditedByUserId",
                        column: x => x.EditedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardFieldHistories_CardId_Field_Revision",
                table: "CardFieldHistories",
                columns: new[] { "CardId", "Field", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardFieldHistories_EditedByUserId",
                table: "CardFieldHistories",
                column: "EditedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardFieldHistories");
        }
    }
}
