namespace AutoApplicator.Application.Interfaces;

public interface IAutomationStateService
{
    bool IsRunning { get; }
    AutomationStatusInfo CurrentStatus { get; }
    event Action? StatusChanged;
    Task StartAsync(AutomationMode mode, bool globalEasyApply = false);
    void Stop();
}

public enum AutomationMode
{
    Search,
    Apply,
    Full
}

public sealed record AutomationStatusInfo(string Message, int Current, int Total)
{
    public AutomationStatusInfo() : this("Idle", 0, 0) { }
    public string Progress => Total > 0 ? $"{Current}/{Total}" : "";
}
