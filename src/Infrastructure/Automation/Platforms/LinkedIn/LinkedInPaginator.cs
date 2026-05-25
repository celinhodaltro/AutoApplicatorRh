using AutoApplicator.Domain.Entities;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;

public sealed class LinkedInPaginator
{
    private readonly IHumanBehavior _behavior;
    private readonly ILogger<LinkedInPaginator> _logger;
    private readonly LinkedInDedupService _dedup;
    private readonly LinkedInExtractor _extractor;
    private readonly LinkedInAdapter _adapter;
    private static readonly int JobsPerPage = 25;

    public LinkedInPaginator(
        IHumanBehavior behavior,
        ILogger<LinkedInPaginator> logger,
        LinkedInDedupService dedup,
        LinkedInExtractor extractor,
        LinkedInAdapter adapter)
    {
        _behavior = behavior;
        _logger = logger;
        _dedup = dedup;
        _extractor = extractor;
        _adapter = adapter;
    }

    public async Task<List<ExtractedJob>> GetAllPagesAsync(
        IPage page,
        SearchProfile profile,
        int maxJobs,
        CancellationToken ct,
        bool globalEasyApply = false)
    {
        var allJobs = new List<ExtractedJob>();
        var pageNum = 1;

        while (!ct.IsCancellationRequested && allJobs.Count < maxJobs)
        {
            var url = BuildPageUrl(profile, pageNum);
            _logger.LogInformation("Paginator: navigating to page {PageNum}: {Url}", pageNum, url);

            await page.GotoAsync(url, new()
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30000
            });

            await WaitForCardsAsync(page);

            var jobs = await _extractor.ExtractJobCardsAsync(page);

            if (jobs.Count == 0) break;

            // Deduplicate
            var newJobs = jobs.Where(j => _dedup.TryAdd(j.ExternalId)).ToList();
            allJobs.AddRange(newJobs);

            _logger.LogInformation("Paginator: page {PageNum}: {Total} found, {New} new, {AllTotal} total",
                pageNum, jobs.Count, newJobs.Count, allJobs.Count);

            if (jobs.Count < JobsPerPage) break; // last page
            pageNum++;
        }

        return allJobs;
    }

    private string BuildPageUrl(SearchProfile profile, int pageNum)
    {
        return _adapter.BuildSearchUrl(profile, pageNum);
    }

    private async Task WaitForCardsAsync(IPage page)
    {
        // Usa os mesmos selectors do LinkedInSelectors
        var cardSelectors = LinkedInSelectors.NavigateCardSelectors;

        var cardsFound = false;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            foreach (var sel in cardSelectors)
            {
                try
                {
                    var count = await page.Locator(sel).CountAsync();
                    if (count > 0)
                    {
                        cardsFound = true;
                        break;
                    }
                }
                catch { }
            }
            if (cardsFound) break;
            _logger.LogInformation("Paginator: waiting for cards (attempt {Attempt})...", attempt + 1);
            await Task.Delay(2000);
        }

        // Scroll to trigger lazy loading
        try
        {
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
            await Task.Delay(1000);
            await page.EvaluateAsync("window.scrollTo(0, 0)");
            await Task.Delay(500);
        }
        catch { }
    }
}
