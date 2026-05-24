using AutoApplicator.Infrastructure.Automation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.SuccessDetectors;

public sealed class ContainerTextDetector : ISuccessDetector
{
    private readonly ILogger<ContainerTextDetector> _logger;

    public ContainerTextDetector(ILogger<ContainerTextDetector> logger)
    {
        _logger = logger;
    }

    public async Task<bool> DetectAsync(IPage page)
    {
        var containers = new[] { ".jobs-easy-apply-modal", ".artdeco-modal", ".jpac-modal" };

        foreach (var container in containers)
        {
            try
            {
                var text = await page.Locator(container).First.InnerTextAsync(new() { Timeout = 1000 });

                if (LinkedInSelectors.SuccessPhrases.Any(p =>
                        text.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("[Success] Confirmation text found in: {Container}", container);
                    return true;
                }
            }
            catch
            {
                // Container not found or not visible, try next
            }
        }

        return false;
    }
}
