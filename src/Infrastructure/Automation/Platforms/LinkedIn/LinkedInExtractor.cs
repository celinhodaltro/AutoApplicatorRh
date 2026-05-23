using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;

public sealed class LinkedInExtractor
{
    private readonly ILogger _logger;
    private readonly HumanBehavior _behavior;

    private static readonly string[] JobCardSelectors =
    [
        ".job-card-container",
        ".jobs-search-results__list-item",
        "li[data-occludable-job-id]",
        ".scaffold-layout__list-item"
    ];

    private static readonly string[] CardTitleSelectors =
    [
        ".job-card-list__title",
        "a.job-card-container__link",
        ".artdeco-entity-lockup__title a",
        "a[class*=\"job-card-list__title\"]"
    ];

    private static readonly string[] CardCompanySelectors =
    [
        ".job-card-container__primary-description",
        ".artdeco-entity-lockup__subtitle",
        ".job-card-container__company-name"
    ];

    private static readonly string[] CardLocationSelectors =
    [
        ".job-card-container__metadata-item",
        ".artdeco-entity-lockup__caption",
        ".job-card-container__metadata-wrapper li"
    ];

    private static readonly string[] DetailDescriptionSelectors =
    [
        "#job-details",
        ".jobs-description__content",
        ".jobs-description-content__text",
        ".jobs-box__html-content",
        "article[class*=\"jobs-description\"]",
        ".jobs-description",
        "div[class*=\"description__text\"]",
        ".job-details-about-the-job-module"
    ];

    private static readonly string[] DetailSalarySelectors =
    [
        ".job-details-jobs-unified-top-card__job-insight span",
        "[class*=\"salary\"]",
        ".compensation__salary"
    ];

    private static readonly string[] DetailPostedDateSelectors =
    [
        ".job-details-jobs-unified-top-card__primary-description-container span",
        "time",
        ".jobs-unified-top-card__posted-date"
    ];

    public LinkedInExtractor(ILogger logger)
    {
        _logger = logger;
        _behavior = new HumanBehavior();
    }

    public async Task<List<ExtractedJob>> ExtractJobCardsAsync(IPage page)
    {
        var cards = new List<ExtractedJob>();
        var extractedIds = new HashSet<string>();

        IReadOnlyList<IElementHandle> cardElements = [];
        foreach (var sel in JobCardSelectors)
        {
            cardElements = await page.QuerySelectorAllAsync(sel);
            if (cardElements.Count > 0)
            {
                _logger.LogInformation("Found {Count} job cards using: {Selector}", cardElements.Count, sel);
                break;
            }
        }

        foreach (var el in cardElements)
        {
            try
            {
                var card = await ExtractSingleCardAsync(page, el);
                if (card is not null && extractedIds.Add(card.ExternalId))
                {
                    cards.Add(card);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract LinkedIn card");
            }
        }

        var allJobIds = await page.EvaluateAsync<string[]>(@"
            Array.from(document.querySelectorAll('li[data-occludable-job-id]'))
                .map(el => el.getAttribute('data-occludable-job-id'))
                .filter(Boolean)
        ").ConfigureAwait(false);

        var missingIds = allJobIds.Where(id => !extractedIds.Contains(id)).ToList();
        if (missingIds.Count > 0)
        {
            _logger.LogInformation("{Count} occluded items, scrolling to extract", missingIds.Count);

            foreach (var id in missingIds)
            {
                try
                {
                    await page.EvaluateAsync(@"(jobId) => {
                        const el = document.querySelector(`li[data-occludable-job-id=""${jobId}""]`);
                        if (el) el.scrollIntoView({ behavior: 'instant', block: 'center' });
                    }", id);

                    await _behavior.DelayAsync(250, 450);

                    var el = await page.QuerySelectorAsync($"li[data-occludable-job-id=\"{id}\"]");
                    if (el is null) continue;

                    var card = await ExtractSingleCardAsync(page, el);
                    if (card is not null)
                    {
                        cards.Add(card);
                        extractedIds.Add(card.ExternalId);
                    }
                    else
                    {
                        cards.Add(new ExtractedJob
                        {
                            ExternalId = id,
                            Title = string.Empty,
                            Company = string.Empty,
                            Location = string.Empty,
                            Url = $"https://www.linkedin.com/jobs/view/{id}/",
                            EasyApply = false
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract occluded card {Id}", id);
                }
            }
        }

        _logger.LogInformation("Extracted {Count} LinkedIn cards", cards.Count);
        return cards;
    }

    private async Task<ExtractedJob?> ExtractSingleCardAsync(IPage page, IElementHandle el)
    {
        var jobId = await el.GetAttributeAsync("data-occludable-job-id")
                 ?? await el.GetAttributeAsync("data-job-id")
                 ?? await ExtractJobIdFromHrefAsync(el);

        if (string.IsNullOrEmpty(jobId)) return null;

        var title = await GetFirstVisibleTextAsync(el, CardTitleSelectors);
        if (string.IsNullOrEmpty(title)) return null;

        var company = await GetFirstVisibleTextAsync(el, CardCompanySelectors);
        var location = await GetFirstVisibleTextAsync(el, CardLocationSelectors);

        var url = string.Empty;
        foreach (var sel in CardTitleSelectors)
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
            catch { /* try next */ }
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
        await _behavior.DelayAsync(800, 1500);

        var description = await QuickTextAsync(page, DetailDescriptionSelectors);
        if (string.IsNullOrEmpty(description))
        {
            description = await FallbackExtractDescriptionAsync(page);
            if (!string.IsNullOrEmpty(description))
                _logger.LogInformation("Fallback extracted description ({Length} chars)", description.Length);
        }

        var salary = await ExtractSalaryAsync(page);
        var postedDate = await QuickTextAsync(page, DetailPostedDateSelectors);

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

        return new JobDetail
        {
            Description = description ?? string.Empty,
            Salary = salary,
            PostedDate = postedDateStr
        };
    }

    private static async Task<string> GetFirstVisibleTextAsync(IElementHandle parent, string[] selectors)
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
        return string.Empty;
    }

    private static async Task<string> QuickTextAsync(IPage page, string[] selectors)
    {
        foreach (var sel in selectors)
        {
            try
            {
                var text = await page.Locator(sel).First.InnerTextAsync(new() { Timeout = 1500 });
                if (!string.IsNullOrEmpty(text?.Trim())) return text.Trim();
            }
            catch { /* try next */ }
        }
        return string.Empty;
    }

    private async Task<string?> ExtractSalaryAsync(IPage page)
    {
        foreach (var sel in DetailSalarySelectors)
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
            catch { /* try next */ }
        }

        try
        {
            return await page.EvaluateAsync<string?>(@"
                const allText = document.body.innerText;
                const match = allText.match(/\$[\d,]+(?:\/yr|\/hr|K?\s*-\s*\$[\d,]+(?:\/yr|\/hr|K)?)/i);
                return match ? match[0] : null;
            ");
        }
        catch { return null; }
    }

    private async Task<string> FallbackExtractDescriptionAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<string>(@"
                const containers = [
                    document.querySelector('.scaffold-layout__detail'),
                    document.querySelector('.jobs-search__job-details'),
                    document.querySelector('[class*=""job-details""]'),
                    document.querySelector('.job-view-layout')
                ].filter(Boolean);
                const root = containers[0] || document.body;
                let best = '';
                const candidates = root.querySelectorAll('div, section, article, span');
                for (const el of candidates) {
                    const t = (el.textContent || '').trim();
                    if (t.length > best.length && t.length > 100) {
                        const childBlocks = el.querySelectorAll('div, section, article');
                        if (childBlocks.length < 5) best = t;
                    }
                }
                return best;
            ");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallback description extraction failed");
            return string.Empty;
        }
    }

    private static async Task<string?> ExtractJobIdFromHrefAsync(IElementHandle el)
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
        catch { /* ignore */ }
        return null;
    }
}
