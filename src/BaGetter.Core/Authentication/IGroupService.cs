using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Entities;

namespace BaGetter.Core.Authentication;

public interface IGroupService
{
    Task<Group> FindByIdAsync(Guid groupId, CancellationToken cancellationToken);
    Task<Group> FindByNameAsync(string name, CancellationToken cancellationToken);
    Task<Group> FindByEntraGroupIdAsync(string entraGroupId, CancellationToken cancellationToken);
    Task<Group> CreateGroupAsync(string name, string entraGroupId, string description, CancellationToken cancellationToken);
    Task<List<Group>> GetAllGroupsAsync(CancellationToken cancellationToken);
    Task<List<Group>> GetUserGroupsAsync(Guid userId, CancellationToken cancellationToken);
    Task AddUserToGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken);
    Task RemoveUserFromGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken);
    Task SyncEntraGroupMembershipsAsync(Guid userId, IReadOnlyList<string> entraGroupIds, CancellationToken cancellationToken);
    Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken);
}
