using AutoApplicator.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Services;

public sealed class PlaywrightService : IPlaywrightService, IAsyncDisposable
{
    private readonly ILogger<PlaywrightService> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _initialized;
    private readonly object _lock = new();

    private const int DefaultTimeout = 30_000;

    private static readonly string UserDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoApplicator",
        "playwright-data");

    public PlaywrightService(ILogger<PlaywrightService> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        lock (_lock)
        {
            if (_initialized)
                return;
        }

        try
        {
            _logger.LogInformation("Initializing Playwright with persistent context at {UserDataDir}", UserDataDir);

            _playwright = await Microsoft.Playwright.Playwright.CreateAsync();

            _context = await _playwright.Chromium.LaunchPersistentContextAsync(UserDataDir, new()
            {
                Headless = false,
                Args =
                [
                    "--start-maximized",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-features=IsolateOrigins,site-per-process",
                    "--disable-web-security",
                    "--disable-features=BlockInsecurePrivateNetworkRequests"
                ],
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "en-US",
                TimezoneId = "America/New_York",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                BypassCSP = true,
                IgnoreHTTPSErrors = true
            });

            var pages = _context.Pages;
            _page = pages.Count > 0 ? pages[0] : await _context.NewPageAsync();

            await _page.SetViewportSizeAsync(1920, 1080);

            await _context.AddInitScriptAsync("""
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
                Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'] });
                """);

            _page.SetDefaultTimeout(DefaultTimeout);
            _page.SetDefaultNavigationTimeout(DefaultTimeout);

            _initialized = true;
            _logger.LogInformation("Playwright initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Playwright");
            await CleanupAsync();
            throw;
        }
    }

    public async Task NavigateAsync(string url)
    {
        await EnsureInitializedAsync();

        try
        {
            _logger.LogInformation("Navigating to {Url}", url);
            var response = await _page!.GotoAsync(url, new()
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = DefaultTimeout
            });

            if (response is not null && !response.Ok)
            {
                _logger.LogWarning("Navigation to {Url} returned status {Status}", url, response.Status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to {Url}", url);
            throw;
        }
    }

    public async Task<string> GetHtmlAsync()
    {
        await EnsureInitializedAsync();

        try
        {
            return await _page!.ContentAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get page HTML");
            throw;
        }
    }

    public async Task ClickAsync(string selector)
    {
        await EnsureInitializedAsync();

        try
        {
            _logger.LogInformation("Clicking selector '{Selector}'", selector);
            await _page!.ClickAsync(selector, new() { Timeout = DefaultTimeout });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to click selector '{Selector}'", selector);
            throw;
        }
    }

    public async Task TypeAsync(string selector, string text)
    {
        await EnsureInitializedAsync();

        try
        {
            _logger.LogInformation("Typing into '{Selector}'", selector);
            await _page!.FillAsync(selector, text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to type into selector '{Selector}'", selector);
            throw;
        }
    }

    public async Task SelectOptionAsync(string selector, string value)
    {
        await EnsureInitializedAsync();

        try
        {
            _logger.LogInformation("Selecting option '{Value}' in '{Selector}'", value, selector);
            await _page!.SelectOptionAsync(selector, new[] { value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to select option in selector '{Selector}'", selector);
            throw;
        }
    }

    public async Task<bool> IsVisibleAsync(string selector)
    {
        await EnsureInitializedAsync();

        try
        {
            return await _page!.IsVisibleAsync(selector);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check visibility of selector '{Selector}'", selector);
            return false;
        }
    }

    public async Task ScreenshotAsync(string path)
    {
        await EnsureInitializedAsync();

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var timestampedPath = Path.Combine(
                dir ?? ".",
                $"{fileName}_{timestamp}{ext}");

            _logger.LogInformation("Saving screenshot to {Path}", timestampedPath);
            await _page!.ScreenshotAsync(new()
            {
                Path = timestampedPath,
                FullPage = true,
                Type = ScreenshotType.Png
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save screenshot to {Path}", path);
            throw;
        }
    }

    public IPage? GetPage()
    {
        return _page;
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
        {
            await InitializeAsync();
        }
    }

    private async Task CleanupAsync()
    {
        try
        {
            if (_page is not null)
            {
                await _page.CloseAsync();
                _page = null;
            }

            if (_context is not null)
            {
                await _context.CloseAsync();
                _context = null;
            }

            if (_browser is not null)
            {
                await _browser.CloseAsync();
                _browser = null;
            }

            _playwright?.Dispose();
            _playwright = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Playwright cleanup");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupAsync();
        GC.SuppressFinalize(this);
    }
}
