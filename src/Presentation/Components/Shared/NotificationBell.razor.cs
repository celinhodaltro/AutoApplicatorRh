using AutoApplicator.Infrastructure.Services;
using Microsoft.AspNetCore.Components;

namespace AutoApplicator.App.Components.Shared;

public partial class NotificationBell : IDisposable
{
    [Inject] private NotificationService NotificationService { get; set; } = default!;

    private bool _showPanel;

    protected override void OnInitialized()
    {
        NotificationService.NotificationsChanged += () => InvokeAsync(StateHasChanged);
    }

    private void TogglePanel() => _showPanel = !_showPanel;
    private void ClosePanel() => _showPanel = false;

    private void MarkRead(Guid id) => NotificationService.MarkAsRead(id);
    private void MarkAllRead() => NotificationService.MarkAllAsRead();
    private void ClearAll()
    {
        NotificationService.Clear();
        _showPanel = false;
    }

    private static string GetIcon(NotificationType type) => type switch
    {
        NotificationType.Success => "âœ…",
        NotificationType.Warning => "âš ï¸",
        NotificationType.Error => "âŒ",
        _ => "â„¹ï¸"
    };

    private static string FormatTime(DateTime timestamp)
    {
        var diff = DateTime.Now - timestamp;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
        return timestamp.ToString("MMM dd HH:mm");
    }

    public void Dispose()
    {
        NotificationService.NotificationsChanged -= () => InvokeAsync(StateHasChanged);
    }
}
