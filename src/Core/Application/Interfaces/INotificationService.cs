namespace AutoApplicator.Application.Interfaces;

public interface INotificationService
{
    int UnreadCount { get; }
    IReadOnlyList<NotificationItem> Notifications { get; }
    event Action? NotificationsChanged;
    void Add(NotificationType type, string title, string message, string? actionLabel = null, string? actionUrl = null);
    void MarkAsRead(Guid id);
    void MarkAllAsRead();
    void Clear();
}

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record NotificationItem(
    Guid Id,
    NotificationType Type,
    string Title,
    string Message,
    DateTime Timestamp,
    bool IsRead,
    string? ActionLabel,
    string? ActionUrl);
