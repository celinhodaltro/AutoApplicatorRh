using AutoApplicator.Domain.Enums;

namespace AutoApplicator.App.Components.Pages.Settings;

public partial class Settings
{
    private bool _globalEasyApply;
    private int _actionDelay = 1000;
    private int _profileCooldown = 30;
    private int _maxApplications = 100;
    private bool _headlessMode = true;

    private LogType _logType = LogType.Services;
    private bool _loadingLogs;
    private List<string> _logLines = [];
    private string _currentLogPath = "";

    private const string PrefEasyApply = "global_easy_apply";
    private const string PrefActionDelay = "action_delay";
    private const string PrefProfileCooldown = "profile_cooldown";
    private const string PrefMaxApps = "max_applications";
    private const string PrefHeadless = "headless_mode";

    protected override void OnInitialized()
    {
        _globalEasyApply = Preferences.Get(PrefEasyApply, false);
        _actionDelay = Preferences.Get(PrefActionDelay, 1000);
        _profileCooldown = Preferences.Get(PrefProfileCooldown, 30);
        _maxApplications = Preferences.Get(PrefMaxApps, 100);
        _headlessMode = Preferences.Get(PrefHeadless, true);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) await LoadLogContent();
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
        catch { }
    }

    private async Task SwitchLogs(LogType type)
    {
        _logType = type;
        await LoadLogContent();
    }

    private async Task RefreshLogs() => await LoadLogContent();

    private async Task LoadLogContent()
    {
        _loadingLogs = true;

        try
        {
            var subDir = _logType == LogType.Services ? "Services" : "Error";
            var logDir = Path.Combine(FileSystem.AppDataDirectory, "Logs", subDir);

            if (!Directory.Exists(logDir))
            {
                _logLines = [];
                _currentLogPath = logDir;
                return;
            }

            var logFiles = Directory.GetFiles(logDir, "*.txt")
                .OrderByDescending(f => f)
                .ToList();

            if (logFiles.Count == 0)
            {
                _logLines = [];
                _currentLogPath = logDir;
                return;
            }

            var latestFile = logFiles.First();
            _currentLogPath = latestFile;

            var lines = await File.ReadAllLinesAsync(latestFile);
            _logLines = lines.Reverse().Take(200).Reverse().ToList();
        }
        catch (Exception ex)
        {
            _logLines = [$"Error loading logs: {ex.Message}"];
        }
        finally
        {
            _loadingLogs = false;
        }
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

    private static string GetLogLineClass(string line)
    {
        if (line.Contains("[ERR]") || line.Contains("[FTL]")) return "log-line log-error";
        if (line.Contains("[WRN]")) return "log-line log-warning";
        if (line.Contains("[INF]")) return "log-line log-info";
        if (line.Contains("[DBG]") || line.Contains("[VRB]")) return "log-line log-debug";
        return "log-line";
    }

    private enum LogType { Services, Error }
}
