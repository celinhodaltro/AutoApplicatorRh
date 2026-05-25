using AutoApplicator.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Services;

public sealed class BrowserLoginService
{
    private readonly PlaywrightService _playwright;
    private readonly ILogger<BrowserLoginService> _logger;
    private IBrowserPage? _loginPage;

    public BrowserLoginService(PlaywrightService playwright, ILogger<BrowserLoginService> logger)
    {
        _playwright = playwright;
        _logger = logger;
    }

    public async Task OpenLoginPageAsync(string url)
    {
        _loginPage = await _playwright.CreateNewPageAsync();
        await _loginPage.GoToAsync(url);
        _logger.LogInformation("Opened login page in new tab: {Url}", url);
        
        // Save cookies periodically while waiting for login
        try
        {
            var lastSave = DateTime.UtcNow;
            while (!_loginPage.IsClosed)
            {
                await Task.Delay(1000);
                
                // Save cookies every 3 seconds while login page is open
                if ((DateTime.UtcNow - lastSave).TotalSeconds >= 3)
                {
                    try { await _playwright.SaveCookiesAsync(); } catch { /* context may be closed */ }
                    lastSave = DateTime.UtcNow;
                }
            }
        }
        catch { /* page was closed */ }
        
        // Final save attempt (may fail if context closed)
        try { await _playwright.SaveCookiesAsync(); } catch { /* context closed, cookies already saved above */ }
        _logger.LogInformation("Login tab closed. Session cookies should be persisted.");
    }

    public void Dispose()
    {
        // Don't close the login page - let the user close it manually
        _loginPage = null;
    }
}
