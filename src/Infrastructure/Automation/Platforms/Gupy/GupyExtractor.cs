using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Gupy;

public sealed class GupyExtractor
{
    private readonly ILogger _logger;
    private readonly IHumanBehavior _behavior;

    public GupyExtractor(ILogger<GupyExtractor> logger, IHumanBehavior behavior)
    {
        _logger = logger;
        _behavior = behavior;
    }

    public async Task<List<ExtractedJob>> ExtractJobCardsAsync(IPage page)
    {
        var cards = new List<ExtractedJob>();

        try
        {
            await _behavior.DelayAsync(2000, 3000);

            var jobLinks = await page.QuerySelectorAllAsync(GupySelectors.JobCardSelector);
            var processedUrls = new HashSet<string>();

            foreach (var link in jobLinks)
            {
                try
                {
                    var href = await link.GetAttributeAsync("href");
                    if (string.IsNullOrEmpty(href)) continue;

                    var url = href.StartsWith("http") ? href : $"https://portal.gupy.io{href}";
                    if (!processedUrls.Add(url)) continue;

                    // Extract data from aria-label:
                    // "Ir para vaga {title} da empresa {company} na cidade {city}..."
                    var ariaLabel = await link.GetAttributeAsync("aria-label");
                    if (string.IsNullOrEmpty(ariaLabel)) continue;

                    var title = ExtractFromLabel(ariaLabel, "Ir para vaga ", " da empresa ");
                    if (string.IsNullOrEmpty(title)) continue;

                    var company = ExtractFromLabel(ariaLabel, " da empresa ", " na cidade ")
                                  ?? ExtractFromLabel(ariaLabel, " da empresa ", " em ")
                                  ?? ExtractFromLabel(ariaLabel, " da empresa ", " na ")
                                  ?? ExtractFromLabel(ariaLabel, " da empresa ", null);

                    var location = ExtractFromLabel(ariaLabel, " na cidade ", null)
                                   ?? ExtractFromLabel(ariaLabel, " em ", null);

                    // Fallback: extract location from [data-testid="job-location"] inside the card
                    if (string.IsNullOrEmpty(location) || location.Length > 60)
                    {
                        var locationEl = await link.QuerySelectorAsync(GupySelectors.CardLocationSelector)
                                         ?? await page.QuerySelectorAsync(GupySelectors.CardLocationSelector);
                        if (locationEl is not null)
                            location = (await locationEl.InnerTextAsync())?.Trim();
                    }

                    // Extract ExternalId from URL (e.g., /job/12345 -> 12345)
                    var externalId = ExtractJobId(href);

                    _logger.LogDebug("Extracted Gupy job: {Title} at {Company} in {Location}", title, company, location);

                    cards.Add(new ExtractedJob
                    {
                        ExternalId = externalId,
                        Title = title,
                        Company = company ?? string.Empty,
                        Location = location ?? string.Empty,
                        Url = url,
                        EasyApply = true // All Gupy jobs are Easy Apply
                    });
                }
                catch
                {
                    // try next card
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract Gupy job cards");
        }

        _logger.LogInformation("Extracted {Count} Gupy job cards", cards.Count);
        return cards;
    }

    public async Task<JobDetail> ExtractJobDetailsAsync(IPage page)
    {
        await _behavior.DelayAsync(500, 1000);

        try
        {
            // Extract title from h1
            string? title = null;
            var titleEl = await page.QuerySelectorAsync(GupySelectors.JobTitleSelector);
            if (titleEl is not null)
                title = (await titleEl.InnerTextAsync())?.Trim();

            // Extract description from [data-testid="text-section"] (multiple, concatenate)
            var descriptionParts = new List<string>();
            var textSections = await page.QuerySelectorAllAsync("[data-testid=\"text-section\"]");
            foreach (var section in textSections)
            {
                var text = (await section.InnerTextAsync())?.Trim();
                if (!string.IsNullOrEmpty(text))
                    descriptionParts.Add(text);
            }
            var description = string.Join("\n\n", descriptionParts);

            return new JobDetail
            {
                Title = title,
                Description = description
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract Gupy job details");
            return new JobDetail { Description = string.Empty };
        }
    }

    /// <summary>
    /// Extracts text between two markers. If <paramref name="until"/> is null, extracts from <paramref name="after"/> to end.
    /// </summary>
    private static string? ExtractFromLabel(string label, string after, string? until)
    {
        var startIdx = label.IndexOf(after, StringComparison.Ordinal);
        if (startIdx < 0) return null;

        startIdx += after.Length;
        if (startIdx >= label.Length) return null;

        if (until is null)
            return label[startIdx..].Trim().TrimEnd('.').Trim();

        var endIdx = label.IndexOf(until, startIdx, StringComparison.Ordinal);
        if (endIdx < 0) return null;

        return label[startIdx..endIdx].Trim().TrimEnd('.').Trim();
    }

    /// <summary>
    /// Extracts the numeric job ID from a Gupy URL path (e.g., /job/12345 -> "12345").
    /// Falls back to a base64-encoded hash of the full href.
    /// </summary>
    private static string ExtractJobId(string href)
    {
        var match = Regex.Match(href, @"/job/(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value;

        // Fallback: use the last path segment
        match = Regex.Match(href, @"/job/([^/?]+)", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value;

        // Last resort: base64 encode the href
        return Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(href))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
