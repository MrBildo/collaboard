using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookDeliveryAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AttemptedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveryAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveryAttempts_BoardId_AttemptedAtUtc",
                table: "WebhookDeliveryAttempts",
                columns: new[] { "BoardId", "AttemptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveryAttempts_EventId",
                table: "WebhookDeliveryAttempts",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookDeliveryAttempts");
        }
    }
}
