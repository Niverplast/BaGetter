using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGetter.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class MoveIsAdminToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "FeedPermissions");

            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);
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
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
