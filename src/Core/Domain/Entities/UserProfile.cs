namespace AutoApplicator.Domain.Entities;

public class UserProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Skills { get; set; } = [];
    public List<string> Experience { get; set; } = [];
    public Dictionary<string, string> Preferences { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
