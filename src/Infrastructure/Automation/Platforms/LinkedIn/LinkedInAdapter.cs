using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;

public sealed class LinkedInAdapter : IPlatformAdapter
{
    public PlatformType Platform => PlatformType.LinkedIn;
    public string BaseUrl => "https://www.linkedin.com";

    private readonly LinkedInExtractor _extractor;
    private readonly IHumanBehavior _behavior;
    private readonly ILogger<LinkedInAdapter> _logger;

    public LinkedInAdapter(
        LinkedInExtractor extractor,
        IHumanBehavior behavior,
        ILogger<LinkedInAdapter> logger)
    {
        _extractor = extractor;
        _behavior = behavior;
        _logger = logger;
    }

    public async Task<AuthCheckResult> IsAuthenticatedAsync(IPage page)
    {
        foreach (var sel in LinkedInSelectors.AuthSelectors)
        {
            try
            {
                var locator = page.Locator(sel).First;
                await locator.WaitForAsync(new() { Timeout = 3000 });
                var visible = await locator.IsVisibleAsync();
                if (visible)
                    return new AuthCheckResult { IsAuthenticated = true };
            }
            catch { /* try next selector */ }
        }

        var url = page.Url;
        if (url.Contains("/feed") || url.Contains("/jobs") || url.Contains("/in/"))
        {
            try
            {
                var signInLocator = page
                    .Locator("a[data-tracking-control-name=\"guest_homepage-basic_nav-header-signin\"]")
                    .First;
                await signInLocator.WaitForAsync(new() { Timeout = 2000 });
                var signIn = await signInLocator.IsVisibleAsync();
                return new AuthCheckResult
                {
                    IsAuthenticated = !signIn,
                    LoginUrl = "https://www.linkedin.com/login",
                    Message = signIn ? "LinkedIn: please log in first" : ""
                };
            }
            catch { /* try next approach */ }
        }

        return new AuthCheckResult
        {
            IsAuthenticated = false,
            LoginUrl = "https://www.linkedin.com/login",
            Message = "LinkedIn: authentication required"
        };
    }

    public string BuildSearchUrl(SearchProfile profile, int pageNum = 1)
    {
        var queryParts = new List<string>();

        if (profile.Keywords.Count > 0 || profile.ExcludeTerms.Count > 0)
        {
            queryParts.AddRange(profile.Keywords);
            queryParts.AddRange(profile.ExcludeTerms.Select(t => $"-{t}"));
        }

        var parameters = new Dictionary<string, string>();

        if (queryParts.Count > 0)
            parameters["keywords"] = string.Join(" ", queryParts);

        if (profile.Location.Count > 0)
            parameters["location"] = profile.Location[0];

        if (!string.IsNullOrEmpty(profile.DatePosted) && LinkedInSelectors.DatePostedMap.TryGetValue(profile.DatePosted, out var tpr))
            parameters["f_TPR"] = tpr;

        if (profile.JobTypes.Count > 0)
        {
            var types = profile.JobTypes
                .Select(t => LinkedInSelectors.JobTypeMap.GetValueOrDefault(t))
                .Where(t => t is not null)
                .ToList();
            if (types.Count > 0)
                parameters["f_JT"] = string.Join(",", types);
        }

        if (profile.ExperienceLevel.Count > 0)
        {
            var levels = profile.ExperienceLevel
                .Select(l => LinkedInSelectors.ExperienceMap.GetValueOrDefault(l))
                .Where(l => l is not null)
                .ToList();
            if (levels.Count > 0)
                parameters["f_E"] = string.Join(",", levels);
        }

        if (profile.RemoteOnly)
            parameters["f_WT"] = "2";

        if (profile.SalaryMin.HasValue)
        {
            var buckets = new[] { 40000, 60000, 80000, 100000, 120000, 140000, 160000, 180000, 200000 };
            var bucket = Array.FindLast(buckets, b => b <= profile.SalaryMin.Value);
            if (bucket > 0)
            {
                var code = Array.IndexOf(buckets, bucket) + 1;
                parameters["f_SB2"] = code.ToString();
            }
        }

        if (profile.EasyApplyOnly)
            parameters["f_AL"] = "true";

        if (pageNum > 1)
            parameters["start"] = ((pageNum - 1) * 25).ToString();

        var paramString = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return $"{BaseUrl}/jobs/search/?{paramString}";
    }

    public async Task<List<ExtractedJob>> ExtractListingsAsync(IPage page)
    {
        foreach (var sel in LinkedInSelectors.ListSelectors)
        {
            var visible = await page.Locator(sel).First.IsVisibleAsync();
            if (visible)
            {
                await _behavior.ScrollListAsync(page, sel);
                break;
            }
        }

        return await _extractor.ExtractJobCardsAsync(page);
    }

    public async Task<bool> HasNextPageAsync(IPage page)
    {
        foreach (var sel in LinkedInSelectors.PaginationSelectors)
        {
            try
            {
                var locator = page.Locator(sel).First;
                var visible = await locator.IsVisibleAsync();
                if (visible)
                {
                    var disabled = await locator.GetAttributeAsync("disabled");
                    return disabled is null;
                }
            }
            catch { /* try next */ }
        }
        return false;
    }

    public async Task GoToNextPageAsync(IPage page)
    {
        foreach (var sel in LinkedInSelectors.PaginationSelectors)
        {
            try
            {
                var visible = await page.Locator(sel).First.IsVisibleAsync();
                if (visible)
                {
                    await _behavior.HumanClickAsync(page, sel);
                    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                    await _behavior.DelayAsync(2000, 4000);
                    return;
                }
            }
            catch { /* try next */ }
        }
        _logger.LogWarning("Could not find next page button on LinkedIn");
    }

    public async Task NavigateToPageAsync(IPage page, SearchProfile profile, int pageNum)
    {
        var url = BuildSearchUrl(profile, pageNum);
        _logger.LogInformation("Navigating to page {PageNum}: {Url}", pageNum, url);
        
        await page.GotoAsync(url, new() 
        { 
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30000 
        });
        
        // Wait for job cards to actually render (lazy loading)
        var cardsFound = false;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            foreach (var sel in LinkedInSelectors.NavigateCardSelectors)
            {
                try
                {
                    var count = await page.Locator(sel).CountAsync();
                    if (count > 0)
                    {
                        _logger.LogInformation("Page {PageNum}: Found {Count} job cards via '{Selector}'", pageNum, count, sel);
                        cardsFound = true;
                        break;
                    }
                }
                catch { }
            }
            
            if (cardsFound) break;
            
            _logger.LogInformation("Page {PageNum}: Waiting for job cards to load (attempt {Attempt})...", pageNum, attempt + 1);
            await Task.Delay(2000);
        }
        
        if (!cardsFound)
        {
            _logger.LogWarning("Page {PageNum}: No job cards found after multiple attempts", pageNum);
        }
        
        // Extra scroll to trigger lazy loading
        try
        {
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
            await Task.Delay(1000);
            await page.EvaluateAsync("window.scrollTo(0, 0)");
            await Task.Delay(500);
        }
        catch { }
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
}
