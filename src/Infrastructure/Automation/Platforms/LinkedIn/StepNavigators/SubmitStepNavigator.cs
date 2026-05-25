using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.StepNavigators;

public sealed class SubmitStepNavigator : IStepNavigator
{
    private readonly IHumanBehavior _behavior;
    private readonly ILogger<SubmitStepNavigator> _logger;

    public SubmitStepNavigator(IHumanBehavior behavior, ILogger<SubmitStepNavigator> logger)
    {
        _behavior = behavior;
        _logger = logger;
    }

    public async Task<bool> CanNavigateAsync(IPage page)
    {
        var selector = await page.WaitForAnySelectorAsync(LinkedInSelectors.SubmitButton, timeoutMs: 1500);
        return selector is not null;
    }

    public async Task<StepResult> NavigateAsync(IPage page)
    {
        var selector = await page.WaitForAnySelectorAsync(LinkedInSelectors.SubmitButton, timeoutMs: 1500);
        if (selector is null)
        {
            _logger.LogWarning("Submit button not found for navigation");
            return StepResult.Error;
        }

        _logger.LogInformation("Clicking Submit button: {Selector}", selector);

        // SCROLL MODAL FIRST - ensure submit button is visible
        await ScrollModalToBottomAsync(page);

        // Human click with fallback strategy
        try
        {
            await _behavior.HumanClickAsync(page, selector);
        }
        catch
        {
            _logger.LogWarning("Human click failed for {Selector}, trying force click", selector);
            try
            {
                await page.Locator(selector).ClickAsync(new() { Force = true });
            }
            catch
            {
                _logger.LogWarning("Force click failed for {Selector}, trying JavaScript click", selector);
                try
                {
                    await page.EvaluateAsync(@"(sel) => document.querySelector(sel)?.click()", selector);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "All click methods failed for {Selector}", selector);
                }
            }
        }

        await _behavior.DelayAsync(2000, 3000);

        _logger.LogInformation("Submit step navigation successful");
        return StepResult.Submit;
    }

    private static async Task ScrollModalToBottomAsync(IPage page)
    {
        await page.EvaluateAsync(@"() => {
            const modal = document.querySelector(
                '.jobs-easy-apply-modal, .artdeco-modal');
            if (!modal) return;
            const content = modal.querySelector(
                '.jobs-easy-apply-modal__content, .artdeco-modal__content');
            if (content) content.scrollTo(0, content.scrollHeight);
            modal.scrollTo(0, modal.scrollHeight);
        }");
    }
}
