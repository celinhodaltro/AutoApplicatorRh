namespace AutoApplicator.Infrastructure.Services;

public sealed class NotificationService
{
    private readonly List<Notification> _notifications = [];
    private static readonly int MaxNotifications = 50;

    public event Action? NotificationsChanged;

    public IReadOnlyList<Notification> Notifications => _notifications.AsReadOnly();
    public int UnreadCount => _notifications.Count(n => !n.IsRead);

    public void Add(NotificationType type, string title, string message, string? actionLabel = null, string? actionUrl = null)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            Type = type,
            Title = title,
            Message = message,
            Timestamp = DateTime.Now,
            ActionLabel = actionLabel,
            ActionUrl = actionUrl
        };

        _notifications.Insert(0, notification);

        if (_notifications.Count > MaxNotifications)
            _notifications.RemoveRange(MaxNotifications, _notifications.Count - MaxNotifications);

        NotificationsChanged?.Invoke();
    }

    public void MarkAsRead(Guid id)
    {
        var notification = _notifications.FirstOrDefault(n => n.Id == id);
        if (notification is not null)
        {
            notification.IsRead = true;
            NotificationsChanged?.Invoke();
        }
    }

    public void MarkAllAsRead()
    {
        foreach (var n in _notifications)
            n.IsRead = true;
        NotificationsChanged?.Invoke();
    }

    public void Clear()
    {
        _notifications.Clear();
        NotificationsChanged?.Invoke();
    }
}

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class Notification
{
    public Guid Id { get; init; }
    public NotificationType Type { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public bool IsRead { get; set; }
    public string? ActionLabel { get; init; }
    public string? ActionUrl { get; init; }
}
