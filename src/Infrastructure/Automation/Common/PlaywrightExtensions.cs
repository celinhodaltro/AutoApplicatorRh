using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Common;

public static class PlaywrightExtensions
{
    public static async Task<string?> WaitForAnySelectorAsync(this IPage page, string[] selectors, int timeoutMs = 2000)
    {
        foreach (var sel in selectors)
        {
            try
            {
                var locator = page.Locator(sel).First;
                await locator.WaitForAsync(new() { Timeout = timeoutMs });
                if (await locator.IsVisibleAsync())
                    return sel;
            }
            catch { /* try next selector */ }
        }
        return null;
    }

    public static async Task<ILocator?> WaitForAnyLocatorAsync(this IPage page, string[] selectors, int timeoutMs = 2000)
    {
        foreach (var sel in selectors)
        {
            try
            {
                var locator = page.Locator(sel).First;
                await locator.WaitForAsync(new() { Timeout = timeoutMs });
                if (await locator.IsVisibleAsync())
                    return locator;
            }
            catch { /* try next selector */ }
        }
        return null;
    }
}
