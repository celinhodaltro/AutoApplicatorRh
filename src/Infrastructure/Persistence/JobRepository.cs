using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Persistence;

public sealed class JobRepository : IJobRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<JobRepository> _logger;

    public JobRepository(AppDbContext context, ILogger<JobRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<JobListing?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.JobListings.FindAsync([id], cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job by id {JobId}", id);
            throw;
        }
    }

    public async Task<IEnumerable<JobListing>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.JobListings
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all job listings");
            throw;
        }
    }

    public async Task<IEnumerable<JobListing>> GetByProfileIdAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.JobListings
                .Where(j => j.ProfileId == profileId)
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting jobs by profile id {ProfileId}", profileId);
            throw;
        }
    }

    public async Task<JobListing?> GetByExternalIdAsync(string externalId, PlatformType platform)
    {
        try
        {
            return await _context.JobListings
                .FirstOrDefaultAsync(j => j.ExternalId == externalId && j.Platform == platform);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job by external id {ExternalId}", externalId);
            throw;
        }
    }

    public async Task AddAsync(JobListing job, CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.JobListings.AddAsync(job, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding job {JobId} '{Title}'", job.Id, job.Title);
            throw;
        }
    }

    public async Task UpdateAsync(JobListing job, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _context.JobListings.FindAsync([job.Id], cancellationToken);
            if (existing is not null)
            {
                _context.Entry(existing).CurrentValues.SetValues(job);
            }
            else
            {
                _context.JobListings.Attach(job);
                _context.Entry(job).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating job {JobId} '{Title}'", job.Id, job.Title);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var job = await _context.JobListings.FindAsync([id], cancellationToken);
            if (job is not null)
            {
                _context.JobListings.Remove(job);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting job {JobId}", id);
            throw;
        }
    }
}
