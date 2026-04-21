using System;
using System.Threading.Tasks;
using BaGetter.Core.Configuration;
using BaGetter.Core.Feeds;
using BaGetter.Core.Statistics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BaGetter.Web.Pages;

public class StatisticsModel : PageModel
{
    private readonly IOptionsSnapshot<StatisticsOptions> _options;
    private readonly IFeedContext _feedContext;
    private readonly IStatisticsService _statisticsService;

    public StatisticsModel(
        IOptionsSnapshot<StatisticsOptions> options,
        IFeedContext feedContext,
        IStatisticsService statisticsService)
    {
        _options = options;
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _statisticsService = statisticsService ?? throw new ArgumentNullException(nameof(statisticsService));
    }

    public string FeedName { get; private set; }
    public int PackagesTotal { get; private set; }
    public int VersionsTotal { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!_options.Value.EnableStatisticsPage) return NotFound();

        var feed = _feedContext.CurrentFeed;
        FeedName = feed.Name;
        PackagesTotal = await _statisticsService.GetPackagesTotalAmount(feed.Id);
        VersionsTotal = await _statisticsService.GetVersionsTotalAmount(feed.Id);
        return Page();
    }
}
