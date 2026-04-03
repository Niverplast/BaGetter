using System;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Authentication;
using BaGetter.Core.Entities;
using BaGetter.Core.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BaGetter.Core.Tests.Authentication;

public class PermissionServiceTests
{
    public class CanPushAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseWhenNoPermissions()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "noperm");

            var result = await _target.CanPushAsync(userId, "default", _ct);

            Assert.False(result);
        }

        [Fact]
        public async Task ReturnsTrueWhenUserHasDirectPushPermission()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "pushuser");

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: true, canPull: false, _ct);

            var result = await _target.CanPushAsync(userId, "default", _ct);

            Assert.True(result);
        }

        [Fact]
        public async Task ReturnsTrueWhenUserHasGroupPushPermission()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "grouppush");
            var groupId = await CreateGroupWithUser(userId, "PushGroup");

            await _target.GrantPermissionAsync(
                groupId, PrincipalType.Group, "default",
                canPush: true, canPull: false, _ct);

            var result = await _target.CanPushAsync(userId, "default", _ct);

            Assert.True(result);
        }

        [Fact]
        public async Task ReturnsFalseForDifferentFeed()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "feedscope");

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "feed-a",
                canPush: true, canPull: false, _ct);

            var result = await _target.CanPushAsync(userId, "feed-b", _ct);

            Assert.False(result);
        }
    }

    public class CanPullAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseWhenNoPermissions()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "nopull");

            var result = await _target.CanPullAsync(userId, "default", _ct);

            Assert.False(result);
        }

        [Fact]
        public async Task ReturnsTrueWhenUserHasPullPermission()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "pulluser");

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: false, canPull: true, _ct);

            var result = await _target.CanPullAsync(userId, "default", _ct);

            Assert.True(result);
        }
    }

    public class GrantPermissionAsync : FactsBase
    {
        [Fact]
        public async Task CreatesNewPermission()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "grantee");

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: true, canPull: true, _ct);

            Assert.True(await _target.CanPushAsync(userId, "default", _ct));
            Assert.True(await _target.CanPullAsync(userId, "default", _ct));
        }

        [Fact]
        public async Task UpdatesExistingPermission()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "updategrant");

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: true, canPull: false, _ct);

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: false, canPull: true, _ct);

            Assert.False(await _target.CanPushAsync(userId, "default", _ct));
            Assert.True(await _target.CanPullAsync(userId, "default", _ct));
        }
    }

    public class AdminBypass : FactsBase
    {
        [Fact]
        public async Task AdminCanPushWithoutExplicitPermission()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "adminuser");
            _userService.Setup(s => s.IsAdminAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var result = await _target.CanPushAsync(userId, "default", _ct);

            Assert.True(result);
        }

        [Fact]
        public async Task AdminCanPullWithoutExplicitPermission()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "adminpull");
            _userService.Setup(s => s.IsAdminAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var result = await _target.CanPullAsync(userId, "default", _ct);

            Assert.True(result);
        }
    }

    public class RevokePermissionAsync : FactsBase
    {
        [Fact]
        public async Task RevokesPermission()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "revokee");

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: true, canPull: true, _ct);

            var permission = await _context.FeedPermissions
                .FirstOrDefaultAsync(p => p.PrincipalId == userId, _ct);
            Assert.NotNull(permission);

            await _target.RevokePermissionAsync(permission.Id, _ct);

            Assert.False(await _target.CanPushAsync(userId, "default", _ct));
            Assert.False(await _target.CanPullAsync(userId, "default", _ct));
        }

        [Fact]
        public async Task DoesNothingWhenPermissionNotFound()
        {
            await _target.RevokePermissionAsync(Guid.NewGuid(), _ct);

            // No exception should be thrown
        }
    }

    public class GrantPermissionWithSourceAsync : FactsBase
    {
        [Fact]
        public async Task DefaultsToManualSource()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "defaultsource");

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: true, canPull: true, _ct);

            var perm = await _context.FeedPermissions
                .FirstOrDefaultAsync(p => p.PrincipalId == userId, _ct);
            Assert.NotNull(perm);
            Assert.Equal(PermissionSource.Manual, perm.Source);
        }

        [Fact]
        public async Task SetsEntraRoleSource()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "entrasource");

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: true, canPull: true, _ct,
                source: PermissionSource.EntraRole);

            var perm = await _context.FeedPermissions
                .FirstOrDefaultAsync(p => p.PrincipalId == userId, _ct);
            Assert.NotNull(perm);
            Assert.Equal(PermissionSource.EntraRole, perm.Source);
        }

        [Fact]
        public async Task UpdateExistingPermissionUpdatesSource()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "updatesource");

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: true, canPull: false, _ct);

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: true, canPull: true, _ct,
                source: PermissionSource.EntraRole);

            var perm = await _context.FeedPermissions
                .FirstOrDefaultAsync(p => p.PrincipalId == userId, _ct);
            Assert.Equal(PermissionSource.EntraRole, perm.Source);
        }
    }

    public class RevokePermissionsBySourceAsync : FactsBase
    {
        [Fact]
        public async Task RevokesOnlyMatchingSource()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "sourcerevoke");

            // Create manual permission
            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "feed-a",
                canPush: true, canPull: true, _ct,
                source: PermissionSource.Manual);

            // Create EntraRole permission
            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "feed-b",
                canPush: true, canPull: true, _ct,
                source: PermissionSource.EntraRole);

            // Revoke only EntraRole permissions on feed-b
            await _target.RevokePermissionsBySourceAsync(
                userId, PrincipalType.User, "feed-b", PermissionSource.EntraRole, _ct);

            // Manual permission on feed-a should still exist
            Assert.True(await _target.CanPushAsync(userId, "feed-a", _ct));
            // EntraRole permission on feed-b should be revoked
            Assert.False(await _target.CanPushAsync(userId, "feed-b", _ct));
        }

        [Fact]
        public async Task DoesNothingWhenNoMatchingPermissions()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "nomatch");

            await _target.RevokePermissionsBySourceAsync(
                userId, PrincipalType.User, "default", PermissionSource.EntraRole, _ct);

            // No exception
        }
    }

    public class HasPermissionWorksForBothSources : FactsBase
    {
        [Fact]
        public async Task EntraRolePermissionGrantsAccess()
        {
            var userId = Guid.NewGuid();
            await CreateUser(userId, "entraaccesstest");

            await _target.GrantPermissionAsync(
                userId, PrincipalType.User, "default",
                canPush: true, canPull: true, _ct,
                source: PermissionSource.EntraRole);

            Assert.True(await _target.CanPushAsync(userId, "default", _ct));
            Assert.True(await _target.CanPullAsync(userId, "default", _ct));
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly TestDbContext _context;
        protected readonly Mock<IUserService> _userService;
        protected readonly PermissionService _target;
        protected readonly CancellationToken _ct = CancellationToken.None;

        protected FactsBase()
        {
            _context = TestDbContext.Create();
            _userService = new Mock<IUserService>();
            _target = new PermissionService(
                _context,
                _userService.Object,
                Mock.Of<ILogger<PermissionService>>());
        }

        protected async Task CreateUser(Guid userId, string username)
        {
            _context.Users.Add(new User
            {
                Id = userId,
                Username = username,
                DisplayName = $"{username} Display",
                AuthProvider = AuthProvider.Local,
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(_ct);
        }

        protected async Task<Guid> CreateGroupWithUser(Guid userId, string groupName)
        {
            var group = new Group
            {
                Id = Guid.NewGuid(),
                Name = groupName,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.Groups.Add(group);

            _context.UserGroups.Add(new UserGroup
            {
                UserId = userId,
                GroupId = group.Id
            });

            await _context.SaveChangesAsync(_ct);
            return group.Id;
        }

        public void Dispose()
        {
            _context?.Dispose();
        }
    }
}
