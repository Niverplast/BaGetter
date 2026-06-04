using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Entities;
using BaGetter.Core.Feeds;
using BaGetter.Core.Tests.Support;
using Xunit;

namespace BaGetter.Core.Tests.Feeds;

public class FeedServiceTests
{
    public class GetAllFeedsAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsDefaultFeedFirstThenAlphabeticalByName()
        {
            // Seeded out of order; "Default" would not sort first alphabetically (D > A),
            // so a correct result proves both the default-on-top and the alphabetical rules.
            AddFeed("zebra", "Zebra");
            AddFeed(Feed.DefaultSlug, "Default");
            AddFeed("apple", "Apple");
            await Context.SaveChangesAsync(Ct);

            var feeds = await Target.GetAllFeedsAsync(Ct);

            Assert.Equal(new[] { "Default", "Apple", "Zebra" }, feeds.Select(f => f.Name).ToArray());
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly TestDbContext Context;
        protected readonly FeedService Target;
        protected readonly CancellationToken Ct = CancellationToken.None;

        protected FactsBase()
        {
            Context = TestDbContext.Create();
            Target = new FeedService(Context);
        }

        protected void AddFeed(string slug, string name)
        {
            Context.Feeds.Add(new Feed
            {
                Id = Guid.NewGuid(),
                Slug = slug,
                Name = name,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }

        public void Dispose()
        {
            Context?.Dispose();
        }
    }
}
