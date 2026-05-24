using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms;

public interface IPlatformAdapter
{
    PlatformType Platform { get; }
    string BaseUrl { get; }

    Task<AuthCheckResult> IsAuthenticatedAsync(IPage page);
    string BuildSearchUrl(SearchProfile profile, int pageNum = 1);
    Task<List<ExtractedJob>> ExtractListingsAsync(IPage page);
    Task<bool> HasNextPageAsync(IPage page);
    Task GoToNextPageAsync(IPage page);
    Task NavigateToPageAsync(IPage page, SearchProfile profile, int pageNum);
    Task<JobDetail> ExtractJobDetailsAsync(IPage page, string url);
}

public sealed record AuthCheckResult
{
    public bool IsAuthenticated { get; init; }
    public string LoginUrl { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record ExtractedJob
{
    public string ExternalId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public bool EasyApply { get; init; }
}

public sealed record JobDetail
{
    public string Description { get; init; } = string.Empty;
    public string? Salary { get; init; }
    public string? PostedDate { get; init; }
    public string? Title { get; init; }
    public string? Company { get; init; }
}
