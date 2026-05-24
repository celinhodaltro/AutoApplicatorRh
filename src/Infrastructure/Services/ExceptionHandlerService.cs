using AutoApplicator.Application.Interfaces;
using AutoApplicator.Infrastructure.Services.Exceptions;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Services;

public sealed class ExceptionHandlerService
{
    private readonly NotificationService _notifications;
    private readonly ILogger<ExceptionHandlerService> _logger;

    public ExceptionHandlerService(NotificationService notifications, ILogger<ExceptionHandlerService> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public void Handle(AutomationException ex)
    {
        var type = ex switch
        {
            LoginRequiredException => NotificationType.Error,
            NoApprovedJobsException => NotificationType.Warning,
            NoEnabledProfilesException => NotificationType.Warning,
            QuestionsNeededException => NotificationType.Warning,
            EasyApplyButtonNotFoundException => NotificationType.Warning,
            _ => NotificationType.Error
        };

        _notifications.Add(type, "Automation", ex.UserMessage, ex.ActionLabel, ex.ActionUrl);
        _logger.LogWarning("Business rule: {Message}", ex.UserMessage);
    }

    public bool TryHandle(Exception ex)
    {
        if (ex is AutomationException autoEx)
        {
            Handle(autoEx);
            return true;
        }
        return false;
    }
}
