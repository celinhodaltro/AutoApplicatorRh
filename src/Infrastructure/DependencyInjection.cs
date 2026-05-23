using AutoApplicator.Domain.Interfaces;
using AutoApplicator.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        DbInitializer.Initialize(connectionString);

        services.AddScoped<IProfileRepository>(sp =>
            new ProfileRepository(connectionString, sp.GetRequiredService<ILogger<ProfileRepository>>()));
        services.AddScoped<IJobRepository>(sp =>
            new JobRepository(connectionString, sp.GetRequiredService<ILogger<JobRepository>>()));
        services.AddScoped<IQuestionRepository>(sp =>
            new QuestionRepository(connectionString, sp.GetRequiredService<ILogger<QuestionRepository>>()));

        return services;
    }
}
