using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Gupy;

public sealed class GupyAdapter : IPlatformAdapter
{
    public PlatformType Platform => PlatformType.Gupy;
    public string BaseUrl => "https://portal.gupy.io";

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

    public GupyAdapter(ILogger<GupyAdapter> logger)
    {
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
        var cards = new List<ExtractedJob>();

        try
        {
            await _behavior.DelayAsync(2000, 3000);

            var jobLinks = await page.QuerySelectorAllAsync("a[href*=\"/job/\"]");
            var processedUrls = new HashSet<string>();

            foreach (var link in jobLinks)
            {
                try
                {
                    var href = await link.GetAttributeAsync("href");
                    if (string.IsNullOrEmpty(href)) continue;

                    var url = href.StartsWith("http") ? href : $"{BaseUrl}{href}";
                    if (!processedUrls.Add(url)) continue;

                    var title = (await GetInnerTextAsync(link, CardTitleSelectors))
                                ?? link.InnerTextAsync().Result?.Trim()
                                ?? string.Empty;
                    if (string.IsNullOrEmpty(title)) continue;

                    var company = await GetInnerTextAsync(link, CardCompanySelectors) ?? string.Empty;
                    var location = await GetInnerTextAsync(link, CardLocationSelectors) ?? string.Empty;

                    var externalId = Guid.NewGuid().ToString("N")[..12];

                    cards.Add(new ExtractedJob
                    {
                        ExternalId = externalId,
                        Title = title,
                        Company = company,
                        Location = location,
                        Url = url,
                        EasyApply = false
                    });
                }
                catch { /* try next selector */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract Gupy job listings");
        }

        _logger.LogInformation("Extracted {Count} Gupy job listings", cards.Count);
        return cards;
    }

    public Task<bool> HasNextPageAsync(IPage page)
    {
        return Task.FromResult(false);
    }

    public Task GoToNextPageAsync(IPage page)
    {
        return Task.CompletedTask;
    }

    public Task NavigateToPageAsync(IPage page, SearchProfile profile, int pageNum)
    {
        // Gupy não tem paginação
        return Task.CompletedTask;
    }

    public async Task<JobDetail> ExtractJobDetailsAsync(IPage page, string url)
    {
        if (!page.Url.Contains(url) && !string.IsNullOrEmpty(url))
        {
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await _behavior.DelayAsync(1000, 2000);
        }

        try
        {
            var description = await page.EvaluateAsync<string>(@"
                const desc = document.querySelector('[data-testid=""job-description""], .job-description, [class*=""description""]');
                return desc ? desc.innerText.trim() : '';
            ");

            return new JobDetail
            {
                Description = description ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract Gupy job details");
            return new JobDetail { Description = string.Empty };
        }
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
