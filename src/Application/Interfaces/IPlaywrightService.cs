namespace AutoApplicator.Application.Interfaces;

public interface IPlaywrightService
{
    Task InitializeAsync();
    Task NavigateAsync(string url);
    Task<string> GetHtmlAsync();
    Task ClickAsync(string selector);
    Task TypeAsync(string selector, string text);
    Task SelectOptionAsync(string selector, string value);
    Task<bool> IsVisibleAsync(string selector);
    Task ScreenshotAsync(string path);
}
