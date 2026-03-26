using System;
using System.Collections.Generic;
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

public class GroupServiceTests
{
    public class FindByIdAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullWhenNotFound()
        {
            var result = await _target.FindByIdAsync(Guid.NewGuid(), _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsGroupById()
        {
            var group = await _target.CreateGroupAsync("TestGroup", null, "desc", _ct);

            var result = await _target.FindByIdAsync(group.Id, _ct);

            Assert.NotNull(result);
            Assert.Equal("TestGroup", result.Name);
        }
    }

    public class FindByNameAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullWhenNotFound()
        {
            var result = await _target.FindByNameAsync("nonexistent", _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsGroupByName()
        {
            await _target.CreateGroupAsync("MyGroup", null, "desc", _ct);

            var result = await _target.FindByNameAsync("MyGroup", _ct);

            Assert.NotNull(result);
            Assert.Equal("MyGroup", result.Name);
        }
    }

    public class FindByEntraGroupIdAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullWhenNotFound()
        {
            var result = await _target.FindByEntraGroupIdAsync("no-such-id", _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsGroupByEntraGroupId()
        {
            await _target.CreateGroupAsync("EntraGroup", "entra-gid-1", "desc", _ct);

            var result = await _target.FindByEntraGroupIdAsync("entra-gid-1", _ct);

            Assert.NotNull(result);
            Assert.Equal("EntraGroup", result.Name);
        }
    }

    public class CreateGroupAsync : FactsBase
    {
        [Fact]
        public async Task CreatesGroupWithCorrectProperties()
        {
            var result = await _target.CreateGroupAsync("NewGroup", "entra-id-1", "A description", _ct);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("NewGroup", result.Name);
            Assert.Equal("entra-id-1", result.EntraGroupId);
            Assert.Equal("A description", result.Description);
            Assert.True(result.CreatedAtUtc <= DateTime.UtcNow);
        }
    }

    public class GetAllGroupsAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEmptyWhenNoGroups()
        {
            var result = await _target.GetAllGroupsAsync(_ct);

            Assert.Empty(result);
        }

        [Fact]
        public async Task ReturnsAllGroups()
        {
            await _target.CreateGroupAsync("Group1", null, null, _ct);
            await _target.CreateGroupAsync("Group2", null, null, _ct);

            var result = await _target.GetAllGroupsAsync(_ct);

            Assert.Equal(2, result.Count);
        }
    }

    public class AddUserToGroupAsync : FactsBase
    {
        [Fact]
        public async Task AddsUserToGroup()
        {
            var user = await CreateUser("member");
            var group = await _target.CreateGroupAsync("GroupA", null, null, _ct);

            await _target.AddUserToGroupAsync(user.Id, group.Id, _ct);

            var groups = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Single(groups);
            Assert.Equal("GroupA", groups[0].Name);
        }

        [Fact]
        public async Task DoesNotDuplicateMembership()
        {
            var user = await CreateUser("nodupe");
            var group = await _target.CreateGroupAsync("GroupB", null, null, _ct);

            await _target.AddUserToGroupAsync(user.Id, group.Id, _ct);
            await _target.AddUserToGroupAsync(user.Id, group.Id, _ct);

            var groups = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Single(groups);
        }
    }

    public class RemoveUserFromGroupAsync : FactsBase
    {
        [Fact]
        public async Task RemovesUserFromGroup()
        {
            var user = await CreateUser("removable");
            var group = await _target.CreateGroupAsync("GroupC", null, null, _ct);

            await _target.AddUserToGroupAsync(user.Id, group.Id, _ct);
            await _target.RemoveUserFromGroupAsync(user.Id, group.Id, _ct);

            var groups = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Empty(groups);
        }

        [Fact]
        public async Task DoesNothingWhenNotAMember()
        {
            var user = await CreateUser("notmember");
            var group = await _target.CreateGroupAsync("GroupD", null, null, _ct);

            await _target.RemoveUserFromGroupAsync(user.Id, group.Id, _ct);

            // No exception should be thrown
        }
    }

    public class GetUserGroupsAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEmptyWhenUserHasNoGroups()
        {
            var user = await CreateUser("lonely");

            var result = await _target.GetUserGroupsAsync(user.Id, _ct);

            Assert.Empty(result);
        }

        [Fact]
        public async Task ReturnsMultipleGroups()
        {
            var user = await CreateUser("multi");
            var group1 = await _target.CreateGroupAsync("G1", null, null, _ct);
            var group2 = await _target.CreateGroupAsync("G2", null, null, _ct);

            await _target.AddUserToGroupAsync(user.Id, group1.Id, _ct);
            await _target.AddUserToGroupAsync(user.Id, group2.Id, _ct);

            var result = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Equal(2, result.Count);
        }
    }

    public class SyncEntraGroupMembershipsAsync : FactsBase
    {
        [Fact]
        public async Task AddsNewGroupMemberships()
        {
            var user = await CreateUser("syncuser");
            var group = await _target.CreateGroupAsync("EntraSync", "entra-sync-1", null, _ct);

            await _target.SyncEntraGroupMembershipsAsync(
                user.Id, new List<string> { "entra-sync-1" }, _ct);

            var groups = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Single(groups);
            Assert.Equal("EntraSync", groups[0].Name);
        }

        [Fact]
        public async Task RemovesOldGroupMemberships()
        {
            var user = await CreateUser("syncremove");
            var group = await _target.CreateGroupAsync("OldGroup", "entra-old", null, _ct);

            await _target.AddUserToGroupAsync(user.Id, group.Id, _ct);

            // Sync with empty list removes the Entra group membership
            await _target.SyncEntraGroupMembershipsAsync(
                user.Id, new List<string>(), _ct);

            var groups = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Empty(groups);
        }

        [Fact]
        public async Task SkipsUnknownEntraGroupId()
        {
            var user = await CreateUser("skipunknown");

            await _target.SyncEntraGroupMembershipsAsync(
                user.Id, new List<string> { "unknown-entra-gid" }, _ct);

            var createdGroup = await _target.FindByEntraGroupIdAsync("unknown-entra-gid", _ct);
            Assert.Null(createdGroup);

            var groups = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Empty(groups);
        }
    }

    public class DeleteGroupAsync : FactsBase
    {
        [Fact]
        public async Task DoesNothingWhenGroupNotFound()
        {
            // Should complete without throwing
            await _target.DeleteGroupAsync(Guid.NewGuid(), _ct);
        }

        [Fact]
        public async Task RemovesGroupAndMemberships()
        {
            var user = await CreateUser("deletemember");
            var group = await _target.CreateGroupAsync("ToDelete", null, null, _ct);
            await _target.AddUserToGroupAsync(user.Id, group.Id, _ct);

            await _target.DeleteGroupAsync(group.Id, _ct);

            var found = await _target.FindByIdAsync(group.Id, _ct);
            Assert.Null(found);

            var groups = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Empty(groups);
        }

        [Fact]
        public async Task RemovesFeedPermissionsForGroup()
        {
            var group = await _target.CreateGroupAsync("PermGroup", null, null, _ct);

            _context.FeedPermissions.Add(new BaGetter.Core.Entities.FeedPermission
            {
                Id = Guid.NewGuid(),
                FeedId = "myfeed",
                PrincipalType = BaGetter.Core.Entities.PrincipalType.Group,
                PrincipalId = group.Id,
                CanPull = true
            });
            await _context.SaveChangesAsync(_ct);

            await _target.DeleteGroupAsync(group.Id, _ct);

            var permCount = await _context.FeedPermissions
                .CountAsync(fp => fp.PrincipalId == group.Id, _ct);
            Assert.Equal(0, permCount);
        }

        [Fact]
        public async Task DoesNotRemoveOtherGroupsOrPermissions()
        {
            var group1 = await _target.CreateGroupAsync("Keep", null, null, _ct);
            var group2 = await _target.CreateGroupAsync("Delete", null, null, _ct);

            _context.FeedPermissions.Add(new BaGetter.Core.Entities.FeedPermission
            {
                Id = Guid.NewGuid(),
                FeedId = "feed1",
                PrincipalType = BaGetter.Core.Entities.PrincipalType.Group,
                PrincipalId = group1.Id,
                CanPull = true
            });
            await _context.SaveChangesAsync(_ct);

            await _target.DeleteGroupAsync(group2.Id, _ct);

            var kept = await _target.FindByIdAsync(group1.Id, _ct);
            Assert.NotNull(kept);

            var permCount = await _context.FeedPermissions
                .CountAsync(fp => fp.PrincipalId == group1.Id, _ct);
            Assert.Equal(1, permCount);
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly TestDbContext _context;
        protected readonly GroupService _target;
        protected readonly CancellationToken _ct = CancellationToken.None;

        protected FactsBase()
        {
            _context = TestDbContext.Create();
            _target = new GroupService(
                _context,
                Mock.Of<ILogger<GroupService>>());
        }

        protected async Task<User> CreateUser(string username)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                DisplayName = $"{username} Display",
                Email = $"{username}@test.com",
                AuthProvider = AuthProvider.Entra,
                EntraObjectId = $"oid-{username}",
                IsEnabled = true,
                CanLoginToUI = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync(_ct);
            return user;
        }

        public void Dispose()
        {
            _context?.Dispose();
        }
    }
}
