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

    public class FindByAppRoleValueAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullWhenNotFound()
        {
            var result = await _target.FindByAppRoleValueAsync("no-such-role", _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsGroupByAppRoleValue()
        {
            await _target.CreateGroupAsync("RoleGroup", "TeamFrontend", "desc", _ct);

            var result = await _target.FindByAppRoleValueAsync("TeamFrontend", _ct);

            Assert.NotNull(result);
            Assert.Equal("RoleGroup", result.Name);
        }
    }

    public class CreateGroupAsync : FactsBase
    {
        [Fact]
        public async Task CreatesGroupWithCorrectProperties()
        {
            var result = await _target.CreateGroupAsync("NewGroup", "TeamFrontend", "A description", _ct);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("NewGroup", result.Name);
            Assert.Equal("TeamFrontend", result.AppRoleValue);
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

        [Fact]
        public async Task ThrowsWhenAddingEntraUserToRoleLinkedGroup()
        {
            var user = await CreateUser("entrablock");
            var group = await _target.CreateGroupAsync("RoleGroup", "TeamFrontend", null, _ct);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _target.AddUserToGroupAsync(user.Id, group.Id, _ct));
        }

        [Fact]
        public async Task AllowsAddingLocalUserToRoleLinkedGroup()
        {
            var user = await CreateLocalUser("localok");
            var group = await _target.CreateGroupAsync("RoleGroup", "TeamBackend", null, _ct);

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
        public async Task ThrowsWhenRemovingEntraUserFromRoleLinkedGroup()
        {
            var user = await CreateUser("entraremoveblock");
            var group = await _target.CreateGroupAsync("RoleGroupR", "TeamFrontend", null, _ct);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _target.RemoveUserFromGroupAsync(user.Id, group.Id, _ct));
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

    public class SyncAppRoleMembershipsAsync : FactsBase
    {
        [Fact]
        public async Task AddsNewGroupMemberships()
        {
            var user = await CreateUser("syncuser");
            var group = await _target.CreateGroupAsync("RoleSync", "TeamFrontend", null, _ct);

            await _target.SyncAppRoleMembershipsAsync(
                user.Id, new List<string> { "TeamFrontend" }, _ct);

            var groups = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Single(groups);
            Assert.Equal("RoleSync", groups[0].Name);
        }

        [Fact]
        public async Task RemovesOldGroupMemberships()
        {
            var user = await CreateUser("syncremove");
            var group = await _target.CreateGroupAsync("OldGroup", "TeamBackend", null, _ct);

            // Add membership directly (bypassing service-layer guard, simulating prior sync)
            _context.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
            await _context.SaveChangesAsync(_ct);

            // Sync with empty list removes the role-linked group membership
            await _target.SyncAppRoleMembershipsAsync(
                user.Id, new List<string>(), _ct);

            var groups = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Empty(groups);
        }

        [Fact]
        public async Task SkipsUnknownAppRoleValue()
        {
            var user = await CreateUser("skipunknown");

            await _target.SyncAppRoleMembershipsAsync(
                user.Id, new List<string> { "UnknownRole" }, _ct);

            var createdGroup = await _target.FindByAppRoleValueAsync("UnknownRole", _ct);
            Assert.Null(createdGroup);

            var groups = await _target.GetUserGroupsAsync(user.Id, _ct);
            Assert.Empty(groups);
        }
    }

    public class IsRoleLinkedGroupAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsTrueForRoleLinkedGroup()
        {
            var group = await _target.CreateGroupAsync("RoleGroup", "TeamFrontend", null, _ct);

            var result = await _target.IsRoleLinkedGroupAsync(group.Id, _ct);

            Assert.True(result);
        }

        [Fact]
        public async Task ReturnsFalseForManualGroup()
        {
            var group = await _target.CreateGroupAsync("ManualGroup", null, null, _ct);

            var result = await _target.IsRoleLinkedGroupAsync(group.Id, _ct);

            Assert.False(result);
        }

        [Fact]
        public async Task ReturnsFalseForNonExistentGroup()
        {
            var result = await _target.IsRoleLinkedGroupAsync(Guid.NewGuid(), _ct);

            Assert.False(result);
        }
    }

    public class CanManuallyModifyMembershipAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseForEntraUserOnRoleLinkedGroup()
        {
            var user = await CreateUser("entrauser");
            var group = await _target.CreateGroupAsync("RoleGroup", "TeamFrontend", null, _ct);

            var result = await _target.CanManuallyModifyMembershipAsync(group.Id, user.Id, _ct);

            Assert.False(result);
        }

        [Fact]
        public async Task ReturnsTrueForLocalUserOnRoleLinkedGroup()
        {
            var user = await CreateLocalUser("localuser");
            var group = await _target.CreateGroupAsync("RoleGroup", "TeamBackend", null, _ct);

            var result = await _target.CanManuallyModifyMembershipAsync(group.Id, user.Id, _ct);

            Assert.True(result);
        }

        [Fact]
        public async Task ReturnsTrueForEntraUserOnManualGroup()
        {
            var user = await CreateUser("entramanual");
            var group = await _target.CreateGroupAsync("ManualGroup", null, null, _ct);

            var result = await _target.CanManuallyModifyMembershipAsync(group.Id, user.Id, _ct);

            Assert.True(result);
        }

        [Fact]
        public async Task ReturnsTrueForLocalUserOnManualGroup()
        {
            var user = await CreateLocalUser("localmanual");
            var group = await _target.CreateGroupAsync("ManualGroup", null, null, _ct);

            var result = await _target.CanManuallyModifyMembershipAsync(group.Id, user.Id, _ct);

            Assert.True(result);
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

        protected async Task<User> CreateLocalUser(string username)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                DisplayName = $"{username} Display",
                AuthProvider = AuthProvider.Local,
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
