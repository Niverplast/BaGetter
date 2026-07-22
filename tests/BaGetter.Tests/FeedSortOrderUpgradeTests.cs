using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BaGetter.Core.Configuration;
using BaGetter.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BaGetter.Tests;

/// <summary>
/// Verifies that the AddFeedSortOrder migration autofills the new SortOrder column from the
/// previous visible order (default feed first, then by Name) on a pre-existing database.
/// </summary>
public class FeedSortOrderUpgradeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public FeedSortOrderUpgradeTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BaGetterMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _dbPath = Path.Combine(tempDir, "sortorder-upgrade-test.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    [Fact]
    public async Task SortOrder_IsAutofilledInVisibleOrder_AfterMigration()
    {
        // Migrate up to just before AddFeedSortOrder: the default feed is already seeded and the
        // SortOrder column does not exist yet.
        await ApplyMigrationsUpto("20260701073051_AddUserEmailAndTokenExpiryNotification");

        // Insert two extra feeds out of alphabetical order, without a SortOrder value.
        SeedFeed("zebra", "Zebra");
        SeedFeed("apple", "Apple");

        // Run AddFeedSortOrder, exercising the real SQLite autofill SQL.
        await ApplyAllMigrations();

        var slugs = new List<string>();
        using (var conn = OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Slug FROM Feeds ORDER BY SortOrder";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                slugs.Add(reader.GetString(0));
            }
        }

        Assert.Equal(new[] { "default", "apple", "zebra" }, slugs);
    }

    /// <summary>
    /// Inserts a Feed row with a fresh Guid and only the required non-null columns, deliberately
    /// omitting SortOrder (which does not exist yet at this migration point).
    /// </summary>
    private void SeedFeed(string slug, string name)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Feeds (Id, Slug, Name, MirrorEnabled, MirrorLegacy, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $slug, $name, 0, 0, '2020-01-01', '2020-01-01')";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$slug", slug);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    private async Task ApplyMigrationsUpto(string targetMigration)
    {
        using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SqliteContext>();
        await ctx.Database.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private async Task ApplyAllMigrations()
    {
        using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SqliteContext>();
        await ctx.Database.MigrateAsync();
    }

    private ServiceProvider BuildServiceProvider()
    {
        // Provide a stub IOptionsSnapshot<BaGetterOptions> so SqliteContext can be resolved.
        // OnConfiguring checks !optionsBuilder.IsConfigured, which is false when we supply
        // options via AddDbContext — so the connection string from AddDbContext wins.
        var bagetterOptions = new BaGetterOptions
        {
            Database = new DatabaseOptions { ConnectionString = _connectionString }
        };

        var snapshot = new Mock<IOptionsSnapshot<BaGetterOptions>>();
        snapshot.Setup(s => s.Value).Returns(bagetterOptions);

        var services = new ServiceCollection();
        services.AddSingleton(snapshot.Object);
        services.AddDbContext<SqliteContext>(opts => opts.UseSqlite(_connectionString));

        return services.BuildServiceProvider();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
