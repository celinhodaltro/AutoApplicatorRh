using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Interfaces;
using AutoApplicator.Infrastructure.Automation.Platforms;
using AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;
using AutoApplicator.Infrastructure.Persistence;
using AutoApplicator.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoApplicator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IQuestionRepository, QuestionRepository>();
        services.AddSingleton<PlaywrightService>();
        services.AddSingleton<IPlaywrightService>(sp => sp.GetRequiredService<PlaywrightService>());
        services.AddSingleton<AutomationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<NotificationService>();
        services.AddSingleton<IAutomationStateService>(sp => sp.GetRequiredService<AutomationService>());
        services.AddSingleton<PlatformAdapterFactory>();
        services.AddScoped<LinkedInApplicator>();
        services.AddSingleton<ExceptionHandlerService>();

        return services;
    }

    public static void InitializeDatabase(this IServiceProvider serviceProvider)
    {
        using var context = serviceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
    }
}
