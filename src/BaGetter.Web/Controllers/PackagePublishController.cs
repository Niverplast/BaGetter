using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core;
using BaGetter.Core.Authentication;
using BaGetter.Core.Configuration;
using BaGetter.Core.Indexing;
using BaGetter.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace BaGetter.Web.Controllers;

public class PackagePublishController : Controller
{
    private const string DefaultFeedId = "default";

    private readonly IAuthenticationService _authentication;
    private readonly IFeedAuthenticationService _feedAuthentication;
    private readonly IPermissionService _permissionService;
    private readonly IPackageIndexingService _indexer;
    private readonly IPackageDatabase _packages;
    private readonly IPackageDeletionService _deleteService;
    private readonly IOptionsSnapshot<BaGetterOptions> _options;
    private readonly ILogger<PackagePublishController> _logger;

    public PackagePublishController(
        IAuthenticationService authentication,
        IFeedAuthenticationService feedAuthentication,
        IPermissionService permissionService,
        IPackageIndexingService indexer,
        IPackageDatabase packages,
        IPackageDeletionService deletionService,
        IOptionsSnapshot<BaGetterOptions> options,
        ILogger<PackagePublishController> logger)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _feedAuthentication = feedAuthentication ?? throw new ArgumentNullException(nameof(feedAuthentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _deleteService = deletionService ?? throw new ArgumentNullException(nameof(deletionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
    public async Task Upload(CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode)
        {
            HttpContext.Response.StatusCode = 401;
            return;
        }

        if (!await AuthorizePushAsync(cancellationToken))
        {
            HttpContext.Response.StatusCode = 401;
            return;
        }

        try
        {
            using var uploadStream = await Request.GetUploadStreamOrNullAsync(cancellationToken);
            if (uploadStream == null)
            {
                HttpContext.Response.StatusCode = 400;
                return;
            }

            var result = await _indexer.IndexAsync(uploadStream, cancellationToken);

            switch (result)
            {
                case PackageIndexingResult.InvalidPackage:
                    HttpContext.Response.StatusCode = 400;
                    break;

                case PackageIndexingResult.PackageAlreadyExists:
                    HttpContext.Response.StatusCode = 409;
                    break;

                case PackageIndexingResult.Success:
                    HttpContext.Response.StatusCode = 201;
                    break;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during package upload");

            HttpContext.Response.StatusCode = 500;
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(string id, string version, CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode)
        {
            return Unauthorized();
        }

        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        if (!await AuthorizePushAsync(cancellationToken))
        {
            return Unauthorized();
        }

        if (await _deleteService.TryDeletePackageAsync(id, nugetVersion, cancellationToken))
        {
            return NoContent();
        }
        else
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<IActionResult> Relist(string id, string version, CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode)
        {
            return Unauthorized();
        }

        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        if (!await AuthorizePushAsync(cancellationToken))
        {
            return Unauthorized();
        }

        if (await _packages.RelistPackageAsync(id, nugetVersion, cancellationToken))
        {
            return Ok();
        }
        else
        {
            return NotFound();
        }
    }

    private async Task<bool> AuthorizePushAsync(CancellationToken cancellationToken)
    {
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.None;

        if (authMode == AuthenticationMode.None)
        {
            // Legacy mode: use config-based API key authentication
            return await _authentication.AuthenticateAsync(Request.GetApiKey(), cancellationToken);
        }

        // New mode: prefer X-NuGet-ApiKey (dotnet nuget push -k <token>), fall back to
        // the user identity already established by Basic auth middleware.
        var apiKey = Request.GetApiKey();
        if (!string.IsNullOrEmpty(apiKey))
        {
            var authResult = await _feedAuthentication.AuthenticateByTokenAsync(apiKey, cancellationToken);
            if (authResult.IsAuthenticated && authResult.UserId.HasValue)
                return await _permissionService.CanPushAsync(authResult.UserId.Value, DefaultFeedId, cancellationToken);
        }

        var userIdClaim = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var userId))
            return await _permissionService.CanPushAsync(userId, DefaultFeedId, cancellationToken);

        return false;
    }
}
