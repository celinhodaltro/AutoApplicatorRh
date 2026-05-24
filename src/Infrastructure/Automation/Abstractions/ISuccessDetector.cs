using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Abstractions;

public interface ISuccessDetector
{
    Task<bool> DetectAsync(IPage page);
}
