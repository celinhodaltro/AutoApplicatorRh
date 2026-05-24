namespace AutoApplicator.Infrastructure.Services.Exceptions;

public abstract class AutomationException : Exception
{
    public string UserMessage { get; }
    public string? ActionLabel { get; }
    public string? ActionUrl { get; }

    protected AutomationException(string userMessage, string? actionLabel = null, string? actionUrl = null)
        : base(userMessage)
    {
        UserMessage = userMessage;
        ActionLabel = actionLabel;
        ActionUrl = actionUrl;
    }
}
