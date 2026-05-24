namespace AutoApplicator.Infrastructure.Automation.Models;

public sealed record ApplyResult(
    bool Success,
    string? ErrorMessage = null,
    bool NeedsManualIntervention = false,
    Dictionary<string, string>? AnswersUsed = null);
