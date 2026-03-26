using System;
using System.Threading;
using System.Threading.Tasks;

namespace BaGetter.Core.Authentication;

public interface IPermissionService
{
    Task<bool> CanPushAsync(Guid userId, string feedId, CancellationToken cancellationToken);
    Task<bool> CanPullAsync(Guid userId, string feedId, CancellationToken cancellationToken);
    Task GrantPermissionAsync(Guid principalId, Entities.PrincipalType principalType, string feedId, bool canPush, bool canPull, CancellationToken cancellationToken);
    Task RevokePermissionAsync(Guid permissionId, CancellationToken cancellationToken);
}
