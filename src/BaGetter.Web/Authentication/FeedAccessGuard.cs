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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BaGetter.Web.Authentication;

/// <summary>
/// Mirrors <see cref="FeedPermissionHandler"/> for Razor UI pages, which aren't routed
/// through the NuGet authorization policy.
/// </summary>
public static class FeedAccessGuard
{
    /// <summary>
    /// Returns null when the user may read the current feed, or a short-circuit
    /// <see cref="IActionResult"/> otherwise. Use for feed-specific pages (package
    /// details, statistics) where denying access as 404 is correct.
    /// </summary>
    public static async Task<IActionResult> CheckReadAccessAsync(
        HttpContext httpContext,
        IFeedContext feedContext,
        IPermissionService permissionService,
        AuthenticationMode authMode,
        CancellationToken cancellationToken)
    {
        // Config mode has no DB-backed users; UI access is gated by _Layout's auth check.
        if (authMode == AuthenticationMode.Config) return null;

        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            // Let the view render — _Layout shows a "Sign in required" prompt. Returning
            // a ChallengeResult here would trigger the NuGet Basic auth browser popup.
            return null;
        }

        // Static-auth anonymous fallback, kept consistent with FeedPermissionHandler.
        if (user.HasClaim(c => c.Type == ClaimTypes.Anonymous))
            return null;

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return new ForbidResult();

        if (feedContext.CurrentFeed == null)
            return new NotFoundResult();

        // 404 rather than 403 so we don't leak whether the feed exists.
        if (!await permissionService.CanPullAsync(userId, feedContext.CurrentFeed.Id, cancellationToken))
            return new NotFoundResult();

        return null;
    }

    /// <summary>
    /// Returns the subset of feeds the current user can pull from, preserving input order.
    /// In Config mode or for the anonymous fallback, returns every feed.
    /// Unauthenticated callers get an empty list.
    /// </summary>
    public static async Task<List<Feed>> FilterAccessibleFeedsAsync(
        HttpContext httpContext,
        IReadOnlyList<Feed> allFeeds,
        IPermissionService permissionService,
        AuthenticationMode authMode,
        CancellationToken cancellationToken)
    {
        if (allFeeds == null || allFeeds.Count == 0) return new List<Feed>();

        if (authMode == AuthenticationMode.Config) return allFeeds.ToList();

        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true) return new List<Feed>();

        if (user.HasClaim(c => c.Type == ClaimTypes.Anonymous)) return allFeeds.ToList();

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return new List<Feed>();

        var accessible = new List<Feed>(allFeeds.Count);
        foreach (var feed in allFeeds)
        {
            if (await permissionService.CanPullAsync(userId, feed.Id, cancellationToken))
            {
                accessible.Add(feed);
            }
        }
        return accessible;
    }
}
