namespace AutoApplicator.Infrastructure.Automation.Models;

public sealed record FormField(FormFieldType Type, string Label, string ElementId, bool Required, string? CurrentValue = null, List<string>? Options = null);
