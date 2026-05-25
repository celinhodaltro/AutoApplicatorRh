using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;

public sealed class LinkedInExtractor
{
    private readonly IEnumerable<IDescriptionExtractor> _descriptionExtractors;
    private readonly IHumanBehavior _behavior;
    private readonly ILogger<LinkedInExtractor> _logger;

    public LinkedInExtractor(
        IEnumerable<IDescriptionExtractor> descriptionExtractors,
        IHumanBehavior behavior,
        ILogger<LinkedInExtractor> logger)
    {
        _descriptionExtractors = descriptionExtractors;
        _behavior = behavior;
        _logger = logger;
    }

    public async Task<List<ExtractedJob>> ExtractJobCardsAsync(IPage page)
    {
        var cards = new List<ExtractedJob>();
        var extractedIds = new HashSet<string>();

        var cardElements = await FindJobCardElementsAsync(page);
        await ParseVisibleCardsAsync(cardElements, cards, extractedIds);

        var allJobIds = await GetAllJobIdsAsync(page);
        await ExtractOccludedCardsAsync(page, allJobIds, extractedIds, cards);

        _logger.LogInformation("Extracted {Count} LinkedIn cards", cards.Count);
        return cards;
    }

    private async Task<IReadOnlyList<IElementHandle>> FindJobCardElementsAsync(IPage page)
    {
        foreach (var sel in LinkedInSelectors.JobCardSelectors)
        {
            var cardElements = await page.QuerySelectorAllAsync(sel);
            if (cardElements.Count > 0)
            {
                _logger.LogInformation("Found {Count} job cards using: {Selector}", cardElements.Count, sel);
                return cardElements;
            }
        }
        return [];
    }

    private async Task ParseVisibleCardsAsync(
        IReadOnlyList<IElementHandle> cardElements, List<ExtractedJob> cards, HashSet<string> extractedIds)
    {
        foreach (var el in cardElements)
        {
            try
            {
                var card = await ExtractSingleCardAsync(el);
                if (card is not null && extractedIds.Add(card.ExternalId))
                    cards.Add(card);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract LinkedIn card");
            }
        }
    }

    private static async Task<string[]> GetAllJobIdsAsync(IPage page)
    {
        return await page.EvaluateAsync<string[]>(@"
            Array.from(document.querySelectorAll('li[data-occludable-job-id]'))
                .map(el => el.getAttribute('data-occludable-job-id'))
                .filter(Boolean)
        ").ConfigureAwait(false);
    }

    private async Task ExtractOccludedCardsAsync(
        IPage page, string[] allJobIds, HashSet<string> extractedIds, List<ExtractedJob> cards)
    {
        var missingIds = allJobIds.Where(id => !extractedIds.Contains(id)).ToList();
        if (missingIds.Count == 0) return;

        _logger.LogInformation("{Count} occluded items, scrolling to extract", missingIds.Count);

        foreach (var id in missingIds)
        {
            try
            {
                await ScrollJobCardIntoViewAsync(page, id);

                var el = await page.QuerySelectorAsync($"li[data-occludable-job-id=\"{id}\"]");
                if (el is null) continue;

                var card = await ExtractSingleCardAsync(el);
                if (card is not null)
                {
                    cards.Add(card);
                    extractedIds.Add(card.ExternalId);
                }
                else
                {
                    cards.Add(CreateEmptyJobEntry(id));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract occluded card {Id}", id);
            }
        }
    }

    private async Task ScrollJobCardIntoViewAsync(IPage page, string jobId)
    {
        await page.EvaluateAsync(@"(jobId) => {
            const el = document.querySelector(`li[data-occludable-job-id=""${jobId}""]`);
            if (el) el.scrollIntoView({ behavior: 'instant', block: 'center' });
        }", jobId);

        await _behavior.DelayAsync(250, 450);
    }

    private static ExtractedJob CreateEmptyJobEntry(string jobId)
    {
        return new ExtractedJob
        {
            ExternalId = jobId,
            Title = string.Empty,
            Company = string.Empty,
            Location = string.Empty,
            Url = $"https://www.linkedin.com/jobs/view/{jobId}/",
            EasyApply = false
        };
    }

    private async Task<ExtractedJob?> ExtractSingleCardAsync(IElementHandle el)
    {
        var jobId = await el.GetAttributeAsync("data-occludable-job-id")
                 ?? await el.GetAttributeAsync("data-job-id")
                 ?? await ExtractJobIdFromHrefAsync(el);

        if (string.IsNullOrEmpty(jobId)) return null;

        var title = await SelectorHelper.GetFirstVisibleTextAsync(el, LinkedInSelectors.CardTitleSelectors);
        if (string.IsNullOrEmpty(title)) return null;

        var company = await SelectorHelper.GetFirstVisibleTextAsync(el, LinkedInSelectors.CardCompanySelectors);
        var location = await SelectorHelper.GetFirstVisibleTextAsync(el, LinkedInSelectors.CardLocationSelectors);

        var url = string.Empty;
        foreach (var sel in LinkedInSelectors.CardTitleSelectors)
        {
            try
            {
                var linkEl = await el.QuerySelectorAsync(sel);
                if (linkEl is null) continue;
                var href = await linkEl.GetAttributeAsync("href");
                if (!string.IsNullOrEmpty(href))
                {
                    url = href.StartsWith("http") ? href : $"https://www.linkedin.com{href}";
                    break;
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "URL extraction failed via selector '{Selector}'", sel); }
        }

        var cardText = ((await el.InnerTextAsync()) ?? string.Empty).ToLowerInvariant();
        var easyApply = cardText.Contains("easy apply") || cardText.Contains("candidatura simplificada");

        return new ExtractedJob
        {
            ExternalId = jobId,
            Title = title,
            Company = company,
            Location = location,
            Url = url,
            EasyApply = easyApply
        };
    }

    public async Task<JobDetail> ExtractJobDetailsAsync(IPage page)
    {
        await _behavior.DelayAsync(2000, 3000);

        var description = await ExtractDescriptionAsync(page);

        var salary = await ExtractSalaryAsync(page);

        var postedDate = await SelectorHelper.QuickTextAsync(page, LinkedInSelectors.DetailPostedDateSelectors);
        string? postedDateStr = null;
        if (!string.IsNullOrEmpty(postedDate))
        {
            var lower = postedDate.ToLowerInvariant();
            if (lower.Contains("ago") || lower.Contains("hour") || lower.Contains("day") ||
                lower.Contains("week") || lower.Contains("reposted"))
            {
                postedDateStr = postedDate;
            }
        }

        if (string.IsNullOrEmpty(description))
            _logger.LogWarning("Could not extract description for this job");

        return new JobDetail
        {
            Description = description ?? string.Empty,
            Salary = salary,
            PostedDate = postedDateStr
        };
    }

    private async Task<string> ExtractDescriptionAsync(IPage page)
    {
        var html = await page.ContentAsync();
        foreach (var extractor in _descriptionExtractors.OrderBy(e => e.Priority))
        {
            var result = await extractor.ExtractAsync(page, html);
            if (!string.IsNullOrEmpty(result))
            {
                _logger.LogInformation("Description extracted via {Extractor} ({Length} chars)",
                    extractor.GetType().Name, result.Length);
                return result;
            }
        }
        return string.Empty;
    }

    private async Task<string?> ExtractSalaryAsync(IPage page)
    {
        foreach (var sel in LinkedInSelectors.DetailSalarySelectors)
        {
            try
            {
                var elements = await page.QuerySelectorAllAsync(sel);
                foreach (var el in elements)
                {
                    var text = ((await el.InnerTextAsync()) ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(text) && (text.Contains('$') || text.Contains("/yr") || text.Contains("/hr")))
                        return text;
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Salary extraction failed via selector '{Selector}'", sel); }
        }

        try
        {
            return await page.EvaluateAsync<string?>(@"
                const allText = document.body.innerText;
                const match = allText.match(/\$[\d,]+(?:\/yr|\/hr|K?\s*-\s*\$[\d,]+(?:\/yr|\/hr|K)?)/i);
                return match ? match[0] : null;
            ");
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Salary fallback regex extraction failed"); return null; }
    }



    private async Task<string?> ExtractJobIdFromHrefAsync(IElementHandle el)
    {
        try
        {
            var links = await el.QuerySelectorAllAsync("a");
            foreach (var link in links)
            {
                var href = await link.GetAttributeAsync("href");
                if (string.IsNullOrEmpty(href)) continue;

                var viewMatch = System.Text.RegularExpressions.Regex.Match(href, @"/jobs/view/(\d+)");
                if (viewMatch.Success) return viewMatch.Groups[1].Value;

                var paramMatch = System.Text.RegularExpressions.Regex.Match(href, @"currentJobId=(\d+)");
                if (paramMatch.Success) return paramMatch.Groups[1].Value;
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Job ID extraction from href failed"); }
        return null;
    }
}
