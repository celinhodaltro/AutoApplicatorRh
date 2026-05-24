using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Abstractions;

public interface IDescriptionExtractor
{
    int Priority { get; }
    Task<string> ExtractAsync(IPage page, string html);
}
