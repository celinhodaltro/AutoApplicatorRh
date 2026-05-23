using AutoApplicator.Domain.Entities;

namespace AutoApplicator.Domain.Interfaces;

public interface IQuestionRepository
{
    Task<CollectedQuestion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<CollectedQuestion>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(CollectedQuestion question, CancellationToken cancellationToken = default);
    Task UpdateAsync(CollectedQuestion question, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
