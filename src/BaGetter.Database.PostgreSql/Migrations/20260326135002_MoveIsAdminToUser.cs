using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGetter.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class MoveIsAdminToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Migrate existing admin permissions: set IsAdmin on users who had IsAdmin=true on any feed permission.
            // Column names are quoted because PostgreSQL identifiers are case-sensitive when quoted.
            migrationBuilder.Sql(@"
                UPDATE ""Users"" SET ""IsAdmin"" = true
                WHERE ""Id"" IN (
                    SELECT ""PrincipalId"" FROM ""FeedPermissions""
                    WHERE ""IsAdmin"" = true AND ""PrincipalType"" = 0
                )");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "FeedPermissions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "FeedPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
