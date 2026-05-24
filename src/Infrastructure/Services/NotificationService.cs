using AutoApplicator.Application.Interfaces;

namespace AutoApplicator.Infrastructure.Services;

public sealed class NotificationService : INotificationService
{
    private readonly List<NotificationItem> _notifications = [];
    private static readonly int MaxNotifications = 50;

    public event Action? NotificationsChanged;

    IReadOnlyList<NotificationItem> INotificationService.Notifications => _notifications.AsReadOnly();
    public int UnreadCount => _notifications.Count(n => !n.IsRead);

    public void Add(NotificationType type, string title, string message, string? actionLabel = null, string? actionUrl = null)
    {
        var notification = new NotificationItem(
            Guid.NewGuid(),
            type,
            title,
            message,
            DateTime.Now,
            false,
            actionLabel,
            actionUrl);

        _notifications.Insert(0, notification);

        if (_notifications.Count > MaxNotifications)
            _notifications.RemoveRange(MaxNotifications, _notifications.Count - MaxNotifications);

        NotificationsChanged?.Invoke();
    }

    public void MarkAsRead(Guid id)
    {
        var index = _notifications.FindIndex(n => n.Id == id);
        if (index >= 0)
        {
            var old = _notifications[index];
            _notifications[index] = old with { IsRead = true };
            NotificationsChanged?.Invoke();
        }
    }

    public void MarkAllAsRead()
    {
        for (var i = 0; i < _notifications.Count; i++)
            _notifications[i] = _notifications[i] with { IsRead = true };
        NotificationsChanged?.Invoke();
    }

    public void Clear()
    {
        _notifications.Clear();
        NotificationsChanged?.Invoke();
    }
}
