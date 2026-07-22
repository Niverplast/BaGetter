using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Entities;
using BaGetter.Core.Feeds;
using BaGetter.Core.Storage;
using BaGetter.Core.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGetter.Core.Tests.Feeds;

public class FeedServiceTests
{
    public class GetAllFeedsAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFeedsBySortOrder()
        {
            // SortOrder is reverse-alphabetical here, so ordering by SortOrder (not Name) is what a pass proves.
            AddFeed("apple", "Apple", sortOrder: 2);
            AddFeed("mango", "Mango", sortOrder: 1);
            AddFeed("zebra", "Zebra", sortOrder: 0);
            await Context.SaveChangesAsync(Ct);

            var feeds = await Target.GetAllFeedsAsync(Ct);

            Assert.Equal(new[] { "Zebra", "Mango", "Apple" }, feeds.Select(f => f.Name).ToArray());
        }

        [Fact]
        public async Task TiesBrokenByName()
        {
            // Same SortOrder for all three, so Name is the only remaining tiebreak.
            AddFeed("zebra", "Zebra", sortOrder: 0);
            AddFeed("apple", "Apple", sortOrder: 0);
            AddFeed("mango", "Mango", sortOrder: 0);
            await Context.SaveChangesAsync(Ct);

            var feeds = await Target.GetAllFeedsAsync(Ct);

            Assert.Equal(new[] { "Apple", "Mango", "Zebra" }, feeds.Select(f => f.Name).ToArray());
        }
    }

    public class CreateFeedAsync : FactsBase
    {
        [Fact]
        public async Task AppendsNewFeedAtEndOfSortOrder()
        {
            // Highest existing SortOrder is 5, so the new feed must take 6 (max + 1, not count).
            AddFeed("apple", "Apple", sortOrder: 0);
            AddFeed("mango", "Mango", sortOrder: 5);
            await Context.SaveChangesAsync(Ct);

            var feed = await Target.CreateFeedAsync(new Feed { Slug = "zebra", Name = "Zebra" }, Ct);

            Assert.Equal(6, feed.SortOrder);
        }

        [Fact]
        public async Task FirstFeedGetsSortOrderZero()
        {
            var feed = await Target.CreateFeedAsync(new Feed { Slug = "apple", Name = "Apple" }, Ct);

            Assert.Equal(0, feed.SortOrder);
        }
    }

    public class ReorderFeedsAsync : FactsBase
    {
        [Fact]
        public async Task AssignsSortOrderByRequestedOrder()
        {
            // The default feed starts on top; the request moves it to the bottom.
            var def = AddFeed(Feed.DefaultSlug, "Default", sortOrder: 0);
            var apple = AddFeed("apple", "Apple", sortOrder: 1);
            var zebra = AddFeed("zebra", "Zebra", sortOrder: 2);
            await Context.SaveChangesAsync(Ct);

            await Target.ReorderFeedsAsync(new[] { apple.Id, zebra.Id, def.Id }, Ct);

            var feeds = await Target.GetAllFeedsAsync(Ct);
            Assert.Equal(new[] { "Apple", "Zebra", "Default" }, feeds.Select(f => f.Name).ToArray());
        }

        [Fact]
        public async Task IgnoresUnknownFeedIds()
        {
            // An unknown id in the request must be skipped without consuming a SortOrder slot.
            var apple = AddFeed("apple", "Apple", sortOrder: 0);
            var zebra = AddFeed("zebra", "Zebra", sortOrder: 1);
            await Context.SaveChangesAsync(Ct);

            await Target.ReorderFeedsAsync(new[] { zebra.Id, Guid.NewGuid(), apple.Id }, Ct);

            Assert.Equal(0, zebra.SortOrder);
            Assert.Equal(1, apple.SortOrder);
        }

        [Fact]
        public async Task AppendsFeedsMissingFromRequest()
        {
            // Only zebra is in the request; apple and mango are appended in (SortOrder, Name) order.
            var apple = AddFeed("apple", "Apple", sortOrder: 0);
            var mango = AddFeed("mango", "Mango", sortOrder: 1);
            var zebra = AddFeed("zebra", "Zebra", sortOrder: 2);
            await Context.SaveChangesAsync(Ct);

            await Target.ReorderFeedsAsync(new[] { zebra.Id }, Ct);

            Assert.Equal(0, zebra.SortOrder);
            Assert.Equal(1, apple.SortOrder);
            Assert.Equal(2, mango.SortOrder);
        }

        [Fact]
        public async Task IgnoresDuplicateIds()
        {
            // The duplicate apple id must be counted once, so zebra still lands at SortOrder 1.
            var apple = AddFeed("apple", "Apple", sortOrder: 0);
            var zebra = AddFeed("zebra", "Zebra", sortOrder: 1);
            await Context.SaveChangesAsync(Ct);

            await Target.ReorderFeedsAsync(new[] { apple.Id, apple.Id, zebra.Id }, Ct);

            Assert.Equal(0, apple.SortOrder);
            Assert.Equal(1, zebra.SortOrder);
        }
    }

    public class DeleteFeedAsync : FactsBase
    {
        [Fact]
        public async Task RemovesPackagesAndFeedAndCleansStorage()
        {
            var feed = AddFeed("toolfeed", "Tool Feed");
            AddPackage(feed.Id, "package.a", "1.0.0");
            AddPackage(feed.Id, "package.b", "2.1.0");
            await Context.SaveChangesAsync(Ct);

            var deleted = await Target.DeleteFeedAsync(feed.Id, Ct);

            Assert.True(deleted);
            Assert.False(await Context.Feeds.AnyAsync(f => f.Id == feed.Id, Ct));
            Assert.False(await Context.Packages.AnyAsync(p => p.FeedId == feed.Id, Ct));
            // Child rows must be gone too, or the package delete would hit a FK constraint.
            Assert.Empty(await Context.PackageDependencies.ToListAsync(Ct));
            Assert.Empty(await Context.TargetFrameworks.ToListAsync(Ct));

            PackageStorage.Verify(
                s => s.DeleteAsync("toolfeed", "package.a", It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
                Times.Once);
            PackageStorage.Verify(
                s => s.DeleteAsync("toolfeed", "package.b", It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task SucceedsForFeedWithoutPackages()
        {
            var feed = AddFeed("emptyfeed", "Empty Feed");
            await Context.SaveChangesAsync(Ct);

            var deleted = await Target.DeleteFeedAsync(feed.Id, Ct);

            Assert.True(deleted);
            Assert.False(await Context.Feeds.AnyAsync(f => f.Id == feed.Id, Ct));
            PackageStorage.Verify(
                s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ReturnsFalseWhenFeedNotFound()
        {
            Assert.False(await Target.DeleteFeedAsync(Guid.NewGuid(), Ct));
        }

        [Fact]
        public async Task ThrowsWhenDeletingDefaultFeed()
        {
            var feed = AddFeed(Feed.DefaultSlug, "Default");
            await Context.SaveChangesAsync(Ct);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Target.DeleteFeedAsync(feed.Id, Ct));
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly TestDbContext Context;
        protected readonly Mock<IPackageStorageService> PackageStorage;
        protected readonly FeedService Target;
        protected readonly CancellationToken Ct = CancellationToken.None;

        protected FactsBase()
        {
            Context = TestDbContext.Create();
            PackageStorage = new Mock<IPackageStorageService>();
            Target = new FeedService(Context, PackageStorage.Object, Mock.Of<ILogger<FeedService>>());
        }

        protected Feed AddFeed(string slug, string name, int sortOrder = 0)
        {
            var feed = new Feed
            {
                Id = Guid.NewGuid(),
                Slug = slug,
                Name = name,
                SortOrder = sortOrder,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            Context.Feeds.Add(feed);
            return feed;
        }

        protected void AddPackage(Guid feedId, string id, string version)
        {
            // Include a dependency and a target framework so deleting the package exercises the
            // child-row cascade (PackageDependencies' FK is NoAction, so it must be removed too).
            Context.Packages.Add(new Package
            {
                FeedId = feedId,
                Id = id,
                Version = new NuGetVersion(version),
                Dependencies = new List<PackageDependency>
                {
                    new() { Id = "Some.Dependency", VersionRange = "1.0.0", TargetFramework = "net8.0" },
                },
                TargetFrameworks = new List<TargetFramework>
                {
                    new() { Moniker = "net8.0" },
                },
            });
        }

        public void Dispose()
        {
            Context?.Dispose();
        }
    }
}
