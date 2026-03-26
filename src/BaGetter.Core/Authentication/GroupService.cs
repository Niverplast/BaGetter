using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BaGetter.Core.Authentication;

public class GroupService : IGroupService
{
    private readonly IContext _context;
    private readonly ILogger<GroupService> _logger;

    public GroupService(IContext context, ILogger<GroupService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Group> FindByIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        return await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
    }

    public async Task<Group> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        return await _context.Groups.FirstOrDefaultAsync(g => g.Name == name, cancellationToken);
    }

    public async Task<Group> FindByEntraGroupIdAsync(string entraGroupId, CancellationToken cancellationToken)
    {
        return await _context.Groups.FirstOrDefaultAsync(
            g => g.EntraGroupId == entraGroupId, cancellationToken);
    }

    public async Task<Group> CreateGroupAsync(
        string name,
        string entraGroupId,
        string description,
        CancellationToken cancellationToken)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            EntraGroupId = entraGroupId,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Groups.Add(group);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created group {GroupName} with ID {GroupId}", name, group.Id);
        return group;
    }

    public async Task<List<Group>> GetAllGroupsAsync(CancellationToken cancellationToken)
    {
        return await _context.Groups.ToListAsync(cancellationToken);
    }

    public async Task<List<Group>> GetUserGroupsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.Group)
            .ToListAsync(cancellationToken);
    }

    public async Task AddUserToGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        var exists = await _context.UserGroups
            .AnyAsync(ug => ug.UserId == userId && ug.GroupId == groupId, cancellationToken);

        if (exists) return;

        _context.UserGroups.Add(new UserGroup
        {
            UserId = userId,
            GroupId = groupId
        });

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Added user {UserId} to group {GroupId}", userId, groupId);
    }

    public async Task RemoveUserFromGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        var membership = await _context.UserGroups
            .FirstOrDefaultAsync(ug => ug.UserId == userId && ug.GroupId == groupId, cancellationToken);

        if (membership == null) return;

        _context.UserGroups.Remove(membership);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Removed user {UserId} from group {GroupId}", userId, groupId);
    }

    public async Task SyncEntraGroupMembershipsAsync(
        Guid userId,
        IReadOnlyList<string> entraGroupIds,
        CancellationToken cancellationToken)
    {
        // Get all Entra-linked groups the user is currently a member of
        var currentMemberships = await _context.UserGroups
            .Where(ug => ug.UserId == userId && ug.Group.EntraGroupId != null)
            .Include(ug => ug.Group)
            .ToListAsync(cancellationToken);

        var currentEntraGroupIds = currentMemberships
            .Select(ug => ug.Group.EntraGroupId)
            .ToHashSet();

        // Add memberships for new Entra groups
        foreach (var entraGroupId in entraGroupIds)
        {
            if (currentEntraGroupIds.Contains(entraGroupId)) continue;

            var group = await FindByEntraGroupIdAsync(entraGroupId, cancellationToken);
            if (group == null)
            {
                // Auto-create group if it doesn't exist yet
                group = await CreateGroupAsync(
                    $"Entra-{entraGroupId}",
                    entraGroupId,
                    "Auto-created from Entra ID group sync",
                    cancellationToken);
            }

            _context.UserGroups.Add(new UserGroup
            {
                UserId = userId,
                GroupId = group.Id
            });
        }

        // Remove memberships for Entra groups the user is no longer in
        var entraGroupIdSet = entraGroupIds.ToHashSet();
        foreach (var membership in currentMemberships)
        {
            if (!entraGroupIdSet.Contains(membership.Group.EntraGroupId))
            {
                _context.UserGroups.Remove(membership);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Synced Entra group memberships for user {UserId}: {GroupCount} groups",
            userId, entraGroupIds.Count);
    }
}
