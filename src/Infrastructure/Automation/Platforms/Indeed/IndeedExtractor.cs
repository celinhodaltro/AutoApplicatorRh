using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Indeed;

public sealed class IndeedExtractor
{
    private readonly ILogger _logger;
    private readonly HumanBehavior _behavior;

    private static readonly string[] JobCardSelectors =
    [
        ".job_seen_beacon",
        ".resultContent",
        ".cardOutline",
        "[data-jk]",
        "li.css-1ac2h1y"
    ];

    private static readonly string[] CardTitleSelectors =
    [
        ".jobTitle a",
        "h2.jobTitle span",
        "a[data-jk] span[title]",
        ".jcs-JobTitle span"
    ];

    private static readonly string[] CardCompanySelectors =
    [
        "[data-testid=\"company-name\"]",
        ".companyName",
        ".company_location .companyName",
        "span.css-92r8pb"
    ];

    private static readonly string[] CardLocationSelectors =
    [
        "[data-testid=\"text-location\"]",
        ".companyLocation",
        ".company_location .companyLocation",
        ".css-1p0sjhy"
    ];

    private static readonly string[] DetailDescriptionSelectors =
    [
        "#jobDescriptionText",
        ".jobsearch-JobComponent-description",
        "[id=\"jobDescriptionText\"]",
        ".jobsearch-jobDescriptionText",
        "#jobDescription",
        ".jobsearch-BodyContainer",
        "div[class*=\"jobDescription\"]",
        "[data-testid=\"jobDescriptionText\"]"
    ];

    private static readonly string[] DetailSalarySelectors =
    [
        "#salaryInfoAndJobType",
        "[data-testid=\"attribute_snippet_testid\"]",
        ".salary-snippet-container",
        ".css-1bkk2ja"
    ];

    private static readonly string[] QuickApplySelectors =
    [
        ".quick-apply-pill",
        "[data-testid=\"quick-apply-pill\"]",
        ".indeed-apply-pill",
        "span[class*=\"quick\"]",
        "span[class*=\"apply\"]",
        ".css-1f4l2z0",
        ".jobCardShelfContainer"
    ];

    public IndeedExtractor(ILogger logger)
    {
        _logger = logger;
        _behavior = new HumanBehavior();
    }

    public async Task<List<ExtractedJob>> ExtractJobCardsAsync(IPage page)
    {
        var cardElements = await FindJobCardElementsAsync(page);
        if (cardElements.Count == 0) return [];

        var cards = new List<ExtractedJob>();
        foreach (var el in cardElements)
        {
            try
            {
                var card = await ExtractCardDataAsync(el);
                if (card is not null)
                    cards.Add(card);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract Indeed card");
            }
        }

        _logger.LogInformation("Extracted {Count} Indeed cards from {Total} elements", cards.Count, cardElements.Count);
        return cards;
    }

    private async Task<IReadOnlyList<IElementHandle>> FindJobCardElementsAsync(IPage page)
    {
        foreach (var sel in JobCardSelectors)
        {
            var cardElements = await page.QuerySelectorAllAsync(sel);
            if (cardElements.Count > 0)
            {
                _logger.LogInformation("Found {Count} Indeed cards using: {Selector}", cardElements.Count, sel);
                return cardElements;
            }
        }

        _logger.LogWarning("No Indeed job cards found on page");
        return [];
    }

    private async Task<ExtractedJob?> ExtractCardDataAsync(IElementHandle el)
    {
        var jobKey = await el.GetAttributeAsync("data-jk")
                  ?? await ExtractJobKeyFromLinkAsync(el);

        if (string.IsNullOrEmpty(jobKey)) return null;

        var title = await GetFirstVisibleTextAsync(el, CardTitleSelectors);
        if (string.IsNullOrEmpty(title)) return null;

        var company = await GetFirstVisibleTextAsync(el, CardCompanySelectors);
        var location = await GetFirstVisibleTextAsync(el, CardLocationSelectors);

        var url = await ExtractCardUrlAsync(el, jobKey);

        var cardText = ((await el.InnerTextAsync()) ?? string.Empty).ToLowerInvariant();
        var easyApply = await DetectEasyApplyAsync(el, cardText);

        return new ExtractedJob
        {
            ExternalId = jobKey,
            Title = title,
            Company = company,
            Location = location,
            Url = url,
            EasyApply = easyApply
        };
    }

    private async Task<string> ExtractCardUrlAsync(IElementHandle el, string jobKey)
    {
        try
        {
            var linkEl = await el.QuerySelectorAsync("a[data-jk], .jobTitle a, h2.jobTitle a");
            if (linkEl is not null)
            {
                var href = await linkEl.GetAttributeAsync("href");
                if (!string.IsNullOrEmpty(href))
                {
                    return href.StartsWith("http") ? href
                        : href.StartsWith("/") ? $"{_baseUrl(href)}{href}"
                        : $"{_baseUrl(href)}/{href}";
                }
            }
        }
        catch { /* try next selector */ }

        return $"https://br.indeed.com/viewjob?jk={jobKey}";
    }

    private async Task<bool> DetectEasyApplyAsync(IElementHandle el, string cardText)
    {
        if (cardText.Contains("quick apply")
            || cardText.Contains("candidatar-se com o indeed")
            || cardText.Contains("candidatar-se")
            || cardText.Contains("candidatura rápida")
            || cardText.Contains("apply now")
            || cardText.Contains("candidatura simplificada")
            || cardText.Contains("apply with indeed"))
        {
            return true;
        }

        foreach (var sel in QuickApplySelectors)
        {
            try
            {
                var badge = await el.QuerySelectorAsync(sel);
                if (badge is null) continue;
                var badgeText = ((await badge.InnerTextAsync()) ?? string.Empty).ToLowerInvariant();
                if (badgeText.Contains("quick") || badgeText.Contains("apply") || badgeText.Contains("rápida"))
                    return true;
            }
            catch { /* try next */ }
        }

        return false;
    }

    public async Task<JobDetail> ExtractJobDetailsAsync(IPage page)
    {
        await _behavior.DelayAsync(500, 1000);

        var description = await QuickTextAsync(page, DetailDescriptionSelectors);
        if (string.IsNullOrEmpty(description))
        {
            _logger.LogInformation("Known selectors failed for Indeed description, trying fallback");
            description = await FallbackExtractDescriptionAsync(page);
        }

        string? salary = null;
        foreach (var sel in DetailSalarySelectors)
        {
            try
            {
                var elements = await page.QuerySelectorAllAsync(sel);
                foreach (var el in elements)
                {
                    var text = ((await el.InnerTextAsync()) ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(text) && (text.Contains('$') || text.Contains("/yr") || text.Contains("/hr")))
                    {
                        salary = text;
                        break;
                    }
                }
                if (salary is not null) break;
            }
            catch { /* try next */ }
        }

        string? title = null;
        try
        {
            var titleEl = await page.QuerySelectorAsync("h1.jobsearch-JobInfoHeader-title, h2.jobTitle");
            if (titleEl is not null)
                title = ((await titleEl.InnerTextAsync()) ?? string.Empty).Trim();
        }
        catch { /* try next selector */ }

        return new JobDetail
        {
            Description = description ?? string.Empty,
            Salary = salary,
            Title = title
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

    private async Task<string> FallbackExtractDescriptionAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<string>(@"
                let best = '';
                const candidates = document.querySelectorAll('div, section, article');
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
            _logger.LogWarning(ex, "Fallback Indeed description extraction failed");
            return string.Empty;
        }
    }

    private static async Task<string?> ExtractJobKeyFromLinkAsync(IElementHandle el)
    {
        try
        {
            var links = await el.QuerySelectorAllAsync("a");
            foreach (var link in links)
            {
                var href = await link.GetAttributeAsync("href");
                if (string.IsNullOrEmpty(href)) continue;

                var jkMatch = System.Text.RegularExpressions.Regex.Match(href, @"[?&]jk=([a-f0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (jkMatch.Success) return jkMatch.Groups[1].Value;

                var vjMatch = System.Text.RegularExpressions.Regex.Match(href, @"/viewjob\?.*jk=([a-f0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (vjMatch.Success) return vjMatch.Groups[1].Value;
            }
        }
        catch { /* try next selector */ }
        return null;
    }

    private static string _baseUrl(string href)
    {
        if (href.StartsWith("/br")) return "https://br.indeed.com";
        if (href.StartsWith("/uk")) return "https://uk.indeed.com";
        return "https://www.indeed.com";
    }
}
