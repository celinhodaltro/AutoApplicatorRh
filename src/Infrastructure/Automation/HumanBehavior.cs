using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation;

public sealed class HumanBehavior
{
    private readonly ILogger<HumanBehavior> _logger;
    private static readonly Random Rng = new();

    public HumanBehavior(ILogger<HumanBehavior>? logger = null)
    {
        _logger = logger!;
    }

    public Task DelayAsync(int minMs = 500, int maxMs = 1500)
    {
        var delay = Rng.Next(minMs, maxMs + 1);
        return Task.Delay(delay);
    }

    public async Task HumanClickAsync(IPage page, string selector)
    {
        try
        {
            var box = await page.Locator(selector).First.BoundingBoxAsync();
            if (box is not null)
            {
                var offsetX = (float)(box.Width * (0.3 + Rng.NextDouble() * 0.4));
                var offsetY = (float)(box.Height * (0.3 + Rng.NextDouble() * 0.4));
                await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
                await DelayAsync(100, 300);
                await page.Mouse.MoveAsync(box.X + offsetX, box.Y + offsetY);
                await DelayAsync(50, 150);
                await page.Mouse.ClickAsync(box.X + offsetX, box.Y + offsetY);
            }
            else
            {
                await page.Locator(selector).First.ClickAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Human click failed for {Selector}, trying force click", selector);
            await page.Locator(selector).First.ClickAsync(new() { Force = true });
        }
    }

    public async Task HumanTypeAsync(IPage page, string selector, string text)
    {
        var locator = page.Locator(selector).First;
        await locator.ClickAsync();
        await DelayAsync(100, 300);
        await locator.FillAsync(string.Empty);
        await DelayAsync(200, 400);

        foreach (var ch in text)
        {
            await locator.PressAsync(ch.ToString());
            await Task.Delay(Rng.Next(30, 80));
        }
    }

    public async Task ScrollListAsync(IPage page, string listSelector)
    {
        try
        {
            var visible = await page.Locator(listSelector).First.IsVisibleAsync();
            if (!visible) return;

            var metrics = await page.EvaluateAsync<ScrollMetrics>(@"(sel) => {
                const list = document.querySelector(sel);
                if (!list) return { scrollHeight: 0, clientHeight: 0 };
                return { scrollHeight: list.scrollHeight, clientHeight: list.clientHeight };
            }", listSelector);

            if (metrics.ScrollHeight <= metrics.ClientHeight) return;

            var step = Math.Max(200, metrics.ClientHeight / 3);
            var pos = 0;
            var scrollHeight = metrics.ScrollHeight;

            while (pos < scrollHeight)
            {
                pos = Math.Min(pos + step, scrollHeight);
                await page.EvaluateAsync(@"(args) => {
                    const list = document.querySelector(args.sel);
                    if (list) list.scrollTop = args.pos;
                }", new { sel = listSelector, pos });
                await DelayAsync(300, 600);

                var newHeight = await page.EvaluateAsync<int>(@"(sel) => {
                    const list = document.querySelector(sel);
                    return list ? list.scrollHeight : 0;
                }", listSelector);

                if (newHeight > scrollHeight) scrollHeight = newHeight;
            }

            await DelayAsync(500, 1000);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Scroll failed for {Selector}", listSelector);
        }
    }

    private sealed record ScrollMetrics
    {
        public int ScrollHeight { get; init; }
        public int ClientHeight { get; init; }
    }
}
