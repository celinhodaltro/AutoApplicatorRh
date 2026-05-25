using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Gupy;

public sealed class GupyPaginator
{
    private readonly IHumanBehavior _behavior;
    private readonly ILogger<GupyPaginator> _logger;
    private readonly GupyExtractor _extractor;
    private static readonly int JobsPerPage = 20;

    public GupyPaginator(
        IHumanBehavior behavior,
        ILogger<GupyPaginator> logger,
        GupyExtractor extractor)
    {
        _behavior = behavior;
        _logger = logger;
        _extractor = extractor;
    }

    public async Task<List<ExtractedJob>> GetAllPagesAsync(
        IPage page,
        string keywords,
        int maxJobs,
        CancellationToken ct)
    {
        var allJobs = new List<ExtractedJob>();
        var pageNum = 1;

        while (!ct.IsCancellationRequested && allJobs.Count < maxJobs)
        {
            var url = GupySelectors.BuildSearchUrl(keywords, pageNum);
            _logger.LogInformation("GupyPaginator: navigating to page {PageNum}: {Url}", pageNum, url);

            await page.GotoAsync(url, new()
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30000
            });

            await _behavior.DelayAsync(2000, 3000);

            var jobs = await _extractor.ExtractJobCardsAsync(page);

            if (jobs.Count == 0) break;

            allJobs.AddRange(jobs);

            _logger.LogInformation("GupyPaginator: page {PageNum}: {Total} found, {AllTotal} total",
                pageNum, jobs.Count, allJobs.Count);

            if (jobs.Count < JobsPerPage) break; // last page
            pageNum++;
        }

        return allJobs;
    }
}
