using Dapper;
using Microsoft.Data.Sqlite;

namespace AutoApplicator.Infrastructure.Persistence;

public static class DbInitializer
{
    public static void Initialize(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            connection.Execute("""
                CREATE TABLE IF NOT EXISTS SearchProfiles (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Enabled INTEGER NOT NULL DEFAULT 0,
                    Platform INTEGER NOT NULL DEFAULT 0,
                    Keywords TEXT NOT NULL DEFAULT '[]',
                    Location TEXT NOT NULL DEFAULT '[]',
                    DatePosted TEXT NOT NULL DEFAULT '',
                    ExperienceLevel TEXT NOT NULL DEFAULT '[]',
                    JobTypes TEXT NOT NULL DEFAULT '[]',
                    SalaryMin REAL,
                    EasyApplyOnly INTEGER NOT NULL DEFAULT 0,
                    RemoteOnly INTEGER NOT NULL DEFAULT 0,
                    ExcludeTerms TEXT NOT NULL DEFAULT '[]',
                    ResumeFile TEXT NOT NULL DEFAULT '',
                    CoverLetterTemplate TEXT,
                    DefaultAnswers TEXT NOT NULL DEFAULT '{}',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                )
                """, transaction);

            connection.Execute("""
                CREATE TABLE IF NOT EXISTS JobListings (
                    Id TEXT PRIMARY KEY,
                    ExternalId TEXT NOT NULL,
                    Platform INTEGER NOT NULL DEFAULT 0,
                    ProfileId TEXT NOT NULL,
                    Url TEXT NOT NULL DEFAULT '',
                    Title TEXT NOT NULL DEFAULT '',
                    Company TEXT NOT NULL DEFAULT '',
                    Location TEXT NOT NULL DEFAULT '',
                    Salary TEXT,
                    JobType TEXT NOT NULL DEFAULT '',
                    Description TEXT NOT NULL DEFAULT '',
                    PostedDate TEXT NOT NULL,
                    EasyApply INTEGER NOT NULL DEFAULT 0,
                    MatchScore INTEGER,
                    MatchReasoning TEXT,
                    Summary TEXT,
                    RedFlags TEXT NOT NULL DEFAULT '[]',
                    Highlights TEXT NOT NULL DEFAULT '[]',
                    Skills TEXT NOT NULL DEFAULT '[]',
                    Status INTEGER NOT NULL DEFAULT 0,
                    UserNotes TEXT,
                    ReviewedAt TEXT,
                    AppliedAt TEXT,
                    ApplicationAnswers TEXT,
                    CoverLetterUsed TEXT,
                    ResumeUsed TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                )
                """, transaction);

            connection.Execute("""
                CREATE TABLE IF NOT EXISTS CollectedQuestions (
                    Id TEXT PRIMARY KEY,
                    QuestionText TEXT NOT NULL,
                    FieldType INTEGER NOT NULL DEFAULT 0,
                    Options TEXT NOT NULL DEFAULT '[]',
                    Answer TEXT NOT NULL DEFAULT '',
                    Platform INTEGER,
                    JobTitle TEXT,
                    Company TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                )
                """, transaction);

            connection.Execute("""
                CREATE TABLE IF NOT EXISTS UserProfiles (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Skills TEXT NOT NULL DEFAULT '[]',
                    Experience TEXT NOT NULL DEFAULT '[]',
                    Preferences TEXT NOT NULL DEFAULT '{}',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                )
                """, transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
