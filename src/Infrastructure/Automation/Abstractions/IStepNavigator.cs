using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Abstractions;

public interface IStepNavigator
{
    Task<bool> CanNavigateAsync(IPage page);
    Task<StepResult> NavigateAsync(IPage page);
}
