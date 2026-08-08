using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentCreatedAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedAtUtc",
                table: "Comments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Backfill for existing comments: they never recorded a creation time — the only
            // timestamp on record is LastUpdatedAtUtc, which every edit overwrites. Copy that value
            // into CreatedAtUtc for every existing row: exact for a never-edited comment, and a
            // one-time, disclosed approximation for an edited one (whose true creation time is
            // unrecoverable). Both columns store the same sortable-UTC TEXT format, so this is a
            // verbatim value copy — no reformatting, no parse. The AddColumn default above is only a
            // transient placeholder satisfying the NOT NULL constraint for existing rows; this
            // statement overwrites it on every one, and new comments set CreatedAtUtc at posting.
            migrationBuilder.Sql("""
                UPDATE "Comments"
                SET "CreatedAtUtc" = "LastUpdatedAtUtc";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Comments");
        }
    }
}
