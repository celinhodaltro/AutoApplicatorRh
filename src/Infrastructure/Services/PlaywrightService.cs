using AutoApplicator.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.Threading;

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
    private readonly SemaphoreSlim _contextSemaphore = new(1, 1);

    private const int DefaultTimeout = 30_000;

    private static readonly string UserDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoApplicator",
        "playwright-data");

    private static readonly string CookiesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoApplicator",
        "playwright-cookies.json");

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
                    "--no-sandbox",
                    "--disable-gpu",
                    "--disable-dev-shm-usage",
                    "--disable-features=IsolateOrigins,site-per-process",
                    "--disable-features=BlockInsecurePrivateNetworkRequests",
                    "--window-size=1920,1080"
                ],
                BypassCSP = true,  // Gupy precisa para carregar scripts
                Locale = "pt-BR",
                TimezoneId = "America/Sao_Paulo",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
            });

            var pages = _context.Pages;
            _page = pages.Count > 0 ? pages[0] : await _context.NewPageAsync();

            await _context.AddInitScriptAsync(GetAntiDetectionScript());

            _page.SetDefaultTimeout(DefaultTimeout);
            _page.SetDefaultNavigationTimeout(DefaultTimeout);

            _initialized = true;

            await RestoreCookiesAsync();

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
            else if (response is not null)
            {
                await SaveCookiesAsync();
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

    public async Task<IBrowserPage> CreateNewPageAsync()
    {
        var page = await CreatePageWithRetryAsync();
        return new PlaywrightPageAdapter(page);
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
        await _contextSemaphore.WaitAsync();
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
        finally
        {
            _contextSemaphore.Release();
        }
    }

    private async Task FullReinitializeAsync()
    {
        _logger.LogInformation("Starting full Playwright reinitialization...");
        await CleanupAsync();
        _initialized = false;
        await InitializeAsync();
        _logger.LogInformation("Playwright reinitialized successfully");
    }

    private async Task<IPage> CreatePageWithRetryAsync(int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await EnsureInitializedAsync();

                if (_context is null)
                    throw new PlaywrightException("Context is null after initialization");

                await _contextSemaphore.WaitAsync();
                try
                {
                    var page = await _context!.NewPageAsync();
                    await page.AddInitScriptAsync(GetAntiDetectionScript());
                    page.SetDefaultTimeout(DefaultTimeout);
                    page.SetDefaultNavigationTimeout(DefaultTimeout);

                    _logger.LogInformation("Page created successfully on attempt {Attempt}", attempt);
                    return page;
                }
                finally
                {
                    _contextSemaphore.Release();
                }
            }
            catch (PlaywrightException ex)
            {
                if (attempt < maxRetries)
                {
                    var delay = 2000 * attempt;
                    _logger.LogWarning(ex,
                        "PlaywrightException on attempt {Attempt}/{MaxRetries}. Reinitializing and retrying in {Delay}ms...",
                        attempt, maxRetries, delay);
                    await FullReinitializeAsync();
                    await Task.Delay(delay);
                }
                else
                {
                    _logger.LogError(ex,
                        "Failed to create page after {MaxRetries} attempts. Please keep the browser window open.",
                        maxRetries);
                    throw;
                }
            }
        }

        throw new InvalidOperationException("Unexpected error in page creation retry logic.");
    }

    public async Task SaveCookiesAsync()
    {
        try
        {
            await _contextSemaphore.WaitAsync();
            try
            {
                var cookies = await _context!.CookiesAsync();
                var json = System.Text.Json.JsonSerializer.Serialize(cookies);
                var dir = Path.GetDirectoryName(CookiesPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                await File.WriteAllTextAsync(CookiesPath, json);
                _logger.LogInformation("Saved {Count} cookies to {Path}", cookies.Count, CookiesPath);
            }
            finally
            {
                _contextSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save cookies to {Path}", CookiesPath);
        }
    }

    private async Task RestoreCookiesAsync()
    {
        try
        {
            if (!File.Exists(CookiesPath))
            {
                _logger.LogDebug("No cookies file found at {Path}", CookiesPath);
                return;
            }

            var json = await File.ReadAllTextAsync(CookiesPath);
            var cookies = System.Text.Json.JsonSerializer.Deserialize<Microsoft.Playwright.Cookie[]>(json);
            if (cookies is not null && cookies.Length > 0)
            {
                await _contextSemaphore.WaitAsync();
                try
                {
                    await _context!.AddCookiesAsync(cookies);
                    _logger.LogInformation("Restored {Count} cookies from {Path}", cookies.Length, CookiesPath);
                }
                finally
                {
                    _contextSemaphore.Release();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore cookies from {Path}", CookiesPath);
        }
    }

    private static string GetAntiDetectionScript() => """
        // Webdriver
        try { Object.defineProperty(navigator, 'webdriver', { get: () => undefined }); } catch(e) {}

        // Plugins
        try { Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] }); } catch(e) {}

        // Languages
        try { Object.defineProperty(navigator, 'languages', { get: () => ['pt-BR', 'pt', 'en-US', 'en'] }); } catch(e) {}

        // Chrome runtime
        try {
            window.chrome = window.chrome || {};
            window.chrome.runtime = window.chrome.runtime || {};
        } catch(e) {}

        // Permissions
        try {
            const originalQuery = window.navigator.permissions.query;
            window.navigator.permissions.query = (parameters) => (
                parameters.name === 'notifications' ?
                    Promise.resolve({ state: Notification.permission }) :
                    originalQuery(parameters)
            );
        } catch(e) {}

        // WebGL vendor (avoid automation detection)
        try {
            const getParameter = WebGLRenderingContext.prototype.getParameter;
            WebGLRenderingContext.prototype.getParameter = function(parameter) {
                if (parameter === 37445) return 'Intel Inc.'; // UNMASKED_VENDOR_WEBGL
                if (parameter === 37446) return 'Intel Iris OpenGL Engine'; // UNMASKED_RENDERER_WEBGL
                return getParameter(parameter);
            };
        } catch(e) {}
    """;

    public async ValueTask DisposeAsync()
    {
        await CleanupAsync();
        GC.SuppressFinalize(this);
    }
}
