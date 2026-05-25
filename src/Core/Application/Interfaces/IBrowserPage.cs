namespace AutoApplicator.Application.Interfaces;

public interface IBrowserPage
{
    Task GoToAsync(string url, int timeoutMs = 30000);
    Task<string> GetContentAsync();
    Task<string?> QuerySelectorAsync(string selector);
    Task<IReadOnlyList<string>> QuerySelectorAllAsync(string selector);
    Task ClickAsync(string selector);
    Task TypeAsync(string selector, string text, int delayMs = 50);
    Task SelectOptionAsync(string selector, string value);
    Task WaitForSelectorAsync(string selector, int timeoutMs = 3000);
    Task ScrollToEndAsync();
    Task CloseAsync();
    string Url { get; }
    bool IsClosed { get; }
}
