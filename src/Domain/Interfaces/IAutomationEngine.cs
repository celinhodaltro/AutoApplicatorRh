using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.ValueObjects;

namespace AutoApplicator.Domain.Interfaces;

public interface IAutomationEngine
{
    Task<IReadOnlyList<JobListing>> SearchJobsAsync(SearchProfile profile, CancellationToken cancellationToken = default);
    Task<JobListing?> AnalyzeJobAsync(JobListing job, UserProfile user, CancellationToken cancellationToken = default);
    Task<JobListing?> ApplyToJobAsync(JobListing job, SearchProfile profile, UserProfile user, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectedQuestion>> CollectQuestionsAsync(JobListing job, CancellationToken cancellationToken = default);
}
