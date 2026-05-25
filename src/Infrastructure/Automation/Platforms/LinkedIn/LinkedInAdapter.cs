using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Models;
using AutoApplicator.Infrastructure.Services;
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

    public async Task<AuthCheckResult> IsAuthenticatedAsync(IBrowserPage page)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        foreach (var sel in LinkedInSelectors.AuthSelectors)
        {
            try
            {
                var locator = innerPage.Locator(sel).First;
                await locator.WaitForAsync(new() { Timeout = 3000 });
                var visible = await locator.IsVisibleAsync();
                if (visible)
                    return new AuthCheckResult { IsAuthenticated = true };
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Auth selector '{Selector}' not found", sel); }
        }

        var url = innerPage.Url;
        if (url.Contains("/feed") || url.Contains("/jobs") || url.Contains("/in/"))
        {
            try
            {
                var signInLocator = innerPage
                    .Locator("a[data-tracking-control-name=\"guest_homepage-basic_nav-header-signin\"]")
                    .First;
                await signInLocator.WaitForAsync(new() { Timeout = 2000 });
                var signIn = await signInLocator.IsVisibleAsync();
                return new AuthCheckResult
                {
                    IsAuthenticated = !signIn,
                    LoginUrl = "https://www.linkedin.com/login/pt",
                    Message = signIn ? "LinkedIn: please log in first" : ""
                };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Sign-in button not found — user is likely logged in");
                return new AuthCheckResult { IsAuthenticated = true };
            }
        }

        return new AuthCheckResult
        {
            IsAuthenticated = false,
            LoginUrl = "https://www.linkedin.com/login/pt",
            Message = "LinkedIn: authentication required"
        };
    }

    public string BuildSearchUrl(SearchProfile profile, int pageNum = 1)
    {
        var parameters = new Dictionary<string, string>();

        AppendKeywordParameter(profile, parameters);
        AppendLocationParameter(profile, parameters);
        AppendDatePostedParameter(profile, parameters);
        AppendJobTypeParameter(profile, parameters);
        AppendExperienceLevelParameter(profile, parameters);
        AppendRemoteFilter(profile, parameters);
        AppendSalaryFilter(profile, parameters);
        AppendEasyApplyFilter(profile, parameters);
        AppendPaginationParameter(pageNum, parameters);

        var paramString = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return $"{BaseUrl}/jobs/search/?{paramString}";
    }

    private static void AppendKeywordParameter(SearchProfile profile, Dictionary<string, string> parameters)
    {
        var queryParts = new List<string>();
        if (profile.Keywords.Count > 0 || profile.ExcludeTerms.Count > 0)
        {
            queryParts.AddRange(profile.Keywords);
            queryParts.AddRange(profile.ExcludeTerms.Select(t => $"-{t}"));
        }
        if (queryParts.Count > 0)
            parameters["keywords"] = string.Join(" ", queryParts);
    }

    private static void AppendLocationParameter(SearchProfile profile, Dictionary<string, string> parameters)
    {
        if (profile.Location.Count > 0)
            parameters["location"] = profile.Location[0];
    }

    private static void AppendDatePostedParameter(SearchProfile profile, Dictionary<string, string> parameters)
    {
        if (!string.IsNullOrEmpty(profile.DatePosted) && LinkedInSelectors.DatePostedMap.TryGetValue(profile.DatePosted, out var tpr))
            parameters["f_TPR"] = tpr;
    }

    private static void AppendJobTypeParameter(SearchProfile profile, Dictionary<string, string> parameters)
    {
        if (profile.JobTypes.Count > 0)
        {
            var types = profile.JobTypes
                .Select(t => LinkedInSelectors.JobTypeMap.GetValueOrDefault(t))
                .Where(t => t is not null)
                .ToList();
            if (types.Count > 0)
                parameters["f_JT"] = string.Join(",", types);
        }
    }

    private static void AppendExperienceLevelParameter(SearchProfile profile, Dictionary<string, string> parameters)
    {
        if (profile.ExperienceLevel.Count > 0)
        {
            var levels = profile.ExperienceLevel
                .Select(l => LinkedInSelectors.ExperienceMap.GetValueOrDefault(l))
                .Where(l => l is not null)
                .ToList();
            if (levels.Count > 0)
                parameters["f_E"] = string.Join(",", levels);
        }
    }

    private static void AppendRemoteFilter(SearchProfile profile, Dictionary<string, string> parameters)
    {
        if (profile.RemoteOnly)
            parameters["f_WT"] = "2";
    }

    private static void AppendSalaryFilter(SearchProfile profile, Dictionary<string, string> parameters)
    {
        if (!profile.SalaryMin.HasValue) return;

        var buckets = new[] { 40000, 60000, 80000, 100000, 120000, 140000, 160000, 180000, 200000 };
        var bucket = Array.FindLast(buckets, b => b <= profile.SalaryMin.Value);
        if (bucket > 0)
        {
            var code = Array.IndexOf(buckets, bucket) + 1;
            parameters["f_SB2"] = code.ToString();
        }
    }

    private static void AppendEasyApplyFilter(SearchProfile profile, Dictionary<string, string> parameters)
    {
        if (profile.EasyApplyOnly)
            parameters["f_AL"] = "true";
    }

    private static void AppendPaginationParameter(int pageNum, Dictionary<string, string> parameters)
    {
        if (pageNum > 1)
            parameters["start"] = ((pageNum - 1) * 25).ToString();
    }

    public async Task<List<ExtractedJob>> ExtractListingsAsync(IBrowserPage page)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        foreach (var sel in LinkedInSelectors.ListSelectors)
        {
            var visible = await innerPage.Locator(sel).First.IsVisibleAsync();
            if (visible)
            {
                await _behavior.ScrollListAsync(innerPage, sel);
                break;
            }
        }

        return await _extractor.ExtractJobCardsAsync(innerPage);
    }

    public async Task NavigateToPageAsync(IBrowserPage page, SearchProfile profile, int pageNum)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        var url = BuildSearchUrl(profile, pageNum);
        _logger.LogInformation("Navigating to page {PageNum}: {Url}", pageNum, url);
        
        await innerPage.GotoAsync(url, new() 
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
                    var count = await innerPage.Locator(sel).CountAsync();
                    if (count > 0)
                    {
                        _logger.LogInformation("Page {PageNum}: Found {Count} job cards via '{Selector}'", pageNum, count, sel);
                        cardsFound = true;
                        break;
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Card selector '{Selector}' check failed", sel); }
            }
            
            if (cardsFound) break;
            
            _logger.LogInformation("Page {PageNum}: Waiting for job cards to load (attempt {Attempt})...", pageNum, attempt + 1);
            await Task.Delay(2000);
        }
        
        if (!cardsFound)
            _logger.LogWarning("Page {PageNum}: No job cards found after multiple attempts", pageNum);
        
        await ScrollPageToTriggerLazyLoadingAsync(innerPage);
    }

    private async Task ScrollPageToTriggerLazyLoadingAsync(IPage page)
    {
        try
        {
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
            await Task.Delay(1000);
            await page.EvaluateAsync("window.scrollTo(0, 0)");
            await Task.Delay(500);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Lazy loading scroll failed (non-critical)"); }
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
