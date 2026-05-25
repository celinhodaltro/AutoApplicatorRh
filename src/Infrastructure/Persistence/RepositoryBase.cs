using Microsoft.EntityFrameworkCore;

namespace AutoApplicator.Infrastructure.Persistence;

public abstract class RepositoryBase<T> where T : class
{
    protected readonly AppDbContext _context;

    protected RepositoryBase(AppDbContext context) => _context = context;

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        var entry = _context.ChangeTracker.Entries<T>()
            .FirstOrDefault(e => e.Entity == entity);
        if (entry is not null)
            entry.CurrentValues.SetValues(entity);
        else
        {
            var existing = await _context.Set<T>().FindAsync(new object[] { GetEntityId(entity) }, ct);
            if (existing is not null)
                _context.Entry(existing).CurrentValues.SetValues(entity);
            else
                _context.Set<T>().Update(entity);
        }
        await _context.SaveChangesAsync(ct);
    }

    protected abstract object GetEntityId(T entity);
}
