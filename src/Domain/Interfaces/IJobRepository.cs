using AutoApplicator.Domain.Entities;

namespace AutoApplicator.Domain.Interfaces;

public interface IJobRepository
{
    Task<JobListing?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<JobListing>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<JobListing>> GetByProfileIdAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task AddAsync(JobListing job, CancellationToken cancellationToken = default);
    Task UpdateAsync(JobListing job, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
