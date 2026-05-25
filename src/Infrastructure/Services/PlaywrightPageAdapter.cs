using AutoApplicator.Application.Interfaces;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Services;

public sealed class PlaywrightPageAdapter : IBrowserPage
{
    private readonly IPage _page;

    public PlaywrightPageAdapter(IPage page)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
    }

    internal IPage InnerPage => _page;

    public string Url => _page.Url;
    public bool IsClosed => _page.IsClosed;

    public async Task GoToAsync(string url, int timeoutMs = 30000)
        => await _page.GotoAsync(url, new() { Timeout = timeoutMs });

    public async Task<string> GetContentAsync()
        => await _page.ContentAsync();

    public async Task<string?> QuerySelectorAsync(string selector)
    {
        var element = await _page.QuerySelectorAsync(selector);
        return element is null ? null : await element.InnerTextAsync();
    }

    public async Task<IReadOnlyList<string>> QuerySelectorAllAsync(string selector)
    {
        var elements = await _page.QuerySelectorAllAsync(selector);
        var texts = new List<string>(elements.Count);
        foreach (var el in elements)
            texts.Add(await el.InnerTextAsync());
        return texts.AsReadOnly();
    }

    public async Task ClickAsync(string selector)
        => await _page.ClickAsync(selector);

    public async Task TypeAsync(string selector, string text, int delayMs = 50)
        => await _page.FillAsync(selector, text);

    public async Task SelectOptionAsync(string selector, string value)
        => await _page.SelectOptionAsync(selector, value);

    public async Task WaitForSelectorAsync(string selector, int timeoutMs = 3000)
        => await _page.WaitForSelectorAsync(selector, new() { Timeout = timeoutMs });

    public async Task ScrollToEndAsync()
        => await _page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");

    public async Task CloseAsync()
        => await _page.CloseAsync();
}
