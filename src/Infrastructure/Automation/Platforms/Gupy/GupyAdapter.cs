using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Models;
using AutoApplicator.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Gupy;

public sealed class GupyAdapter : IPlatformAdapter
{
    public PlatformType Platform => PlatformType.Gupy;
    public string BaseUrl => "https://portal.gupy.io";

    private readonly GupyExtractor _extractor;
    private readonly IHumanBehavior _behavior;
    private readonly ILogger<GupyAdapter> _logger;

    public GupyAdapter(
        ILogger<GupyAdapter> logger,
        GupyExtractor extractor,
        IHumanBehavior behavior)
    {
        _extractor = extractor;
        _behavior = behavior;
        _logger = logger;
    }

    public async Task<AuthCheckResult> IsAuthenticatedAsync(IBrowserPage page)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        try
        {
            // Use QuerySelectorAsync which checks DOM presence, NOT visibility
            var loginBtn = await innerPage.QuerySelectorAsync("button:has-text(\"Entrar\"), button[data-testid=\"header-login-button\"], button#button-login");

            if (loginBtn is not null)
            {
                return new AuthCheckResult
                {
                    IsAuthenticated = false,
                    LoginUrl = "https://login.gupy.io/candidates/signin",
                    Message = "Gupy: please log in first"
                };
            }
        }
        catch { /* element not found in DOM = user is logged in */ }

        return new AuthCheckResult { IsAuthenticated = true };
    }

    public string BuildSearchUrl(SearchProfile profile, int pageNum = 1)
    {
        var keywords = string.Join(" ", profile.Keywords);
        return GupySelectors.BuildSearchUrl(keywords, pageNum);
    }

    public async Task<List<ExtractedJob>> ExtractListingsAsync(IBrowserPage page)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        return await _extractor.ExtractJobCardsAsync(innerPage);
    }

    public async Task<bool> HasNextPageAsync(IBrowserPage page)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        try
        {
            var nextBtn = await innerPage.QuerySelectorAsync(GupySelectors.NextPageButton);
            if (nextBtn is not null)
            {
                var isDisabled = await nextBtn.GetAttributeAsync("aria-disabled");
                if (isDisabled == "true") return false;

                var classAttr = await nextBtn.GetAttributeAsync("class");
                if (classAttr is not null && classAttr.Contains("disabled", StringComparison.OrdinalIgnoreCase))
                    return false;

                return true;
            }

            // Fallback: if we have job cards, assume there might be more pages
            var jobCards = await innerPage.QuerySelectorAllAsync(GupySelectors.JobCardSelector);
            if (jobCards.Count >= 20)
            {
                // Check if there's a visible pagination element
                var pagination = await innerPage.QuerySelectorAsync("[aria-label=\"Página\"]");
                return pagination is not null;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for next Gupy page");
            return false;
        }
    }

    public async Task GoToNextPageAsync(IBrowserPage page)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        try
        {
            var nextBtn = await innerPage.QuerySelectorAsync(GupySelectors.NextPageButton);
            if (nextBtn is not null)
            {
                await nextBtn.ClickAsync();
                await innerPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await _behavior.DelayAsync(2000, 3000);
                return;
            }

            _logger.LogWarning("Next page button not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to go to next Gupy page");
        }
    }

    public async Task NavigateToPageAsync(IBrowserPage page, SearchProfile profile, int pageNum)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        var keywords = string.Join(" ", profile.Keywords);
        var url = GupySelectors.BuildSearchUrl(keywords, pageNum);
        await innerPage.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await _behavior.DelayAsync(2000, 3000);
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
