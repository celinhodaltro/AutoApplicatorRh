using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoApplicator.Domain.Enums;

namespace AutoApplicator.App.Components.Pages.Settings;

public partial class Settings
{
    private bool _globalEasyApply;
    private int _actionDelay = 1000;
    private int _profileCooldown = 30;
    private int _maxApplications = 100;
    private bool _headlessMode = true;

    private int _maxSearchJobs = 25;
    private int _maxApplyJobs = 10;
    private int _maxFullJobs = 20;
    private bool _unlimitedSearch;
    private bool _unlimitedApply;
    private bool _unlimitedFull;

    private bool _logView = true;
    private List<string> _logLines = [];

    protected override void OnInitialized()
    {
        try
        {
            _globalEasyApply = Preferences.Get("global_easy_apply", false);
            _actionDelay = Preferences.Get("action_delay", 1000);
            _profileCooldown = Preferences.Get("profile_cooldown", 30);
            _maxApplications = Preferences.Get("max_applications", 100);
            _headlessMode = Preferences.Get("headless_mode", true);

            _maxSearchJobs = Preferences.Get("max_search_jobs", 25);
            if (_maxSearchJobs >= 9999) { _unlimitedSearch = true; _maxSearchJobs = 25; }
            _maxApplyJobs = Preferences.Get("max_apply_jobs", 10);
            if (_maxApplyJobs >= 9999) { _unlimitedApply = true; _maxApplyJobs = 10; }
            _maxFullJobs = Preferences.Get("max_full_jobs", 20);
            if (_maxFullJobs >= 9999) { _unlimitedFull = true; _maxFullJobs = 20; }
        }
        catch
        {
        }
    }

    private void SaveSettings()
    {
        try
        {
            Preferences.Set("global_easy_apply", _globalEasyApply);
            Preferences.Set("action_delay", _actionDelay);
            Preferences.Set("profile_cooldown", _profileCooldown);
            Preferences.Set("max_applications", _maxApplications);
            Preferences.Set("headless_mode", _headlessMode);

            Preferences.Set("max_search_jobs", _unlimitedSearch ? 9999 : _maxSearchJobs);
            Preferences.Set("max_apply_jobs", _unlimitedApply ? 9999 : _maxApplyJobs);
            Preferences.Set("max_full_jobs", _unlimitedFull ? 9999 : _maxFullJobs);
        }
        catch
        {
        }
    }

    private void OpenPlatformLoginUrl(PlatformType platform)
    {
        var url = platform switch
        {
            PlatformType.LinkedIn => "https://www.linkedin.com/login",
            PlatformType.Indeed => "https://secure.indeed.com/auth",
            PlatformType.Gupy => "https://portal.gupy.io/login",
            _ => "https://www.linkedin.com/login"
        };

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
                Verb = "open"
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
        }
    }

    private async Task OpenPlatformLoginViaPlaywright(PlatformType platform)
    {
        var url = platform switch
        {
            PlatformType.Gupy => "https://portal.gupy.io/login",
            PlatformType.Indeed => "https://secure.indeed.com/auth",
            PlatformType.LinkedIn => "https://www.linkedin.com/login",
            _ => "https://www.linkedin.com/login"
        };

        await BrowserLogin.OpenLoginPageAsync(url);
    }

    private void OpenLogsDirectoryInExplorer()
    {
        var logDir = Path.Combine(FileSystem.AppDataDirectory, "Logs");
        if (Directory.Exists(logDir))
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true,
                Verb = "open"
            };
            System.Diagnostics.Process.Start(psi);
        }
    }

    private static string GetLogLineClass(string line) => line switch
    {
        _ when line.Contains("[ERR]") || line.Contains("[FTL]") => "color:#f44747;",
        _ when line.Contains("[WRN]") => "color:#dcdcaa;",
        _ when line.Contains("[INF]") => "color:#6a9955;",
        _ => ""
    };
}
