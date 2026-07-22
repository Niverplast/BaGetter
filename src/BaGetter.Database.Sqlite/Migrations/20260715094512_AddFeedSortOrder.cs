using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGetter.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Feeds",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Autofill SortOrder from the previous visible order (default feed first, then by Name, Id).
            migrationBuilder.Sql(
                "UPDATE Feeds SET SortOrder = r.rn FROM (SELECT Id, ROW_NUMBER() OVER (ORDER BY CASE WHEN Slug = 'default' THEN 0 ELSE 1 END, Name, Id) - 1 AS rn FROM Feeds) AS r WHERE Feeds.Id = r.Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Feeds");
        }
    }
}
