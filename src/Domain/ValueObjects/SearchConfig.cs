namespace AutoApplicator.Domain.ValueObjects;

public record SearchConfig
{
    public string Keywords { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string DatePosted { get; init; } = string.Empty;
    public string ExperienceLevel { get; init; } = string.Empty;
    public string JobType { get; init; } = string.Empty;
    public decimal? SalaryMin { get; init; }
    public bool EasyApplyOnly { get; init; }
    public bool RemoteOnly { get; init; }
}
