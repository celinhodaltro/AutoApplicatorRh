using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Persistence;

public sealed class QuestionRepository : IQuestionRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<QuestionRepository> _logger;

    public QuestionRepository(AppDbContext context, ILogger<QuestionRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CollectedQuestion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.CollectedQuestions.FindAsync([id], cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting question by id {QuestionId}", id);
            throw;
        }
    }

    public async Task<IEnumerable<CollectedQuestion>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.CollectedQuestions
                .OrderByDescending(q => q.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all questions");
            throw;
        }
    }

    public async Task<IEnumerable<CollectedQuestion>> GetUnansweredAsync()
    {
        try
        {
            return await _context.CollectedQuestions
                .Where(q => q.Answer == string.Empty || q.Answer == null)
                .OrderByDescending(q => q.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unanswered questions");
            throw;
        }
    }

    public async Task<CollectedQuestion?> FindByTextAsync(string questionText)
    {
        try
        {
            return await _context.CollectedQuestions
                .FirstOrDefaultAsync(q => q.QuestionText == questionText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding question by text");
            throw;
        }
    }

    public async Task AddAsync(CollectedQuestion question, CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.CollectedQuestions.AddAsync(question, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding question {QuestionId}", question.Id);
            throw;
        }
    }

    public async Task UpdateAsync(CollectedQuestion question, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _context.CollectedQuestions.FindAsync([question.Id], cancellationToken);
            if (existing is not null)
            {
                _context.Entry(existing).CurrentValues.SetValues(question);
            }
            else
            {
                _context.CollectedQuestions.Attach(question);
                _context.Entry(question).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating question {QuestionId}", question.Id);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var question = await _context.CollectedQuestions.FindAsync([id], cancellationToken);
            if (question is not null)
            {
                _context.CollectedQuestions.Remove(question);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting question {QuestionId}", id);
            throw;
        }
    }
}
