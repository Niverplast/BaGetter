using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Authentication;
using BaGetter.Core.Configuration;
using BaGetter.Core.Entities;
using BaGetter.Core.Feeds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BaGetter.Web.Controllers;

[ApiController]
[Route("api/v1/feeds")]
[Authorize(AuthenticationSchemes = AuthenticationConstants.CookieScheme)]
public class FeedController : ControllerBase
{
    private readonly IFeedService _feedService;
    private readonly IUserService _userService;

    public FeedController(IFeedService feedService, IUserService userService)
    {
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
    }

    [HttpGet]
    public async Task<ActionResult<List<FeedDto>>> GetAll(CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
            return Forbid();

        var feeds = await _feedService.GetAllFeedsAsync(cancellationToken);
        return feeds.Select(ToDto).ToList();
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<FeedDto>> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
            return Forbid();

        var feed = await _feedService.GetFeedBySlugAsync(slug, cancellationToken);
        if (feed == null)
            return NotFound();

        return ToDto(feed);
    }

    [HttpPost]
    public async Task<ActionResult<FeedDto>> Create([FromBody] CreateFeedRequest request, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
            return Forbid();

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var feed = new Feed
        {
            Slug = request.Slug,
            Name = request.Name,
            Description = request.Description,
            MirrorEnabled = false,
            MirrorLegacy = false,
        };

        try
        {
            var created = await _feedService.CreateFeedAsync(feed, cancellationToken);
            return CreatedAtAction(nameof(GetBySlug), new { slug = created.Slug }, ToDto(created));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{slug}")]
    public async Task<ActionResult<FeedDto>> Update(string slug, [FromBody] UpdateFeedRequest request, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
            return Forbid();

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var feed = await _feedService.GetFeedBySlugAsync(slug, cancellationToken);
        if (feed == null)
            return NotFound();

        feed.Name = request.Name;
        feed.Description = request.Description;

        var updated = await _feedService.UpdateFeedAsync(feed, cancellationToken);
        return ToDto(updated);
    }

    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(cancellationToken))
            return Forbid();

        var feed = await _feedService.GetFeedBySlugAsync(slug, cancellationToken);
        if (feed == null)
            return NotFound();

        try
        {
            var deleted = await _feedService.DeleteFeedAsync(feed.Id, cancellationToken);
            if (!deleted)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    private async Task<bool> IsAdminAsync(CancellationToken cancellationToken)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !Guid.TryParse(claim.Value, out var userId))
            return false;

        return await _userService.IsAdminAsync(userId, cancellationToken);
    }

    private static FeedDto ToDto(Feed feed) => new FeedDto
    {
        Id = feed.Id,
        Slug = feed.Slug,
        Name = feed.Name,
        Description = feed.Description,
        AllowPackageOverwrites = feed.AllowPackageOverwrites,
        PackageDeletionBehavior = feed.PackageDeletionBehavior,
        IsReadOnlyMode = feed.IsReadOnlyMode,
        MaxPackageSizeGiB = feed.MaxPackageSizeGiB,
        RetentionMaxMajorVersions = feed.RetentionMaxMajorVersions,
        RetentionMaxMinorVersions = feed.RetentionMaxMinorVersions,
        RetentionMaxPatchVersions = feed.RetentionMaxPatchVersions,
        RetentionMaxPrereleaseVersions = feed.RetentionMaxPrereleaseVersions,
        MirrorEnabled = feed.MirrorEnabled,
        MirrorPackageSource = feed.MirrorPackageSource,
        MirrorLegacy = feed.MirrorLegacy,
        MirrorDownloadTimeoutSeconds = feed.MirrorDownloadTimeoutSeconds,
        MirrorAuthType = feed.MirrorAuthType,
        MirrorAuthUsername = feed.MirrorAuthUsername,
        HasMirrorAuthPassword = !string.IsNullOrEmpty(feed.MirrorAuthPassword),
        HasMirrorAuthToken = !string.IsNullOrEmpty(feed.MirrorAuthToken),
        CreatedAtUtc = feed.CreatedAtUtc,
        UpdatedAtUtc = feed.UpdatedAtUtc,
    };
}

public class CreateFeedRequest
{
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired]
    public string Slug { get; set; }

    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired]
    public string Name { get; set; }

    public string Description { get; set; }
}

public class UpdateFeedRequest
{
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired]
    public string Name { get; set; }

    public string Description { get; set; }
}

/// <summary>
/// Safe projection of a <see cref="Feed"/> for API responses.
/// Omits secret fields (password, token, custom headers) and replaces
/// them with boolean presence indicators.
/// </summary>
public class FeedDto
{
    public Guid Id { get; set; }
    public string Slug { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public PackageOverwriteAllowed? AllowPackageOverwrites { get; set; }
    public PackageDeletionBehavior? PackageDeletionBehavior { get; set; }
    public bool? IsReadOnlyMode { get; set; }
    public uint? MaxPackageSizeGiB { get; set; }
    public int? RetentionMaxMajorVersions { get; set; }
    public int? RetentionMaxMinorVersions { get; set; }
    public int? RetentionMaxPatchVersions { get; set; }
    public int? RetentionMaxPrereleaseVersions { get; set; }

    public bool MirrorEnabled { get; set; }
    public string MirrorPackageSource { get; set; }
    public bool MirrorLegacy { get; set; }
    public int? MirrorDownloadTimeoutSeconds { get; set; }
    public MirrorAuthenticationType? MirrorAuthType { get; set; }
    public string MirrorAuthUsername { get; set; }

    /// <summary>True if a mirror password is configured; the value is never returned.</summary>
    public bool HasMirrorAuthPassword { get; set; }

    /// <summary>True if a mirror bearer token is configured; the value is never returned.</summary>
    public bool HasMirrorAuthToken { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
