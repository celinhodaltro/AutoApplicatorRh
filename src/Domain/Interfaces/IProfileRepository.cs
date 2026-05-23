using AutoApplicator.Domain.Entities;

namespace AutoApplicator.Domain.Interfaces;

public interface IProfileRepository
{
    Task<SearchProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<SearchProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(SearchProfile profile, CancellationToken cancellationToken = default);
    Task UpdateAsync(SearchProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
