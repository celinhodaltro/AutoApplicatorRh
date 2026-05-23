using System.Text.Json;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Persistence;

public sealed class JobRepository : IJobRepository
{
    private readonly string _connectionString;
    private readonly ILogger<JobRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JobRepository(string connectionString, ILogger<JobRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<JobListing?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<JobListingRow>(
                "SELECT * FROM JobListings WHERE Id = @Id", new { Id = id.ToString() });

            return row is null ? null : MapToEntity(row);
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
            using var connection = new SqliteConnection(_connectionString);
            var rows = await connection.QueryAsync<JobListingRow>("SELECT * FROM JobListings ORDER BY CreatedAt DESC");
            return rows.Select(MapToEntity);
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
            using var connection = new SqliteConnection(_connectionString);
            var rows = await connection.QueryAsync<JobListingRow>(
                "SELECT * FROM JobListings WHERE ProfileId = @ProfileId ORDER BY CreatedAt DESC",
                new { ProfileId = profileId.ToString() });

            return rows.Select(MapToEntity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting jobs by profile id {ProfileId}", profileId);
            throw;
        }
    }

    public async Task AddAsync(JobListing job, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = MapToRow(job);
            await connection.ExecuteAsync("""
                INSERT INTO JobListings (
                    Id, ExternalId, Platform, ProfileId, Url, Title, Company,
                    Location, Salary, JobType, Description, PostedDate, EasyApply,
                    MatchScore, MatchReasoning, Summary, RedFlags, Highlights, Skills,
                    Status, UserNotes, ReviewedAt, AppliedAt, ApplicationAnswers,
                    CoverLetterUsed, ResumeUsed, CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @ExternalId, @Platform, @ProfileId, @Url, @Title, @Company,
                    @Location, @Salary, @JobType, @Description, @PostedDate, @EasyApply,
                    @MatchScore, @MatchReasoning, @Summary, @RedFlags, @Highlights, @Skills,
                    @Status, @UserNotes, @ReviewedAt, @AppliedAt, @ApplicationAnswers,
                    @CoverLetterUsed, @ResumeUsed, @CreatedAt, @UpdatedAt
                )
                """, row);
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
            using var connection = new SqliteConnection(_connectionString);
            var row = MapToRow(job);
            await connection.ExecuteAsync("""
                UPDATE JobListings SET
                    ExternalId = @ExternalId, Platform = @Platform, ProfileId = @ProfileId,
                    Url = @Url, Title = @Title, Company = @Company, Location = @Location,
                    Salary = @Salary, JobType = @JobType, Description = @Description,
                    PostedDate = @PostedDate, EasyApply = @EasyApply, MatchScore = @MatchScore,
                    MatchReasoning = @MatchReasoning, Summary = @Summary, RedFlags = @RedFlags,
                    Highlights = @Highlights, Skills = @Skills, Status = @Status,
                    UserNotes = @UserNotes, ReviewedAt = @ReviewedAt, AppliedAt = @AppliedAt,
                    ApplicationAnswers = @ApplicationAnswers, CoverLetterUsed = @CoverLetterUsed,
                    ResumeUsed = @ResumeUsed, UpdatedAt = @UpdatedAt
                WHERE Id = @Id
                """, row);
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
            using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync("DELETE FROM JobListings WHERE Id = @Id", new { Id = id.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting job {JobId}", id);
            throw;
        }
    }

    public async Task<JobListing?> GetByExternalIdAsync(string externalId, PlatformType platform)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<JobListingRow>(
                "SELECT * FROM JobListings WHERE ExternalId = @ExternalId AND Platform = @Platform",
                new { ExternalId = externalId, Platform = (int)platform });

            return row is null ? null : MapToEntity(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job by external id {ExternalId}", externalId);
            throw;
        }
    }

    private static JobListing MapToEntity(JobListingRow row)
    {
        return new JobListing
        {
            Id = Guid.Parse(row.Id),
            ExternalId = row.ExternalId,
            Platform = (PlatformType)row.Platform,
            ProfileId = Guid.Parse(row.ProfileId),
            Url = row.Url,
            Title = row.Title,
            Company = row.Company,
            Location = row.Location,
            Salary = row.Salary,
            JobType = row.JobType,
            Description = row.Description,
            PostedDate = DateTime.Parse(row.PostedDate),
            EasyApply = row.EasyApply == 1,
            MatchScore = row.MatchScore,
            MatchReasoning = row.MatchReasoning,
            Summary = row.Summary,
            RedFlags = DeserializeList(row.RedFlags),
            Highlights = DeserializeList(row.Highlights),
            Skills = DeserializeList(row.Skills),
            Status = (JobStatus)row.Status,
            UserNotes = row.UserNotes,
            ReviewedAt = row.ReviewedAt is not null ? DateTime.Parse(row.ReviewedAt) : null,
            AppliedAt = row.AppliedAt is not null ? DateTime.Parse(row.AppliedAt) : null,
            ApplicationAnswers = row.ApplicationAnswers is not null
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(row.ApplicationAnswers, JsonOptions)
                : null,
            CoverLetterUsed = row.CoverLetterUsed,
            ResumeUsed = row.ResumeUsed,
            CreatedAt = DateTime.Parse(row.CreatedAt),
            UpdatedAt = DateTime.Parse(row.UpdatedAt)
        };
    }

    private static JobListingRow MapToRow(JobListing job)
    {
        return new JobListingRow
        {
            Id = job.Id.ToString(),
            ExternalId = job.ExternalId,
            Platform = (int)job.Platform,
            ProfileId = job.ProfileId.ToString(),
            Url = job.Url,
            Title = job.Title,
            Company = job.Company,
            Location = job.Location,
            Salary = job.Salary,
            JobType = job.JobType,
            Description = job.Description,
            PostedDate = job.PostedDate.ToString("o"),
            EasyApply = job.EasyApply ? 1 : 0,
            MatchScore = job.MatchScore,
            MatchReasoning = job.MatchReasoning,
            Summary = job.Summary,
            RedFlags = SerializeList(job.RedFlags),
            Highlights = SerializeList(job.Highlights),
            Skills = SerializeList(job.Skills),
            Status = (int)job.Status,
            UserNotes = job.UserNotes,
            ReviewedAt = job.ReviewedAt?.ToString("o"),
            AppliedAt = job.AppliedAt?.ToString("o"),
            ApplicationAnswers = job.ApplicationAnswers is not null
                ? JsonSerializer.Serialize(job.ApplicationAnswers, JsonOptions)
                : null,
            CoverLetterUsed = job.CoverLetterUsed,
            ResumeUsed = job.ResumeUsed,
            CreatedAt = job.CreatedAt.ToString("o"),
            UpdatedAt = job.UpdatedAt.ToString("o")
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
    private sealed class JobListingRow
    {
        public string Id { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;
        public int Platform { get; set; }
        public string ProfileId { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string? Salary { get; set; }
        public string JobType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PostedDate { get; set; } = string.Empty;
        public int EasyApply { get; set; }
        public int? MatchScore { get; set; }
        public string? MatchReasoning { get; set; }
        public string? Summary { get; set; }
        public string RedFlags { get; set; } = "[]";
        public string Highlights { get; set; } = "[]";
        public string Skills { get; set; } = "[]";
        public int Status { get; set; }
        public string? UserNotes { get; set; }
        public string? ReviewedAt { get; set; }
        public string? AppliedAt { get; set; }
        public string? ApplicationAnswers { get; set; }
        public string? CoverLetterUsed { get; set; }
        public string? ResumeUsed { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
#pragma warning restore S101
}
