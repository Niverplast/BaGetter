using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BaGetter.Core.Feeds;

public class FeedService : IFeedService
{
    private static readonly Regex SlugRegex = new(
        @"^[a-z0-9](?:[a-z0-9-]{0,126}[a-z0-9])?$",
        RegexOptions.Compiled);

    private readonly IContext _context;

    public FeedService(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Feed> GetDefaultFeedAsync(CancellationToken cancellationToken)
    {
        return await _context.Feeds
            .FirstOrDefaultAsync(f => f.Slug == Feed.DefaultSlug, cancellationToken);
    }

    public async Task<Feed> GetFeedByIdAsync(Guid feedId, CancellationToken cancellationToken)
    {
        return await _context.Feeds
            .FirstOrDefaultAsync(f => f.Id == feedId, cancellationToken);
    }

    public async Task<Feed> GetFeedBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        return await _context.Feeds
            .FirstOrDefaultAsync(f => f.Slug == slug, cancellationToken);
    }

    public async Task<List<Feed>> GetAllFeedsAsync(CancellationToken cancellationToken)
    {
        return await _context.Feeds.ToListAsync(cancellationToken);
    }

    public async Task<Feed> CreateFeedAsync(Feed feed, CancellationToken cancellationToken)
    {
        if (!SlugRegex.IsMatch(feed.Slug))
        {
            throw new ArgumentException(
                $"Feed slug '{feed.Slug}' is invalid. Must match ^[a-z0-9](?:[a-z0-9-]{{0,126}}[a-z0-9])?$",
                nameof(feed));
        }

        feed.Id = Guid.NewGuid();
        feed.CreatedAtUtc = DateTime.UtcNow;
        feed.UpdatedAtUtc = DateTime.UtcNow;

        _context.Feeds.Add(feed);
        await _context.SaveChangesAsync(cancellationToken);
        return feed;
    }

    public async Task<Feed> UpdateFeedAsync(Feed feed, CancellationToken cancellationToken)
    {
        feed.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return feed;
    }

    public async Task<bool> DeleteFeedAsync(Guid feedId, CancellationToken cancellationToken)
    {
        var feed = await _context.Feeds
            .FirstOrDefaultAsync(f => f.Id == feedId, cancellationToken);

        if (feed == null)
            return false;

        if (feed.Slug == Feed.DefaultSlug)
            throw new InvalidOperationException("The default feed cannot be deleted.");

        _context.Feeds.Remove(feed);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task EnsureDefaultFeedExistsAsync(CancellationToken cancellationToken)
    {
        var exists = await _context.Feeds
            .AnyAsync(f => f.Slug == Feed.DefaultSlug, cancellationToken);

        if (exists)
            return;

        _context.Feeds.Add(new Feed
        {
            Id = Guid.NewGuid(),
            Slug = Feed.DefaultSlug,
            Name = "Default",
            MirrorEnabled = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}
