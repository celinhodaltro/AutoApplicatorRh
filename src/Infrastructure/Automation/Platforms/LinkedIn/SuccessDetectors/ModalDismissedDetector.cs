using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.SuccessDetectors;

public sealed class ModalDismissedDetector : ISuccessDetector
{
    public PlatformType Platform => PlatformType.LinkedIn;

    private readonly ILogger<ModalDismissedDetector> _logger;

    public ModalDismissedDetector(ILogger<ModalDismissedDetector> logger)
    {
        _logger = logger;
    }

    public async Task<bool> DetectAsync(IPage page)
    {
        try
        {
            foreach (var sel in LinkedInSelectors.ModalContainer)
            {
                var locator = page.Locator(sel).First;
                var visible = await locator.IsVisibleAsync();
                if (visible) return false; // modal still open
            }

            _logger.LogInformation("[Success] Modal dismissed, job page visible");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ModalDismissedDetector failed");
            return false;
        }
    }
}
