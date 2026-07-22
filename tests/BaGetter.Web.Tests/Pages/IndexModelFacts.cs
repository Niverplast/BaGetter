using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Authentication;
using BaGetter.Core.Configuration;
using BaGetter.Core.Entities;
using BaGetter.Core.Feeds;
using BaGetter.Core.Search;
using BaGetter.Protocol.Models;
using BaGetter.Web.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BaGetter.Web.Tests.Pages;

public class IndexModelFacts
{
    public class OnGetAsync : FactsBase
    {
        [Fact]
        public async Task DefaultSearch()
        {
            var target = Build();

            await target.OnGetAsync(Cancellation);

            Assert.Equal(0, CapturedRequest.Skip);
            Assert.Equal(20, CapturedRequest.Take);
            Assert.True(CapturedRequest.IncludePrerelease);
            Assert.True(CapturedRequest.IncludeSemVer2);
            Assert.Null(CapturedRequest.PackageType);
            Assert.Null(CapturedRequest.Framework);
            Assert.Null(CapturedRequest.Tag);
            Assert.True(CapturedRequest.IncludeFacets);
            Assert.False(CapturedRequest.IncludeUnlisted);
            Assert.Null(CapturedRequest.Query);
        }

        [Fact]
        public async Task AcceptsParameters()
        {
            var target = Build();
            target.Prerelease = false;
            target.PageIndex = 5;
            target.PackageType = "foo";
            target.Framework = "bar";
            target.Tag = "baz";
            target.Query = "Hello world";

            await target.OnGetAsync(Cancellation);

            Assert.Equal(80, CapturedRequest.Skip);
            Assert.Equal(20, CapturedRequest.Take);
            Assert.False(CapturedRequest.IncludePrerelease);
            Assert.True(CapturedRequest.IncludeSemVer2);
            Assert.Equal("foo", CapturedRequest.PackageType);
            Assert.Equal("bar", CapturedRequest.Framework);
            Assert.Equal("baz", CapturedRequest.Tag);
            Assert.Equal("Hello world", CapturedRequest.Query);
        }

        [Fact]
        public async Task RootRouteRedirectsToFirstAccessibleFeedByOrder()
        {
            // The user can pull the default-slug feed, but a higher-ordered feed comes first.
            // Ordering wins: they should land on the first accessible feed, not the default.
            AuthMode = AuthenticationMode.Entra;
            IsDefaultRoute = true;
            CurrentFeed = DefaultFeed;

            var userId = Guid.NewGuid();
            var first = new Feed { Id = Guid.NewGuid(), Slug = "team-a", Name = "Team A" };
            FeedService
                .Setup(f => f.GetAllFeedsAsync(Cancellation))
                .ReturnsAsync(new List<Feed> { first, DefaultFeed });
            // GetAllFeedsAsync is the ordering source of truth (SortOrder, Name); the user can pull both.
            Permissions.Setup(p => p.CanPullAsync(userId, first.Id, Cancellation)).ReturnsAsync(true);
            Permissions.Setup(p => p.CanPullAsync(userId, DefaultFeed.Id, Cancellation)).ReturnsAsync(true);

            var result = await Build(AuthenticatedUser(userId)).OnGetAsync(Cancellation);

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("/feeds/team-a/", redirect.Url);
        }

        [Fact]
        public async Task RootRouteRendersWhenDefaultFeedIsFirstAccessible()
        {
            // First accessible feed is the default-slug feed served at "/": render, don't loop-redirect.
            AuthMode = AuthenticationMode.Entra;
            IsDefaultRoute = true;
            CurrentFeed = DefaultFeed;

            var userId = Guid.NewGuid();
            var other = new Feed { Id = Guid.NewGuid(), Slug = "team-a", Name = "Team A" };
            FeedService
                .Setup(f => f.GetAllFeedsAsync(Cancellation))
                .ReturnsAsync(new List<Feed> { DefaultFeed, other });
            Permissions.Setup(p => p.CanPullAsync(userId, DefaultFeed.Id, Cancellation)).ReturnsAsync(true);
            Permissions.Setup(p => p.CanPullAsync(userId, other.Id, Cancellation)).ReturnsAsync(false);

            var result = await Build(AuthenticatedUser(userId)).OnGetAsync(Cancellation);

            Assert.IsType<PageResult>(result);
            Assert.Equal(DefaultFeed.Id, CapturedRequest.FeedId);
        }

        [Fact]
        public async Task FeedRouteRendersWhenUserCanPull()
        {
            // Explicit /feeds/{slug} navigation to a lower-ordered feed the user can pull is preserved.
            AuthMode = AuthenticationMode.Entra;
            IsDefaultRoute = false;
            CurrentFeed = new Feed { Id = Guid.NewGuid(), Slug = "team-b", Name = "Team B" };

            var userId = Guid.NewGuid();
            Permissions.Setup(p => p.CanPullAsync(userId, CurrentFeed.Id, Cancellation)).ReturnsAsync(true);

            var result = await Build(AuthenticatedUser(userId)).OnGetAsync(Cancellation);

            Assert.IsType<PageResult>(result);
            Assert.Equal(CurrentFeed.Id, CapturedRequest.FeedId);
            FeedService.Verify(f => f.GetAllFeedsAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task FeedRouteRedirectsWhenUserCannotPull()
        {
            // Explicit /feeds/{slug} the user can't pull falls back to the first accessible feed.
            AuthMode = AuthenticationMode.Entra;
            IsDefaultRoute = false;
            CurrentFeed = new Feed { Id = Guid.NewGuid(), Slug = "team-b", Name = "Team B" };

            var userId = Guid.NewGuid();
            var accessible = new Feed { Id = Guid.NewGuid(), Slug = "team-a", Name = "Team A" };
            Permissions.Setup(p => p.CanPullAsync(userId, CurrentFeed.Id, Cancellation)).ReturnsAsync(false);
            FeedService
                .Setup(f => f.GetAllFeedsAsync(Cancellation))
                .ReturnsAsync(new List<Feed> { accessible, CurrentFeed });
            Permissions.Setup(p => p.CanPullAsync(userId, accessible.Id, Cancellation)).ReturnsAsync(true);

            var result = await Build(AuthenticatedUser(userId)).OnGetAsync(Cancellation);

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("/feeds/team-a/", redirect.Url);
        }

        [Fact]
        public async Task NoAccessibleFeedsSetsFlag()
        {
            AuthMode = AuthenticationMode.Entra;
            IsDefaultRoute = true;
            CurrentFeed = DefaultFeed;

            var userId = Guid.NewGuid();
            FeedService
                .Setup(f => f.GetAllFeedsAsync(Cancellation))
                .ReturnsAsync(new List<Feed> { DefaultFeed });
            Permissions.Setup(p => p.CanPullAsync(userId, DefaultFeed.Id, Cancellation)).ReturnsAsync(false);

            var target = Build(AuthenticatedUser(userId));
            var result = await target.OnGetAsync(Cancellation);

            Assert.IsType<PageResult>(result);
            Assert.True(target.HasNoAccessibleFeeds);
        }
    }

    public abstract class FactsBase
    {
        protected readonly Mock<ISearchService> Search = new();
        protected readonly Mock<IFeedContext> FeedContext = new();
        protected readonly Mock<IFeedService> FeedService = new();
        protected readonly Mock<IPermissionService> Permissions = new();
        protected readonly CancellationToken Cancellation = CancellationToken.None;

        protected SearchRequest CapturedRequest;
        protected AuthenticationMode AuthMode = AuthenticationMode.Config;
        protected bool IsDefaultRoute;
        protected Feed CurrentFeed;

        protected readonly Feed DefaultFeed = new()
        {
            Id = Guid.NewGuid(),
            Slug = Feed.DefaultSlug,
            Name = "Default",
        };

        protected FactsBase()
        {
            CurrentFeed = DefaultFeed;

            Search
                .Setup(s => s.SearchAsync(It.IsAny<SearchRequest>(), Cancellation))
                .Callback((SearchRequest r, CancellationToken _) => CapturedRequest = r)
                .ReturnsAsync(new SearchResponse());

            FeedContext.Setup(f => f.CurrentFeed).Returns(() => CurrentFeed);
            FeedContext.Setup(f => f.IsDefaultRoute).Returns(() => IsDefaultRoute);
        }

        protected IndexModel Build(ClaimsPrincipal user = null)
        {
            var authOptions = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
            authOptions.Setup(o => o.Value).Returns(new NugetAuthenticationOptions { Mode = AuthMode });

            var model = new IndexModel(
                Search.Object,
                FeedContext.Object,
                FeedService.Object,
                Permissions.Object,
                authOptions.Object);

            var httpContext = new DefaultHttpContext
            {
                User = user ?? new ClaimsPrincipal(new ClaimsIdentity()),
            };
            model.PageContext = new PageContext { HttpContext = httpContext };
            return model;
        }

        protected static ClaimsPrincipal AuthenticatedUser(Guid userId)
            => new(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
                "TestAuth"));
    }
}
