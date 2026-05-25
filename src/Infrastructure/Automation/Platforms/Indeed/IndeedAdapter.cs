using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Models;
using AutoApplicator.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Indeed;

public sealed class IndeedAdapter : IPlatformAdapter
{
    public PlatformType Platform => PlatformType.Indeed;
    public string BaseUrl => _baseUrl;

    private string _baseUrl = "https://www.indeed.com";
    private readonly IndeedExtractor _extractor;
    private readonly HumanBehavior _behavior;
    private readonly ILogger<IndeedAdapter> _logger;

    private static readonly string[] PaginationSelectors =
    [
        "a[data-testid=\"pagination-page-next\"]",
        ".np[aria-label=\"Next Page\"]",
        "nav[aria-label=\"pagination\"] a:last-child"
    ];

    private static readonly Dictionary<string, string> DatePostedMap = new()
    {
        ["Past 24 Hours"] = "1",
        ["Past Week"] = "7",
        ["Past Month"] = "30"
    };

    private static readonly Dictionary<string, string> JobTypeMap = new()
    {
        ["Full-time"] = "fulltime",
        ["Contract"] = "contract",
        ["Freelance"] = "temporary",
        ["Part-time"] = "parttime",
        ["Internship"] = "internship"
    };

    public IndeedAdapter(ILogger<IndeedAdapter> logger)
    {
        _extractor = new IndeedExtractor(logger);
        _behavior = new HumanBehavior();
        _logger = logger;
    }

    public async Task<AuthCheckResult> IsAuthenticatedAsync(IBrowserPage page)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        try
        {
            var modalLocator = innerPage
                .Locator("#privacy-gdpr, .fc-consent-root, [class*=\"cookie\"]")
                .First;
            await modalLocator.WaitForAsync(new() { Timeout = 2000 });
            var hasBlockingModal = await modalLocator.IsVisibleAsync();

            if (hasBlockingModal)
            {
                var dismissBtn = innerPage.Locator("button:has-text(\"Accept\"), button:has-text(\"Accept all\"), button:has-text(\"Permitir\")").First;
                await dismissBtn.WaitForAsync(new() { Timeout = 1000 });
                var dismissVisible = await dismissBtn.IsVisibleAsync();
                if (dismissVisible)
                {
                    await dismissBtn.ClickAsync();
                    await Task.Delay(1000);
                }
            }
        }
        catch { /* dismiss cookie modal, ignore error */ }

        // Indeed allows browsing jobs without authentication, but may redirect
        // to login page (secure.indeed.com/auth) when the apply flow requires it.
        var currentUrl = innerPage.Url;
        if (currentUrl.Contains("secure.indeed.com/auth") || currentUrl.Contains("/auth"))
        {
            return new AuthCheckResult
            {
                IsAuthenticated = false,
                LoginUrl = "https://secure.indeed.com/auth",
                Message = "Indeed: please log in first"
            };
        }

        return new AuthCheckResult { IsAuthenticated = true };
    }

    public string BuildSearchUrl(SearchProfile profile, int pageNum = 1)
    {
        var locationStr = string.Join(" ", profile.Location).ToLowerInvariant();
        if (locationStr.Contains("brasil") || locationStr.Contains("brazil"))
            _baseUrl = "https://br.indeed.com";
        else if (locationStr.Contains("argentina"))
            _baseUrl = "https://ar.indeed.com";
        else if (locationStr.Contains("mexico"))
            _baseUrl = "https://mx.indeed.com";
        else if (locationStr.Contains("uk") || locationStr.Contains("united kingdom") || locationStr.Contains("london"))
            _baseUrl = "https://uk.indeed.com";
        else if (locationStr.Contains("portugal") || locationStr.Contains("lisboa"))
            _baseUrl = "https://pt.indeed.com";
        else
            _baseUrl = "https://www.indeed.com";

        var queryParts = new List<string>();
        if (profile.Keywords.Count > 0 || profile.ExcludeTerms.Count > 0)
        {
            queryParts.AddRange(profile.Keywords);
            queryParts.AddRange(profile.ExcludeTerms.Select(t => $"-{t}"));
        }

        var parameters = new Dictionary<string, string>();

        if (queryParts.Count > 0)
            parameters["q"] = string.Join(" ", queryParts);

        if (profile.Location.Count > 0)
            parameters["l"] = profile.Location[0];

        if (!string.IsNullOrEmpty(profile.DatePosted) && DatePostedMap.TryGetValue(profile.DatePosted, out var fromage))
            parameters["fromage"] = fromage;

        if (profile.JobTypes.Count > 0)
        {
            var types = profile.JobTypes
                .Select(t => JobTypeMap.GetValueOrDefault(t))
                .Where(t => t is not null)
                .Cast<string>()
                .ToList();
            if (types.Count > 0)
                parameters["jt"] = types[0];
        }

        if (profile.RemoteOnly)
        {
            parameters["rbl"] = "-1";
            parameters["remotejob"] = "032b3046-06a3-4876-8dfd-474eb5e7ed11";
        }

        if (profile.EasyApplyOnly)
            parameters["sc"] = "0kf%3Aattr%28DSQF7%29%3B";

        if (pageNum > 1)
            parameters["start"] = ((pageNum - 1) * 10).ToString();

        var paramString = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return $"{_baseUrl}/jobs?{paramString}";
    }

    public async Task<List<ExtractedJob>> ExtractListingsAsync(IBrowserPage page)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        return await _extractor.ExtractJobCardsAsync(innerPage);
    }

    public async Task<bool> HasNextPageAsync(IBrowserPage page)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        foreach (var sel in PaginationSelectors)
        {
            try
            {
                var visible = await innerPage.Locator(sel).First.IsVisibleAsync();
                if (visible) return true;
            }
            catch { /* try next */ }
        }
        return false;
    }

    public async Task GoToNextPageAsync(IBrowserPage page)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        foreach (var sel in PaginationSelectors)
        {
            try
            {
                var visible = await innerPage.Locator(sel).First.IsVisibleAsync();
                if (visible)
                {
                    await _behavior.HumanClickAsync(innerPage, sel);
                    await innerPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                    await _behavior.DelayAsync(2000, 4000);
                    return;
                }
            }
            catch { /* try next */ }
        }
        _logger.LogWarning("Could not find next page button on Indeed");
    }

    public async Task NavigateToPageAsync(IBrowserPage page, SearchProfile profile, int pageNum)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        var url = BuildSearchUrl(profile, pageNum);
        await innerPage.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Task.Delay(2000);
    }

    public async Task<JobDetail> ExtractJobDetailsAsync(IBrowserPage page, string url)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        if (!innerPage.Url.Contains(url) && !string.IsNullOrEmpty(url))
        {
            await innerPage.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await _behavior.DelayAsync(1000, 2000);
        }

        return await _extractor.ExtractJobDetailsAsync(innerPage);
    }
}
