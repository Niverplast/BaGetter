using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGetter.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEmailAndTokenExpiryNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpiryNotificationThresholdDays",
                table: "PersonalAccessTokens",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExpiryNotificationThresholdDays",
                table: "PersonalAccessTokens");
        }
    }
}
