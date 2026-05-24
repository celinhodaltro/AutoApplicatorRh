using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.Text;
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

            var jobLinks = await page.QuerySelectorAllAsync("a[href*=\"/job/\"]");
            var processedUrls = new HashSet<string>();

            foreach (var link in jobLinks)
            {
                try
                {
                    var href = await link.GetAttributeAsync("href");
                    if (string.IsNullOrEmpty(href)) continue;

                    var url = href.StartsWith("http") ? href : $"https://portal.gupy.io{href}";
                    if (!processedUrls.Add(url)) continue;

                    // Extract title from h3 inside the link
                    var titleEl = await link.QuerySelectorAsync("h3");
                    var title = titleEl is not null ? (await titleEl.InnerTextAsync())?.Trim() : string.Empty;
                    if (string.IsNullOrEmpty(title)) continue;

                    // Extract company from aria-label: "Ir para vaga {title} da empresa {company}"
                    var ariaLabel = await link.GetAttributeAsync("aria-label");
                    string? company = null;
                    if (!string.IsNullOrEmpty(ariaLabel))
                    {
                        var match = Regex.Match(ariaLabel, @"da empresa\s+(.+?)$", RegexOptions.IgnoreCase);
                        if (match.Success)
                            company = match.Groups[1].Value.Trim();
                    }

                    // Extract location from [data-testid="job-location"]
                    var locationEl = await page.QuerySelectorAsync("[data-testid=\"job-location\"]");
                    var location = locationEl is not null ? (await locationEl.InnerTextAsync())?.Trim() : string.Empty;

                    // Generate ExternalId from URL (base64 encoded path)
                    var externalId = GenerateExternalId(href);

                    cards.Add(new ExtractedJob
                    {
                        ExternalId = externalId,
                        Title = title,
                        Company = company ?? string.Empty,
                        Location = location ?? string.Empty,
                        Url = url,
                        EasyApply = false
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
            var titleEl = await page.QuerySelectorAsync("h1");
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

    private static string GenerateExternalId(string href)
    {
        // Extract job ID from URL path /job/{id}
        var match = Regex.Match(href, @"/job/([^/?]+)", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value;

        // Fallback: base64 encode the href
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(href))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
