using AutoApplicator.Application.Interfaces;
using Microsoft.AspNetCore.Components;

namespace AutoApplicator.App.Components.Shared;

public partial class NotificationToast : IDisposable
{
    [Inject] private INotificationService NotificationService { get; set; } = default!;

    private readonly List<ToastItem> _toasts = [];
    private readonly HashSet<Guid> _shownIds = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _timers = [];

    protected override void OnInitialized()
    {
        NotificationService.NotificationsChanged += OnNotificationsChanged;
    }

    private void OnNotificationsChanged()
    {
        InvokeAsync(() =>
        {
            var latest = NotificationService.Notifications.Take(3).ToList();
            foreach (var n in latest)
            {
                if (_shownIds.Add(n.Id))
                {
                    _toasts.Insert(0, new ToastItem
                    {
                        Id = n.Id,
                        Type = n.Type,
                        Title = n.Title,
                        Message = n.Message,
                        ActionLabel = n.ActionLabel,
                        ActionUrl = n.ActionUrl
                    });
                    StartAutoDismiss(n.Id);
                }
            }

            while (_toasts.Count > 3)
            {
                var old = _toasts[^1];
                _toasts.RemoveAt(_toasts.Count - 1);
                _shownIds.Remove(old.Id);
                CancelTimer(old.Id);
            }

            StateHasChanged();
        });
    }

    private void StartAutoDismiss(Guid id)
    {
        var cts = new CancellationTokenSource();
        _timers[id] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000, cts.Token);
                await InvokeAsync(() => RemoveToast(id));
            }
            catch (TaskCanceledException) { }
        });
    }

    private void RemoveToast(Guid id)
    {
        CancelTimer(id);
        _shownIds.Remove(id);
        _toasts.RemoveAll(t => t.Id == id);
        StateHasChanged();
    }

    private void CancelTimer(Guid id)
    {
        if (_timers.TryGetValue(id, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _timers.Remove(id);
        }
    }

    private static string GetIcon(NotificationType type) => type switch
    {
        NotificationType.Success => "check_circle",
        NotificationType.Warning => "warning",
        NotificationType.Error => "cancel",
        _ => "info"
    };

    private static string GetBorderColor(NotificationType type) => type switch
    {
        NotificationType.Success => "#22c55e",
        NotificationType.Warning => "#f97316",
        NotificationType.Error => "#ef4444",
        _ => "#667eea"
    };

    public void Dispose()
    {
        NotificationService.NotificationsChanged -= OnNotificationsChanged;
        foreach (var cts in _timers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _timers.Clear();
    }

    private sealed class ToastItem
    {
        public Guid Id { get; set; }
        public NotificationType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? ActionLabel { get; set; }
        public string? ActionUrl { get; set; }
    }
}
