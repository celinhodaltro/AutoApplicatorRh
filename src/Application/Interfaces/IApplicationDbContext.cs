using AutoApplicator.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoApplicator.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<JobListing> Jobs { get; }
    DbSet<SearchProfile> SearchProfiles { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<CollectedQuestion> CollectedQuestions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
