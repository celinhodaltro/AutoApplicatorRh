namespace AutoApplicator.Infrastructure.Services.Exceptions;

public sealed class LoginRequiredException : AutomationException
{
    public LoginRequiredException(string platform, string loginUrl)
        : base($"Login required for {platform}. Please log in first.", "Open Login", loginUrl)
    {
    }
}
