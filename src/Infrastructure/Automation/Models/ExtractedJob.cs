namespace AutoApplicator.Infrastructure.Automation.Models;

public sealed record ExtractedJob
{
    public string ExternalId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public bool EasyApply { get; init; }
}
