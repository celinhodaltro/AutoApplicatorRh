using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Models;

namespace AutoApplicator.Infrastructure.Automation.Platforms;

public interface IPlatformAdapter
{
    PlatformType Platform { get; }
    string BaseUrl { get; }

    Task<AuthCheckResult> IsAuthenticatedAsync(IBrowserPage page);
    string BuildSearchUrl(SearchProfile profile, int pageNum = 1);
    Task<List<ExtractedJob>> ExtractListingsAsync(IBrowserPage page);
    Task NavigateToPageAsync(IBrowserPage page, SearchProfile profile, int pageNum);
    Task<JobDetail> ExtractJobDetailsAsync(IBrowserPage page, string url);
}
