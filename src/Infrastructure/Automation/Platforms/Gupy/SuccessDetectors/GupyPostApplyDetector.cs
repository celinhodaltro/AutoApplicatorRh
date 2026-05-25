using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Gupy.SuccessDetectors;

public sealed class GupyPostApplyDetector : ISuccessDetector
{
    public PlatformType Platform => PlatformType.Gupy;

    private readonly ILogger<GupyPostApplyDetector> _logger;

    public GupyPostApplyDetector(ILogger<GupyPostApplyDetector> logger)
    {
        _logger = logger;
    }

    public async Task<bool> DetectAsync(IPage page)
    {
        try
        {
            // Check for post-apply dialog
            var modal = page.Locator(GupySelectors.PostApplyModal).First;
            await modal.WaitForAsync(new() { Timeout = 3000 });
            var isVisible = await modal.IsVisibleAsync();

            if (isVisible)
            {
                _logger.LogInformation("[Success] Gupy post-apply modal detected");

                // Try to click "Finalizar candidatura"
                try
                {
                    var finalizeBtn = modal.Locator(GupySelectors.FinalizeButton).First;
                    await finalizeBtn.ClickAsync(new() { Timeout = 2000 });
                    _logger.LogInformation("Clicked 'Finalizar candidatura' in post-apply modal");
                }
                catch
                {
                    _logger.LogWarning("Finalizar button not found, trying close button");
                    try
                    {
                        var closeBtn = modal.Locator(GupySelectors.CloseModalButton).First;
                        await closeBtn.ClickAsync(new() { Timeout = 2000 });
                    }
                    catch { /* ignore */ }
                }

                await Task.Delay(1000);
                return true;
            }
        }
        catch
        {
            _logger.LogDebug("Gupy post-apply modal not found");
        }

        return false;
    }
}
