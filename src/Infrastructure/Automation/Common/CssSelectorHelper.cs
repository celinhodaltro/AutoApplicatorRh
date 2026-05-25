namespace AutoApplicator.Infrastructure.Automation.Common;

/// <summary>
/// Helper for escaping CSS selectors, particularly for ElementId values
/// that may contain characters invalid in CSS selector syntax (e.g., parentheses, colons).
/// </summary>
public static class CssSelectorHelper
{
    /// <summary>
    /// Escapes special CSS characters in an ID string so it can be safely used
    /// as a CSS selector (e.g., in "#{id}" selectors).
    /// Escapes: (, ), :, ., #, [, ]
    /// </summary>
    public static string EscapeCssId(string id)
    {
        return id
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace(":", "\\:")
            .Replace(".", "\\.")
            .Replace("#", "\\#")
            .Replace("[", "\\[")
            .Replace("]", "\\]");
    }
}
