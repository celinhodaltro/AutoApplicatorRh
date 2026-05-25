using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.SuccessDetectors;

public sealed class PostApplyModalDetector : ISuccessDetector
{
    public PlatformType Platform => PlatformType.LinkedIn;

    private readonly IHumanBehavior _behavior;
    private readonly ILogger<PostApplyModalDetector> _logger;

    public PostApplyModalDetector(IHumanBehavior behavior, ILogger<PostApplyModalDetector> logger)
    {
        _behavior = behavior;
        _logger = logger;
    }

    public async Task<bool> DetectAsync(IPage page)
    {
        try
        {
            var postApplyModal = page.Locator("div[aria-labelledby=\"post-apply-modal\"], .jpac-modal-header").First;
            await postApplyModal.WaitForAsync(new() { Timeout = 3000 });
            var isVisible = await postApplyModal.IsVisibleAsync();

            if (isVisible)
            {
                _logger.LogInformation("[Success] Post-apply confirmation modal detected");

                // Try the primary "Concluído" / "Done" button
                try
                {
                    var doneBtn = page.Locator("#post-apply-modal + .artdeco-modal__actionbar button.artdeco-button--primary").First;
                    await doneBtn.ClickAsync(new() { Timeout = 2000 });
                    _logger.LogInformation("Clicked primary Done button in post-apply modal");
                }
                catch
                {
                    _logger.LogWarning("Primary Done button not found, trying fallback selectors");
                    var doneSelector = await page.WaitForAnySelectorAsync(LinkedInSelectors.DoneSelectors, 2000);
                    if (doneSelector is not null)
                    {
                        await _behavior.HumanClickAsync(page, doneSelector);
                        _logger.LogInformation("Clicked Done button via fallback selector");
                    }
                }

                await _behavior.DelayAsync(500, 1000);
                return true;
            }
        }
        catch
        {
            _logger.LogDebug("Post-apply modal not found");
        }

        return false;
    }
}
