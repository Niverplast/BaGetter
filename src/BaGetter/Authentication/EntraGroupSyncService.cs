using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Authentication;
using BaGetter.Core.Configuration;
using BaGetter.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGetter.Authentication;

/// <summary>
/// Handles user provisioning and Entra group synchronization on OIDC token validation.
/// </summary>
public class EntraGroupSyncService
{
    private readonly IUserService _userService;
    private readonly IGroupService _groupService;
    private readonly IPermissionService _permissionService;
    private readonly IOptions<BaGetterOptions> _options;
    private readonly ILogger<EntraGroupSyncService> _logger;

    public EntraGroupSyncService(
        IUserService userService,
        IGroupService groupService,
        IPermissionService permissionService,
        IOptions<BaGetterOptions> options,
        ILogger<EntraGroupSyncService> logger)
    {
        _userService = userService;
        _groupService = groupService;
        _permissionService = permissionService;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Provisions or updates a user from Entra ID claims and syncs group memberships.
    /// Called from the OnTokenValidated OIDC event.
    /// </summary>
    public async Task OnTokenValidatedAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var oid = principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                  ?? principal.FindFirstValue("oid");

        if (string.IsNullOrEmpty(oid))
        {
            _logger.LogWarning("Entra ID token is missing the object identifier claim");
            return;
        }

        var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("preferred_username");
        var displayName = principal.FindFirstValue(ClaimTypes.GivenName) is { } given
                          && principal.FindFirstValue(ClaimTypes.Surname) is { } surname
            ? $"{given} {surname}"
            : principal.FindFirstValue("name") ?? email ?? oid;
        var username = email ?? oid;

        // Upsert the user
        var user = await _userService.FindByEntraObjectIdAsync(oid, cancellationToken);
        if (user == null)
        {
            _logger.LogInformation("Provisioning new Entra user: {Username} (OID: {Oid})", username, oid);
            user = await _userService.CreateEntraUserAsync(oid, username, displayName, cancellationToken);
        }
        else
        {
            // Update display name if changed
            var changed = false;
            if (user.DisplayName != displayName) { user.DisplayName = displayName; changed = true; }
            if (user.Username != username) { user.Username = username; changed = true; }

            if (changed)
            {
                user.UpdatedAtUtc = DateTime.UtcNow;
                await _userService.UpdateUserAsync(user, cancellationToken);
            }
        }

        if (!user.IsEnabled)
        {
            _logger.LogWarning("Entra user {Username} is disabled, skipping group sync", user.Username);
            return;
        }

        // Sync group memberships from the 'groups' claim
        var groupClaims = principal.FindAll("groups").Select(c => c.Value).ToList();
        if (groupClaims.Count > 0)
        {
            await _groupService.SyncEntraGroupMembershipsAsync(user.Id, groupClaims, cancellationToken);
        }

        // Check if user is in the admin group and grant admin permissions
        await EnsureAdminGroupPermissionsAsync(user, groupClaims, cancellationToken);

        // Add BaGetter-specific claims to the principal
        var identity = principal.Identity as ClaimsIdentity;
        if (identity != null)
        {
            // Remove any existing NameIdentifier claims (e.g. the Entra 'sub' value)
            // so that FindFirst(NameIdentifier) always returns the BaGetter user ID.
            var existing = identity.FindAll(ClaimTypes.NameIdentifier).ToList();
            foreach (var c in existing)
                identity.RemoveClaim(c);

            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
            identity.AddClaim(new Claim("auth_provider", AuthProvider.Entra.ToString()));
        }
    }

    private async Task EnsureAdminGroupPermissionsAsync(
        User user,
        System.Collections.Generic.List<string> entraGroupIds,
        CancellationToken cancellationToken)
    {
        var adminGroupId = _options.Value.Authentication?.Entra?.AdminGroupId;
        if (string.IsNullOrEmpty(adminGroupId))
            return;

        // Check if the user's Entra group claims contain the configured admin group ID
        var isInAdminGroup = entraGroupIds.Contains(adminGroupId, StringComparer.OrdinalIgnoreCase);

        if (isInAdminGroup)
        {
            // Grant admin permission on the user if not already granted
            var isAdmin = await _userService.IsAdminAsync(user.Id, cancellationToken);
            if (!isAdmin)
            {
                _logger.LogInformation("Granting admin permissions to Entra user {Username} via group ID {AdminGroupId}",
                    user.Username, adminGroupId);
                await _userService.SetAdminAsync(user.Id, true, cancellationToken);

                // Also ensure pull/push permissions on the default feed
                await _permissionService.GrantPermissionAsync(
                    user.Id, PrincipalType.User, "default",
                    canPush: true, canPull: true,
                    cancellationToken);
            }
        }
    }
}
