using AutoApplicator.Domain.Enums;

namespace AutoApplicator.Domain.Entities;

public class CollectedQuestion
{
    public Guid Id { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public QuestionFieldType FieldType { get; set; }
    public List<string> Options { get; set; } = [];
    public string Answer { get; set; } = string.Empty;
    public PlatformType? Platform { get; set; }
    public string? JobTitle { get; set; }
    public string? Company { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
