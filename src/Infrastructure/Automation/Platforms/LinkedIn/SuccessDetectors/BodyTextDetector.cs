using AutoApplicator.Infrastructure.Automation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.SuccessDetectors;

public sealed class BodyTextDetector : ISuccessDetector
{
    private readonly ILogger<BodyTextDetector> _logger;

    public BodyTextDetector(ILogger<BodyTextDetector> logger)
    {
        _logger = logger;
    }

    public async Task<bool> DetectAsync(IPage page)
    {
        try
        {
            var bodyText = await page.Locator("body").InnerTextAsync(new() { Timeout = 1000 });

            if (LinkedInSelectors.SuccessPhrases.Any(p =>
                    bodyText.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation("[Success] Success text found in page body");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read body text for success detection");
        }

        return false;
    }
}
