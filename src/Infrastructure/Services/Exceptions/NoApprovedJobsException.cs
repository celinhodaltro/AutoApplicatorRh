namespace AutoApplicator.Infrastructure.Services.Exceptions;

public sealed class NoApprovedJobsException : AutomationException
{
    public NoApprovedJobsException()
        : base("No approved jobs to apply. Approve some jobs first.", "View Jobs", "/jobs")
    {
    }
}
