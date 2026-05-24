using System.Text.RegularExpressions;
using System.Web;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.DescriptionExtractors;

public sealed class RegexDescriptionExtractor : IDescriptionExtractor
{
    private readonly ILogger<RegexDescriptionExtractor> _logger;

    public RegexDescriptionExtractor(ILogger<RegexDescriptionExtractor> logger)
    {
        _logger = logger;
    }

    public int Priority => 200;

    private static readonly string[] Patterns =
    [
        @"class=""[^""]*?(?:jobs-description__content|jobs-box__html|description__text|show-more-less|jobs-description)[^""]*?"">(.*?)</div>",
        @"<article[^>]*class=""[^""]*jobs-description[^""]*""[^>]*>(.*?)</article>",
        @"<div[^>]*id=""job-details""[^>]*>(.*?)</div>",
        @"<section[^>]*class=""[^""]*description[^""]*""[^>]*>(.*?)</section>"
    ];

    public async Task<string> ExtractAsync(IPage page, string html)
    {
        try
        {
            var content = html;

            if (string.IsNullOrEmpty(content))
            {
                content = await page.ContentAsync();
            }

            foreach (var pattern in Patterns)
            {
                try
                {
                    var match = Regex.Match(content, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var extracted = match.Groups[1].Value;
                        var cleaned = StripHtmlAndDecode(extracted);

                        if (cleaned.Length > 50)
                        {
                            _logger.LogInformation("RegexDescriptionExtractor: Extracted description ({Length} chars) via regex pattern", cleaned.Length);
                            return cleaned;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RegexDescriptionExtractor: Regex pattern failed");
                }
            }

            _logger.LogDebug("RegexDescriptionExtractor: No pattern matched content longer than 50 chars");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RegexDescriptionExtractor.ExtractAsync failed");
            return string.Empty;
        }
    }

    private static string StripHtmlAndDecode(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        // Remove HTML tags
        var stripped = Regex.Replace(html, "<[^>]+>", " ");

        // HTML decode
        stripped = HttpUtility.HtmlDecode(stripped);

        // Normalize whitespace
        stripped = Regex.Replace(stripped, @"\s+", " ");

        return stripped.Trim();
    }
}
