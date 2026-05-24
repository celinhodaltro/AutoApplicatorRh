using AutoApplicator.Domain.Enums;

namespace AutoApplicator.App.Components.Pages.Settings;

public partial class Settings
{
    private bool _globalEasyApply;
    private int _actionDelay = 1000;
    private int _profileCooldown = 30;
    private int _maxApplications = 100;
    private bool _headlessMode = true;

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
        }
        catch
        {
            // Preferences not available (e.g., during testing)
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
        }
        catch { /* Preferences save failed */ }
    }

    private void OpenLogin(PlatformType platform)
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
        catch { /* Failed to open browser */ }
    }

    private void OpenLogsFolder()
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

    private string GetLogLineClass(string line)
    {
        if (line.Contains("[ERR]") || line.Contains("[FTL]")) return "color:#f44747;";
        if (line.Contains("[WRN]")) return "color:#dcdcaa;";
        if (line.Contains("[INF]")) return "color:#6a9955;";
        return "";
    }
}
