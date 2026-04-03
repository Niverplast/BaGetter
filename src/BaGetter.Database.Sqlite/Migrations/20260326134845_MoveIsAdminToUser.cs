using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGetter.Database.Sqlite.Migrations
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
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Migrate existing admin permissions: set IsAdmin on users who had IsAdmin=1 on any feed permission.
            // suppressTransaction is required because SQLite table rebuild (DropColumn) uses PRAGMA statements
            // that cannot run inside a transaction.
            migrationBuilder.Sql(@"
                UPDATE Users SET IsAdmin = 1
                WHERE Id IN (
                    SELECT PrincipalId FROM FeedPermissions
                    WHERE IsAdmin = 1 AND PrincipalType = 0
                )", suppressTransaction: true);

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
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
