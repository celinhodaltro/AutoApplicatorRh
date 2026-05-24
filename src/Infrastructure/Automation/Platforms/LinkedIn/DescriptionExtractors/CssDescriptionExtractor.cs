using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.DescriptionExtractors;

public sealed class CssDescriptionExtractor : IDescriptionExtractor
{
    private readonly ILogger<CssDescriptionExtractor> _logger;

    public CssDescriptionExtractor(ILogger<CssDescriptionExtractor> logger)
    {
        _logger = logger;
    }

    public int Priority => 100;

    public async Task<string> ExtractAsync(IPage page, string html)
    {
        try
        {
            var text = await SelectorHelper.QuickTextAsync(page, LinkedInSelectors.DetailDescriptionSelectors);

            if (text.Length > 50)
            {
                _logger.LogInformation("CssDescriptionExtractor: Extracted description ({Length} chars) via CSS selectors", text.Length);
                return text;
            }

            _logger.LogDebug("CssDescriptionExtractor: Text too short ({Length} chars), returning empty", text.Length);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CssDescriptionExtractor.ExtractAsync failed");
            return string.Empty;
        }
    }
}
