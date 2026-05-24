namespace AutoApplicator.Infrastructure.Automation.Models;

public sealed record AuthCheckResult
{
    public bool IsAuthenticated { get; init; }
    public string LoginUrl { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
