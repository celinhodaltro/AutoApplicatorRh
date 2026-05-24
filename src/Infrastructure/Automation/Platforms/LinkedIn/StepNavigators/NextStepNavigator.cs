using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.StepNavigators;

public sealed class NextStepNavigator : IStepNavigator
{
    private readonly IHumanBehavior _behavior;
    private readonly ILogger<NextStepNavigator> _logger;

    public NextStepNavigator(IHumanBehavior behavior, ILogger<NextStepNavigator> logger)
    {
        _behavior = behavior;
        _logger = logger;
    }

    public async Task<bool> CanNavigateAsync(IPage page)
    {
        var selector = await page.WaitForAnySelectorAsync(LinkedInSelectors.NextButton, timeoutMs: 2000);
        return selector is not null;
    }

    public async Task<StepResult> NavigateAsync(IPage page)
    {
        var selector = await page.WaitForAnySelectorAsync(LinkedInSelectors.NextButton, timeoutMs: 2000);
        if (selector is null)
        {
            _logger.LogWarning("Next button not found for navigation");
            return StepResult.Error;
        }

        _logger.LogInformation("Clicking Next button: {Selector}", selector);
        await _behavior.HumanClickAsync(page, selector);
        await _behavior.DelayAsync(1500, 2500);

        var error = await SelectorHelper.GetValidationErrorAsync(page);
        if (error is not null)
        {
            _logger.LogWarning("Validation error after next step: {Error}", error);
            return StepResult.Error;
        }

        _logger.LogInformation("Next step navigation successful");
        return StepResult.Next;
    }
}
