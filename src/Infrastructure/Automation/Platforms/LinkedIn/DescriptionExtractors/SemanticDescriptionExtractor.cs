using System.Text.RegularExpressions;
using System.Web;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.DescriptionExtractors;

public sealed class SemanticDescriptionExtractor : IDescriptionExtractor
{
    private readonly ILogger<SemanticDescriptionExtractor> _logger;

    public SemanticDescriptionExtractor(ILogger<SemanticDescriptionExtractor> logger)
    {
        _logger = logger;
    }

    public int Priority => 300;

    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex DescriptionKeywordRegex = new(
        @"(About the job|Qualifications|Responsibilities|Description|Job Description|Job details)[:\s]+(.{100,}?)(?=(Qualifications|Requirements|Skills|Experience|Education|About this role|$))",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<string> ExtractAsync(IPage page, string html)
    {
        try
        {
            var content = html;

            if (string.IsNullOrEmpty(content))
            {
                content = await page.ContentAsync();
            }

            // Remove HTML tags, replace with spaces
            var plainText = HtmlTagRegex.Replace(content, " ");

            // Normalize whitespace
            plainText = WhitespaceRegex.Replace(plainText, " ").Trim();

            // Try to find description by keywords
            var match = DescriptionKeywordRegex.Match(plainText);
            if (match.Success)
            {
                var extracted = match.Groups[2].Value;
                extracted = HttpUtility.HtmlDecode(extracted);
                extracted = WhitespaceRegex.Replace(extracted, " ").Trim();

                if (extracted.Length > 50)
                {
                    _logger.LogInformation("SemanticDescriptionExtractor: Extracted description ({Length} chars) via keyword pattern", extracted.Length);
                    return extracted;
                }
            }

            _logger.LogDebug("SemanticDescriptionExtractor: No keyword match found with content longer than 50 chars");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SemanticDescriptionExtractor.ExtractAsync failed");
            return string.Empty;
        }
    }
}
