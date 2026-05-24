using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Models;
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
