using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGetter.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddCachedFromToPackage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CachedFrom",
                table: "Packages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CachedFrom",
                table: "Packages");
        }
    }
}
