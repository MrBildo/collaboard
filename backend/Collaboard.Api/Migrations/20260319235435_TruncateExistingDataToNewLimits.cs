using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class TruncateExistingDataToNewLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Boards SET Name = SUBSTR(Name, 1, 80) WHERE LENGTH(Name) > 80;");
            migrationBuilder.Sql("UPDATE Boards SET Slug = SUBSTR(Slug, 1, 80) WHERE LENGTH(Slug) > 80;");
            migrationBuilder.Sql("UPDATE Lanes SET Name = SUBSTR(Name, 1, 40) WHERE LENGTH(Name) > 40;");
            migrationBuilder.Sql("UPDATE Cards SET Name = SUBSTR(Name, 1, 120) WHERE LENGTH(Name) > 120;");
            migrationBuilder.Sql("UPDATE Labels SET Name = SUBSTR(Name, 1, 30) WHERE LENGTH(Name) > 30;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data truncation is not reversible
        }
    }
}
