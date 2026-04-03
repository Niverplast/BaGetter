using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGetter.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceEntraGroupWithAppRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EntraGroupId",
                table: "Groups",
                newName: "AppRoleValue");

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "FeedPermissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_AppRoleValue",
                table: "Groups",
                column: "AppRoleValue",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Groups_AppRoleValue",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "FeedPermissions");

            migrationBuilder.RenameColumn(
                name: "AppRoleValue",
                table: "Groups",
                newName: "EntraGroupId");
        }
    }
}
