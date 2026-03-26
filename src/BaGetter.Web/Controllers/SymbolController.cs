using System;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Authentication;
using BaGetter.Core.Configuration;
using BaGetter.Core.Indexing;
using BaGetter.Core.Storage;
using BaGetter.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGetter.Web.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class SymbolController : Controller
{
    private const string DefaultFeedId = "default";

    private readonly IAuthenticationService _authentication;
    private readonly IFeedAuthenticationService _feedAuthentication;
    private readonly IPermissionService _permissionService;
    private readonly ISymbolIndexingService _indexer;
    private readonly ISymbolStorageService _storage;
    private readonly IOptionsSnapshot<BaGetterOptions> _options;
    private readonly ILogger<SymbolController> _logger;

    public SymbolController(
        IAuthenticationService authentication,
        IFeedAuthenticationService feedAuthentication,
        IPermissionService permissionService,
        ISymbolIndexingService indexer,
        ISymbolStorageService storage,
        IOptionsSnapshot<BaGetterOptions> options,
        ILogger<SymbolController> logger)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _feedAuthentication = feedAuthentication ?? throw new ArgumentNullException(nameof(feedAuthentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
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
                case SymbolIndexingResult.InvalidSymbolPackage:
                    HttpContext.Response.StatusCode = 400;
                    break;

                case SymbolIndexingResult.PackageNotFound:
                    HttpContext.Response.StatusCode = 404;
                    break;

                case SymbolIndexingResult.Success:
                    HttpContext.Response.StatusCode = 201;
                    break;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during symbol upload");

            HttpContext.Response.StatusCode = 500;
        }
    }

    public async Task<IActionResult> Get(string file, string key)
    {
        var pdbStream = await _storage.GetPortablePdbContentStreamOrNullAsync(file, key);
        if (pdbStream == null)
        {
            return NotFound();
        }

        return File(pdbStream, "application/octet-stream");
    }

    private async Task<bool> AuthorizePushAsync(CancellationToken cancellationToken)
    {
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.None;

        if (authMode == AuthenticationMode.None)
        {
            return await _authentication.AuthenticateAsync(Request.GetApiKey(), cancellationToken);
        }

        var apiKey = Request.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
            return false;

        var authResult = await _feedAuthentication.AuthenticateByTokenAsync(apiKey, cancellationToken);
        if (!authResult.IsAuthenticated || !authResult.UserId.HasValue)
            return false;

        return await _permissionService.CanPushAsync(authResult.UserId.Value, DefaultFeedId, cancellationToken);
    }
}
