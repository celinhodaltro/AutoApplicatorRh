using System.Text.Json;
using AutoApplicator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoApplicator.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DbSet<SearchProfile> SearchProfiles => Set<SearchProfile>();
    public DbSet<JobListing> JobListings => Set<JobListing>();
    public DbSet<CollectedQuestion> CollectedQuestions => Set<CollectedQuestion>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SearchProfile>(entity =>
        {
            entity.ToTable("SearchProfiles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Platform).HasConversion<int>();
            SetStringListConversion(entity.Property(e => e.Keywords));
            SetStringListConversion(entity.Property(e => e.Location));
            SetStringListConversion(entity.Property(e => e.ExperienceLevel));
            SetStringListConversion(entity.Property(e => e.JobTypes));
            SetStringListConversion(entity.Property(e => e.ExcludeTerms));
            SetDictionaryConversion(entity.Property(e => e.DefaultAnswers));
            entity.Property(e => e.SalaryMin).HasColumnType("REAL");
        });

        modelBuilder.Entity<JobListing>(entity =>
        {
            entity.ToTable("JobListings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Platform).HasConversion<int>();
            entity.Property(e => e.Status).HasConversion<int>();
            SetStringListConversion(entity.Property(e => e.RedFlags));
            SetStringListConversion(entity.Property(e => e.Highlights));
            SetStringListConversion(entity.Property(e => e.Skills));
            SetNullableDictionaryConversion(entity.Property(e => e.ApplicationAnswers));
        });

        modelBuilder.Entity<CollectedQuestion>(entity =>
        {
            entity.ToTable("CollectedQuestions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.FieldType).HasConversion<int>();
            entity.Property(e => e.Platform).HasConversion<int?>();
            entity.Property(e => e.Group).HasMaxLength(200);
            SetStringListConversion(entity.Property(e => e.Options));
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("UserProfiles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            SetStringListConversion(entity.Property(e => e.Skills));
            SetStringListConversion(entity.Property(e => e.Experience));
            SetDictionaryConversion(entity.Property(e => e.Preferences));
        });
    }

    private static void SetStringListConversion(PropertyBuilder<List<string>> property)
    {
        property.HasConversion(new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new List<string>()));
    }

    private static void SetDictionaryConversion(PropertyBuilder<Dictionary<string, string>> property)
    {
        property.HasConversion(new ValueConverter<Dictionary<string, string>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions) ?? new Dictionary<string, string>()));
    }

    private static void SetNullableDictionaryConversion(PropertyBuilder<Dictionary<string, string>?> property)
    {
        property.HasConversion(new ValueConverter<Dictionary<string, string>?, string>(
            v => v == null ? "null" : JsonSerializer.Serialize(v, JsonOptions),
            v => v == "null" ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions)));
    }
}
