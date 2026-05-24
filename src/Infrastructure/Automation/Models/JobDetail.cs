namespace AutoApplicator.Infrastructure.Automation.Models;

public sealed record JobDetail
{
    public string Description { get; init; } = string.Empty;
    public string? Salary { get; init; }
    public string? PostedDate { get; init; }
    public string? Title { get; init; }
    public string? Company { get; init; }
}
