using System.Text.RegularExpressions;
using System.Web;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.DescriptionExtractors;

public sealed class FallbackDescriptionExtractor : IDescriptionExtractor
{
    private readonly ILogger<FallbackDescriptionExtractor> _logger;

    public FallbackDescriptionExtractor(ILogger<FallbackDescriptionExtractor> logger)
    {
        _logger = logger;
    }

    public int Priority => 400;

    private static readonly Regex DivSplitRegex = new(@"<div[^>]*>", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly string[] SkipContaining =
    [
        "nav", "header", "footer"
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

            // Split by <div...> tags
            var blocks = DivSplitRegex.Split(content);

            var candidates = new List<string>();

            foreach (var block in blocks)
            {
                try
                {
                    // Remove any remaining HTML tags
                    var cleaned = HtmlTagRegex.Replace(block, " ");

                    // HTML decode
                    cleaned = HttpUtility.HtmlDecode(cleaned);

                    // Normalize whitespace
                    cleaned = WhitespaceRegex.Replace(cleaned, " ").Trim();

                    if (cleaned.Length <= 100) continue;

                    // Skip blocks containing nav, header, footer
                    var shouldSkip = false;
                    foreach (var skip in SkipContaining)
                    {
                        if (cleaned.Contains(skip, StringComparison.OrdinalIgnoreCase))
                        {
                            shouldSkip = true;
                            break;
                        }
                    }

                    if (!shouldSkip)
                    {
                        candidates.Add(cleaned);
                    }
                }
                catch
                {
                    // Ignore malformed blocks
                }
            }

            if (candidates.Count > 0)
            {
                // Pick the longest block
                var best = candidates.OrderByDescending(b => b.Length).First();

                _logger.LogInformation("FallbackDescriptionExtractor: Extracted description ({Length} chars) via fallback (longest div block)", best.Length);
                return best;
            }

            _logger.LogDebug("FallbackDescriptionExtractor: No suitable block found");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FallbackDescriptionExtractor.ExtractAsync failed");
            return string.Empty;
        }
    }
}
