using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BaGetter.Core.Authentication;
using BaGetter.Core.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BaGetter.Web.Authentication;

public class FeedPermissionHandler : AuthorizationHandler<FeedPermissionRequirement>
{
    private const string DefaultFeedId = "default";
    private readonly IPermissionService _permissionService;
    private readonly IUserService _userService;
    private readonly IOptions<BaGetterOptions> _options;

    public FeedPermissionHandler(
        IPermissionService permissionService,
        IUserService userService,
        IOptions<BaGetterOptions> options)
    {
        _permissionService = permissionService;
        _userService = userService;
        _options = options;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FeedPermissionRequirement requirement)
    {
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.None;

        // In legacy mode, the existing auth handler already did its job
        if (authMode == AuthenticationMode.None)
        {
            context.Succeed(requirement);
            return;
        }

        // Anonymous users get a pass if they have the Anonymous claim (legacy mode fallback)
        if (context.User.HasClaim(c => c.Type == ClaimTypes.Anonymous))
        {
            context.Succeed(requirement);
            return;
        }

        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            context.Fail();
            return;
        }

        var hasPermission = requirement.Permission switch
        {
            FeedPermissionRequirement.Pull => await _permissionService.CanPullAsync(userId, DefaultFeedId, default),
            FeedPermissionRequirement.Push => await _permissionService.CanPushAsync(userId, DefaultFeedId, default),
            FeedPermissionRequirement.Admin => await _userService.IsAdminAsync(userId, default),
            _ => false
        };

        if (hasPermission)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
