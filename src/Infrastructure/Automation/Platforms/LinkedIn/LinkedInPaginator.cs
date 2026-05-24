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
    private static readonly int JobsPerPage = 25;

    public LinkedInPaginator(
        IHumanBehavior behavior,
        ILogger<LinkedInPaginator> logger,
        LinkedInDedupService dedup,
        LinkedInExtractor extractor)
    {
        _behavior = behavior;
        _logger = logger;
        _dedup = dedup;
        _extractor = extractor;
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

    private static string BuildPageUrl(SearchProfile profile, int pageNum)
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

        // Usa os mesmos dicionários do LinkedInSelectors (já existentes)
        if (!string.IsNullOrEmpty(profile.DatePosted) && LinkedInSelectors.DatePostedMap.TryGetValue(profile.DatePosted, out var tpr))
            parameters["f_TPR"] = tpr;

        if (profile.JobTypes.Count > 0)
        {
            var types = profile.JobTypes
                .Select(t => LinkedInSelectors.JobTypeMap.GetValueOrDefault(t))
                .Where(t => t is not null).ToList();
            if (types.Count > 0)
                parameters["f_JT"] = string.Join(",", types);
        }

        if (profile.ExperienceLevel.Count > 0)
        {
            var levels = profile.ExperienceLevel
                .Select(l => LinkedInSelectors.ExperienceMap.GetValueOrDefault(l))
                .Where(l => l is not null).ToList();
            if (levels.Count > 0)
                parameters["f_E"] = string.Join(",", levels);
        }

        if (profile.RemoteOnly) parameters["f_WT"] = "2";

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

        if (profile.EasyApplyOnly) parameters["f_AL"] = "true";

        if (pageNum > 1)
            parameters["start"] = ((pageNum - 1) * 25).ToString();

        var paramString = string.Join("&", parameters.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return $"https://www.linkedin.com/jobs/search/?{paramString}";
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
