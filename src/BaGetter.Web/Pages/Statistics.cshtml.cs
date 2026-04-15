using System;
using BaGetter.Core.Configuration;
using BaGetter.Core.Feeds;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BaGetter.Web.Pages;

public class StatisticsModel : PageModel
{
    private readonly IOptionsSnapshot<StatisticsOptions> _options;
    private readonly IFeedContext _feedContext;

    public StatisticsModel(IOptionsSnapshot<StatisticsOptions> options, IFeedContext feedContext)
    {
        _options = options;
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
    }

    public string FeedName { get; private set; }

    public IActionResult OnGet()
    {
        if (!_options.Value.EnableStatisticsPage) return NotFound();

        FeedName = _feedContext.CurrentFeed.Name;
        return Page();
    }
}
