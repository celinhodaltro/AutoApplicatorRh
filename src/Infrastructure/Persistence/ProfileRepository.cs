using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Persistence;

public sealed class ProfileRepository : RepositoryBase<SearchProfile>, IProfileRepository
{
    private readonly ILogger<ProfileRepository> _logger;

    public ProfileRepository(AppDbContext context, ILogger<ProfileRepository> logger) : base(context)
    {
        _logger = logger;
    }

    protected override object GetEntityId(SearchProfile entity) => entity.Id;

    public async Task<SearchProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SearchProfiles.FindAsync([id], cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting profile by id {ProfileId}", id);
            throw;
        }
    }

    public async Task<IEnumerable<SearchProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SearchProfiles
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all profiles");
            throw;
        }
    }

    public async Task<IEnumerable<SearchProfile>> GetEnabledProfilesAsync()
    {
        try
        {
            return await _context.SearchProfiles
                .Where(p => p.Enabled)
                .OrderBy(p => p.Name)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting enabled profiles");
            throw;
        }
    }

    public async Task AddAsync(SearchProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SearchProfiles.AddAsync(profile, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding profile {ProfileId} '{Name}'", profile.Id, profile.Name);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.SearchProfiles.FindAsync([id], cancellationToken);
            if (profile is not null)
            {
                _context.SearchProfiles.Remove(profile);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile {ProfileId}", id);
            throw;
        }
    }
}
