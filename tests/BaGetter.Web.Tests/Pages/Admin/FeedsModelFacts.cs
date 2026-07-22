using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Authentication;
using BaGetter.Core.Feeds;
using BaGetter.Web.Pages.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BaGetter.Web.Tests.Pages.Admin;

public class FeedsModelFacts
{
    public class OnPostReorderAsync : FactsBase
    {
        [Fact]
        public async Task NonAdminUserIsForbidden()
        {
            _users.Setup(u => u.IsAdminAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await _target.OnPostReorderAsync(new List<Guid> { Guid.NewGuid() }, CancellationToken.None);

            Assert.IsType<ForbidResult>(result);
            _feeds.Verify(f => f.ReorderFeedsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task NullListReturnsBadRequest()
        {
            var result = await _target.OnPostReorderAsync(null, CancellationToken.None);

            Assert.IsType<BadRequestResult>(result);
            _feeds.Verify(f => f.ReorderFeedsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EmptyListReturnsBadRequest()
        {
            var result = await _target.OnPostReorderAsync(new List<Guid>(), CancellationToken.None);

            Assert.IsType<BadRequestResult>(result);
            _feeds.Verify(f => f.ReorderFeedsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AdminWithValidIdsReordersAndReturnsOk()
        {
            var orderedFeedIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

            var result = await _target.OnPostReorderAsync(orderedFeedIds, CancellationToken.None);

            _feeds.Verify(f => f.ReorderFeedsAsync(orderedFeedIds, It.IsAny<CancellationToken>()), Times.Once);
            Assert.IsType<OkResult>(result);
        }
    }

    public class FactsBase
    {
        protected readonly Mock<IFeedService> _feeds = new();
        protected readonly Mock<IUserService> _users = new();
        protected readonly FeedsModel _target;

        protected FactsBase()
        {
            var adminId = Guid.NewGuid();
            _users.Setup(u => u.IsAdminAsync(adminId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, adminId.ToString()) }, "TestAuth"));

            _target = new FeedsModel(_feeds.Object, _users.Object, NullLogger<FeedsModel>.Instance)
            {
                PageContext = new PageContext(new ActionContext(
                    new DefaultHttpContext { User = principal },
                    new RouteData(),
                    new PageActionDescriptor())),
            };
        }
    }
}
