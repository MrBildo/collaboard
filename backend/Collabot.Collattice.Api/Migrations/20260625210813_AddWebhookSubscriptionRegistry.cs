using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collabot.Collattice.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookSubscriptionRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionId",
                table: "WebhookDeliveryAttempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Secret = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventTypes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveryAttempts_SubscriptionId_AttemptedAtUtc",
                table: "WebhookDeliveryAttempts",
                columns: new[] { "SubscriptionId", "AttemptedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveryAttempts_WebhookSubscriptions_SubscriptionId",
                table: "WebhookDeliveryAttempts",
                column: "SubscriptionId",
                principalTable: "WebhookSubscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveryAttempts_WebhookSubscriptions_SubscriptionId",
                table: "WebhookDeliveryAttempts");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveryAttempts_SubscriptionId_AttemptedAtUtc",
                table: "WebhookDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "WebhookDeliveryAttempts");
        }
    }
}
