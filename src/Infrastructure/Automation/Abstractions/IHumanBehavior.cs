using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Abstractions;

public interface IHumanBehavior
{
    Task DelayAsync(int minMs = 500, int maxMs = 1500);
    Task HumanClickAsync(IPage page, string selector);
    Task HumanTypeAsync(IPage page, string selector, string text);
    Task ScrollListAsync(IPage page, string listSelector);
}
