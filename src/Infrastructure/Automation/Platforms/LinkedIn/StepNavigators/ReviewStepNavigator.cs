using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.StepNavigators;

public sealed class ReviewStepNavigator : IStepNavigator
{
    private readonly IHumanBehavior _behavior;
    private readonly ILogger<ReviewStepNavigator> _logger;

    public ReviewStepNavigator(IHumanBehavior behavior, ILogger<ReviewStepNavigator> logger)
    {
        _behavior = behavior;
        _logger = logger;
    }

    public async Task<bool> CanNavigateAsync(IPage page)
    {
        var selector = await page.WaitForAnySelectorAsync(LinkedInSelectors.ReviewButton, timeoutMs: 1500);
        return selector is not null;
    }

    public async Task<StepResult> NavigateAsync(IPage page)
    {
        var selector = await page.WaitForAnySelectorAsync(LinkedInSelectors.ReviewButton, timeoutMs: 1500);
        if (selector is null)
        {
            _logger.LogWarning("Review button not found for navigation");
            return StepResult.Error;
        }

        _logger.LogInformation("Clicking Review button: {Selector}", selector);
        await _behavior.HumanClickAsync(page, selector);
        await _behavior.DelayAsync(1500, 2500);

        _logger.LogInformation("Review step navigation successful");
        return StepResult.Review;
    }
}
