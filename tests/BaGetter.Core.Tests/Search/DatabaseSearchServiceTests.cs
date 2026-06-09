using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Entities;
using BaGetter.Core.Indexing;
using BaGetter.Core.Metadata;
using BaGetter.Core.Search;
using BaGetter.Core.Tests.Support;
using BaGetter.Protocol.Models;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGetter.Core.Tests.Search;

public class DatabaseSearchServiceTests
{
    [Fact]
    public void Ctor_ContextIsNull_ShouldThrow()
    {
        // Arrange
        var frameworkCompatibilityService = new Mock<IFrameworkCompatibilityService>();
        var searchResponseBuilder = new Mock<ISearchResponseBuilder>();

        // Act/Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new DatabaseSearchService(null, frameworkCompatibilityService.Object, searchResponseBuilder.Object));
    }

    [Fact]
    public void Ctor_FrameworkCompatibilityServiceIsNull_ShouldThrow()
    {
        // Arrange
        var context = new Mock<IContext>();
        var searchResponseBuilder = new Mock<ISearchResponseBuilder>();

        // Act/Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new DatabaseSearchService(context.Object, null, searchResponseBuilder.Object));
    }

    [Fact]
    public void Ctor_SearchResponseBuilderIsNull_ShouldThrow()
    {
        // Arrange
        var context = new Mock<IContext>();
        var frameworkCompatibilityService = new Mock<IFrameworkCompatibilityService>();

        // Act/Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new DatabaseSearchService(context.Object, frameworkCompatibilityService.Object, null));
    }

    public class SearchAsync : IDisposable
    {
        private static readonly Guid FeedId = Guid.NewGuid();
        private static readonly Guid OtherFeedId = Guid.NewGuid();

        private readonly TestDbContext _context;
        private readonly DatabaseSearchService _target;
        private IReadOnlyList<PackageRegistration> _capturedRegistrations;

        public SearchAsync()
        {
            _context = TestDbContext.Create();

            _context.Feeds.Add(new Feed { Id = FeedId, Slug = "feed", Name = "Feed" });
            _context.Feeds.Add(new Feed { Id = OtherFeedId, Slug = "other", Name = "Other" });

            // Feed under test.
            Seed("Alpha", "1.0.0", prerelease: false, types: ["Dependency"], frameworks: ["net8.0"], tags: ["json", "logging"]);
            Seed("Beta", "2.0.0", prerelease: false, types: ["DotnetTool"], frameworks: ["net9.0", "net8.0"], tags: ["logging"]);
            Seed("Gamma", "1.0.0-pre", prerelease: true, types: ["Dependency"], frameworks: ["net6.0"], tags: ["preview"]);
            // Empty types and the "any" sentinel (stored for framework-agnostic packages) must not become facets.
            Seed("Epsilon", "1.0.0", prerelease: false, types: [""], frameworks: ["any"], tags: ["any"]);
            // Different feed: must never leak into this feed's facets or tag filter.
            Seed("Delta", "1.0.0", prerelease: false, types: ["Other"], frameworks: ["net48"], tags: ["other"], feedId: OtherFeedId);
            _context.SaveChanges();

            var frameworks = new Mock<IFrameworkCompatibilityService>();
            var builder = new Mock<ISearchResponseBuilder>();
            builder
                .Setup(b => b.BuildSearch(It.IsAny<IReadOnlyList<PackageRegistration>>()))
                .Returns((IReadOnlyList<PackageRegistration> r) =>
                {
                    _capturedRegistrations = r;
                    return new SearchResponse
                    {
                        Data = r.Select(g => new SearchResult { PackageId = g.PackageId }).ToList(),
                    };
                });

            _target = new DatabaseSearchService(_context, frameworks.Object, builder.Object);
        }

        [Fact]
        public async Task ComputesFacets_FromOnlyTheRequestedFeed()
        {
            var response = await _target.SearchAsync(Request(includeFacets: true), CancellationToken.None);

            Assert.Equal(["Dependency", "DotnetTool"], response.Facets.PackageTypes);
            Assert.Equal(["net6.0", "net8.0", "net9.0"], response.Facets.Frameworks);
            Assert.Equal(["json", "logging", "preview"], response.Facets.Tags);
        }

        [Fact]
        public async Task ComputesFacets_ExcludesPrereleaseValues_WhenPrereleaseOff()
        {
            var response = await _target.SearchAsync(
                Request(includeFacets: true, includePrerelease: false),
                CancellationToken.None);

            // Gamma (the only prerelease) drops out, taking net6.0 and the "preview" tag with it.
            Assert.Equal(["Dependency", "DotnetTool"], response.Facets.PackageTypes);
            Assert.Equal(["net8.0", "net9.0"], response.Facets.Frameworks);
            Assert.Equal(["json", "logging"], response.Facets.Tags);
        }

        [Fact]
        public async Task DoesNotComputeFacets_WhenNotRequested()
        {
            var response = await _target.SearchAsync(Request(includeFacets: false), CancellationToken.None);

            Assert.Null(response.Facets);
        }

        [Fact]
        public async Task FiltersByTag()
        {
            await _target.SearchAsync(Request(tag: "logging"), CancellationToken.None);

            Assert.Equal(["Alpha", "Beta"], _capturedRegistrations.Select(r => r.PackageId).OrderBy(id => id).ToArray());
        }

        [Fact]
        public async Task FiltersByTag_IsCaseInsensitive()
        {
            await _target.SearchAsync(Request(tag: "LOGGING"), CancellationToken.None);

            Assert.Equal(["Alpha", "Beta"], _capturedRegistrations.Select(r => r.PackageId).OrderBy(id => id).ToArray());
        }

        private static SearchRequest Request(
            bool includeFacets = false,
            bool includePrerelease = true,
            string tag = null)
        {
            return new SearchRequest
            {
                FeedId = FeedId,
                Skip = 0,
                Take = 20,
                IncludePrerelease = includePrerelease,
                IncludeSemVer2 = true,
                IncludeFacets = includeFacets,
                Tag = tag,
            };
        }

        private void Seed(
            string id,
            string version,
            bool prerelease,
            string[] types,
            string[] frameworks,
            string[] tags,
            Guid? feedId = null)
        {
            _context.Packages.Add(new Package
            {
                Id = id,
                Version = NuGetVersion.Parse(version),
                FeedId = feedId ?? FeedId,
                Listed = true,
                IsPrerelease = prerelease,
                SemVerLevel = SemVerLevel.Unknown,
                Authors = [],
                PackageTypes = types.Select(t => new PackageType { Name = t }).ToList(),
                TargetFrameworks = frameworks.Select(f => new TargetFramework { Moniker = f }).ToList(),
                Tags = tags,
            });
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
