using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Gupy;

public sealed class GupyAdapter : IPlatformAdapter
{
    public PlatformType Platform => PlatformType.Gupy;
    public string BaseUrl => "https://portal.gupy.io";

    private readonly GupyExtractor _extractor;
    private readonly HumanBehavior _behavior;
    private readonly ILogger<GupyAdapter> _logger;

    private static readonly string[] JobCardSelectors =
    [
        "[data-testid=\"job-card\"]",
        ".job-card",
        "a[class*=\"job\"]",
        "div[class*=\"job-card\"]"
    ];

    private static readonly string[] CardTitleSelectors =
    [
        "[data-testid=\"job-card-title\"]",
        "h2",
        "h3",
        "a[class*=\"job\"] h3",
        "a[class*=\"job\"] h2"
    ];

    private static readonly string[] CardCompanySelectors =
    [
        "[data-testid=\"job-card-company\"]",
        "[class*=\"company\"]",
        "span[class*=\"company\"]"
    ];

    private static readonly string[] CardLocationSelectors =
    [
        "[data-testid=\"job-card-location\"]",
        "[class*=\"location\"]",
        "[class*=\"place\"]"
    ];

    public GupyAdapter(ILogger<GupyAdapter> logger, GupyExtractor extractor)
    {
        _extractor = extractor;
        _behavior = new HumanBehavior();
        _logger = logger;
    }

    public Task<AuthCheckResult> IsAuthenticatedAsync(IPage page)
    {
        return Task.FromResult(new AuthCheckResult { IsAuthenticated = true });
    }

    public string BuildSearchUrl(SearchProfile profile, int pageNum = 1)
    {
        var keywords = string.Join(" ", profile.Keywords);
        var query = Uri.EscapeDataString(keywords);
        var page = pageNum > 1 ? $"&page={pageNum}" : string.Empty;
        return $"{BaseUrl}/job-search?term={query}{page}";
    }

    public async Task<List<ExtractedJob>> ExtractListingsAsync(IPage page)
    {
        return await _extractor.ExtractJobCardsAsync(page);
    }

    public Task<bool> HasNextPageAsync(IPage page)
    {
        return Task.FromResult(false);
    }

    public Task GoToNextPageAsync(IPage page)
    {
        return Task.CompletedTask;
    }

    public async Task NavigateToPageAsync(IPage page, SearchProfile profile, int pageNum)
    {
        var url = $"{BaseUrl}/job-search?term={Uri.EscapeDataString(string.Join(" ", profile.Keywords))}&page={pageNum}";
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Task.Delay(2000);
    }

    public async Task<JobDetail> ExtractJobDetailsAsync(IPage page, string url)
    {
        if (!page.Url.Contains(url) && !string.IsNullOrEmpty(url))
        {
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await _behavior.DelayAsync(1000, 2000);
        }

        return await _extractor.ExtractJobDetailsAsync(page);
    }

    private static async Task<string?> GetInnerTextAsync(IElementHandle parent, string[] selectors)
    {
        foreach (var sel in selectors)
        {
            try
            {
                var el = await parent.QuerySelectorAsync(sel);
                if (el is null) continue;
                var text = (await el.InnerTextAsync())?.Trim();
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch { /* try next */ }
        }
        return null;
    }
}
