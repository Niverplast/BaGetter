using System;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Entities;

namespace BaGetter.Core.Authentication;

public interface IPermissionService
{
    Task<bool> CanPushAsync(Guid userId, string feedId, CancellationToken cancellationToken);
    Task<bool> CanPullAsync(Guid userId, string feedId, CancellationToken cancellationToken);
    Task<FeedPermission> GetPermissionAsync(Guid principalId, PrincipalType principalType, string feedId, CancellationToken cancellationToken);
    Task GrantPermissionAsync(Guid principalId, PrincipalType principalType, string feedId, bool canPush, bool canPull, CancellationToken cancellationToken, PermissionSource source = PermissionSource.Manual);
    Task RevokePermissionAsync(Guid permissionId, CancellationToken cancellationToken);
    Task RevokePermissionsBySourceAsync(Guid principalId, PrincipalType principalType, string feedId, PermissionSource source, CancellationToken cancellationToken);
}
