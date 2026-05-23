using AutoApplicator.Domain.Enums;

namespace AutoApplicator.Domain.Entities;

public class JobListing
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public PlatformType Platform { get; set; }
    public Guid ProfileId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? Salary { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime PostedDate { get; set; }
    public bool EasyApply { get; set; }
    public int? MatchScore { get; set; }
    public string? MatchReasoning { get; set; }
    public string? Summary { get; set; }
    public List<string> RedFlags { get; set; } = [];
    public List<string> Highlights { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public JobStatus Status { get; set; }
    public string? UserNotes { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public Dictionary<string, string>? ApplicationAnswers { get; set; }
    public string? CoverLetterUsed { get; set; }
    public string? ResumeUsed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
