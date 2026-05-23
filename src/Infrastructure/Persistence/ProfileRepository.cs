using System.Text.Json;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Persistence;

public sealed class ProfileRepository : IProfileRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ProfileRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ProfileRepository(string connectionString, ILogger<ProfileRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<SearchProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<SearchProfileRow>(
                "SELECT * FROM SearchProfiles WHERE Id = @Id", new { Id = id.ToString() });

            return row is null ? null : MapToEntity(row);
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
            using var connection = new SqliteConnection(_connectionString);
            var rows = await connection.QueryAsync<SearchProfileRow>(
                "SELECT * FROM SearchProfiles ORDER BY Name ASC");

            return rows.Select(MapToEntity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all profiles");
            throw;
        }
    }

    public async Task AddAsync(SearchProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = MapToRow(profile);
            await connection.ExecuteAsync("""
                INSERT INTO SearchProfiles (
                    Id, Name, Enabled, Platform, Keywords, Location, DatePosted,
                    ExperienceLevel, JobTypes, SalaryMin, EasyApplyOnly, RemoteOnly,
                    ExcludeTerms, ResumeFile, CoverLetterTemplate, DefaultAnswers,
                    CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @Name, @Enabled, @Platform, @Keywords, @Location, @DatePosted,
                    @ExperienceLevel, @JobTypes, @SalaryMin, @EasyApplyOnly, @RemoteOnly,
                    @ExcludeTerms, @ResumeFile, @CoverLetterTemplate, @DefaultAnswers,
                    @CreatedAt, @UpdatedAt
                )
                """, row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding profile {ProfileId} '{Name}'", profile.Id, profile.Name);
            throw;
        }
    }

    public async Task UpdateAsync(SearchProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = MapToRow(profile);
            await connection.ExecuteAsync("""
                UPDATE SearchProfiles SET
                    Name = @Name, Enabled = @Enabled, Platform = @Platform,
                    Keywords = @Keywords, Location = @Location, DatePosted = @DatePosted,
                    ExperienceLevel = @ExperienceLevel, JobTypes = @JobTypes,
                    SalaryMin = @SalaryMin, EasyApplyOnly = @EasyApplyOnly,
                    RemoteOnly = @RemoteOnly, ExcludeTerms = @ExcludeTerms,
                    ResumeFile = @ResumeFile, CoverLetterTemplate = @CoverLetterTemplate,
                    DefaultAnswers = @DefaultAnswers, UpdatedAt = @UpdatedAt
                WHERE Id = @Id
                """, row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile {ProfileId} '{Name}'", profile.Id, profile.Name);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync("DELETE FROM SearchProfiles WHERE Id = @Id", new { Id = id.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile {ProfileId}", id);
            throw;
        }
    }

    public async Task<IEnumerable<SearchProfile>> GetEnabledProfilesAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var rows = await connection.QueryAsync<SearchProfileRow>(
                "SELECT * FROM SearchProfiles WHERE Enabled = 1 ORDER BY Name ASC");

            return rows.Select(MapToEntity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting enabled profiles");
            throw;
        }
    }

    private static SearchProfile MapToEntity(SearchProfileRow row)
    {
        return new SearchProfile
        {
            Id = Guid.Parse(row.Id),
            Name = row.Name,
            Enabled = row.Enabled == 1,
            Platform = (PlatformType)row.Platform,
            Keywords = DeserializeList(row.Keywords),
            Location = DeserializeList(row.Location),
            DatePosted = row.DatePosted,
            ExperienceLevel = DeserializeList(row.ExperienceLevel),
            JobTypes = DeserializeList(row.JobTypes),
            SalaryMin = row.SalaryMin is not null ? (decimal)row.SalaryMin : null,
            EasyApplyOnly = row.EasyApplyOnly == 1,
            RemoteOnly = row.RemoteOnly == 1,
            ExcludeTerms = DeserializeList(row.ExcludeTerms),
            ResumeFile = row.ResumeFile,
            CoverLetterTemplate = row.CoverLetterTemplate,
            DefaultAnswers = DeserializeDictionary(row.DefaultAnswers),
            CreatedAt = DateTime.Parse(row.CreatedAt),
            UpdatedAt = DateTime.Parse(row.UpdatedAt)
        };
    }

    private static SearchProfileRow MapToRow(SearchProfile profile)
    {
        return new SearchProfileRow
        {
            Id = profile.Id.ToString(),
            Name = profile.Name,
            Enabled = profile.Enabled ? 1 : 0,
            Platform = (int)profile.Platform,
            Keywords = SerializeList(profile.Keywords),
            Location = SerializeList(profile.Location),
            DatePosted = profile.DatePosted,
            ExperienceLevel = SerializeList(profile.ExperienceLevel),
            JobTypes = SerializeList(profile.JobTypes),
            SalaryMin = profile.SalaryMin is not null ? (double)profile.SalaryMin : null,
            EasyApplyOnly = profile.EasyApplyOnly ? 1 : 0,
            RemoteOnly = profile.RemoteOnly ? 1 : 0,
            ExcludeTerms = SerializeList(profile.ExcludeTerms),
            ResumeFile = profile.ResumeFile,
            CoverLetterTemplate = profile.CoverLetterTemplate,
            DefaultAnswers = SerializeDictionary(profile.DefaultAnswers),
            CreatedAt = profile.CreatedAt.ToString("o"),
            UpdatedAt = profile.UpdatedAt.ToString("o")
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

    private static Dictionary<string, string> DeserializeDictionary(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string SerializeDictionary(Dictionary<string, string> dict)
    {
        return JsonSerializer.Serialize(dict, JsonOptions);
    }

#pragma warning disable S101
    private sealed class SearchProfileRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Enabled { get; set; }
        public int Platform { get; set; }
        public string Keywords { get; set; } = "[]";
        public string Location { get; set; } = "[]";
        public string DatePosted { get; set; } = string.Empty;
        public string ExperienceLevel { get; set; } = "[]";
        public string JobTypes { get; set; } = "[]";
        public double? SalaryMin { get; set; }
        public int EasyApplyOnly { get; set; }
        public int RemoteOnly { get; set; }
        public string ExcludeTerms { get; set; } = "[]";
        public string ResumeFile { get; set; } = string.Empty;
        public string? CoverLetterTemplate { get; set; }
        public string DefaultAnswers { get; set; } = "{}";
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
#pragma warning restore S101
}
