using AutoApplicator.Domain.Enums;

namespace AutoApplicator.Domain.Entities;

public class SearchProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public PlatformType Platform { get; set; }
    public List<string> Keywords { get; set; } = [];
    public List<string> Location { get; set; } = [];
    public string DatePosted { get; set; } = string.Empty;
    public List<string> ExperienceLevel { get; set; } = [];
    public List<string> JobTypes { get; set; } = [];
    public decimal? SalaryMin { get; set; }
    public bool EasyApplyOnly { get; set; }
    public bool RemoteOnly { get; set; }
    public List<string> ExcludeTerms { get; set; } = [];
    public string ResumeFile { get; set; } = string.Empty;
    public string? CoverLetterTemplate { get; set; }
    public Dictionary<string, string> DefaultAnswers { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
