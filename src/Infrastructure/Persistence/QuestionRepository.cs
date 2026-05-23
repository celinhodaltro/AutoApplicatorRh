using System.Text.Json;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Persistence;

public sealed class QuestionRepository : IQuestionRepository
{
    private readonly string _connectionString;
    private readonly ILogger<QuestionRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public QuestionRepository(string connectionString, ILogger<QuestionRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<CollectedQuestion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<QuestionRow>(
                "SELECT * FROM CollectedQuestions WHERE Id = @Id", new { Id = id.ToString() });

            return row is null ? null : MapToEntity(row);
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
            using var connection = new SqliteConnection(_connectionString);
            var rows = await connection.QueryAsync<QuestionRow>(
                "SELECT * FROM CollectedQuestions ORDER BY CreatedAt DESC");

            return rows.Select(MapToEntity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all questions");
            throw;
        }
    }

    public async Task AddAsync(CollectedQuestion question, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = MapToRow(question);
            await connection.ExecuteAsync("""
                INSERT INTO CollectedQuestions (
                    Id, QuestionText, FieldType, Options, Answer,
                    Platform, JobTitle, Company, CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @QuestionText, @FieldType, @Options, @Answer,
                    @Platform, @JobTitle, @Company, @CreatedAt, @UpdatedAt
                )
                """, row);
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
            using var connection = new SqliteConnection(_connectionString);
            var row = MapToRow(question);
            await connection.ExecuteAsync("""
                UPDATE CollectedQuestions SET
                    QuestionText = @QuestionText, FieldType = @FieldType,
                    Options = @Options, Answer = @Answer, Platform = @Platform,
                    JobTitle = @JobTitle, Company = @Company, UpdatedAt = @UpdatedAt
                WHERE Id = @Id
                """, row);
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
            using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync("DELETE FROM CollectedQuestions WHERE Id = @Id", new { Id = id.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting question {QuestionId}", id);
            throw;
        }
    }

    public async Task<CollectedQuestion?> FindByTextAsync(string questionText)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<QuestionRow>(
                "SELECT * FROM CollectedQuestions WHERE QuestionText = @QuestionText LIMIT 1",
                new { QuestionText = questionText });

            return row is null ? null : MapToEntity(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding question by text");
            throw;
        }
    }

    public async Task<IEnumerable<CollectedQuestion>> GetUnansweredAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var rows = await connection.QueryAsync<QuestionRow>(
                "SELECT * FROM CollectedQuestions WHERE TRIM(Answer) = '' ORDER BY CreatedAt DESC");

            return rows.Select(MapToEntity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unanswered questions");
            throw;
        }
    }

    private static CollectedQuestion MapToEntity(QuestionRow row)
    {
        return new CollectedQuestion
        {
            Id = Guid.Parse(row.Id),
            QuestionText = row.QuestionText,
            FieldType = (QuestionFieldType)row.FieldType,
            Options = DeserializeList(row.Options),
            Answer = row.Answer,
            Platform = row.Platform is not null ? (PlatformType)row.Platform : null,
            JobTitle = row.JobTitle,
            Company = row.Company,
            CreatedAt = DateTime.Parse(row.CreatedAt),
            UpdatedAt = DateTime.Parse(row.UpdatedAt)
        };
    }

    private static QuestionRow MapToRow(CollectedQuestion question)
    {
        return new QuestionRow
        {
            Id = question.Id.ToString(),
            QuestionText = question.QuestionText,
            FieldType = (int)question.FieldType,
            Options = SerializeList(question.Options),
            Answer = question.Answer,
            Platform = question.Platform is not null ? (int)question.Platform : null,
            JobTitle = question.JobTitle,
            Company = question.Company,
            CreatedAt = question.CreatedAt.ToString("o"),
            UpdatedAt = question.UpdatedAt.ToString("o")
        };
    }

    private static List<string> DeserializeList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string SerializeList(List<string> list)
    {
        return JsonSerializer.Serialize(list, JsonOptions);
    }

#pragma warning disable S101
    private sealed class QuestionRow
    {
        public string Id { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public int FieldType { get; set; }
        public string Options { get; set; } = "[]";
        public string Answer { get; set; } = string.Empty;
        public int? Platform { get; set; }
        public string? JobTitle { get; set; }
        public string? Company { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
#pragma warning restore S101
}
