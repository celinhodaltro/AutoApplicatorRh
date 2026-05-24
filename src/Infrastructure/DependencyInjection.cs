using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Interfaces;
using AutoApplicator.Infrastructure.Automation;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Platforms;
using AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;
using AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.DescriptionExtractors;
using AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.FieldFillers;
using AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.StepNavigators;
using AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.SuccessDetectors;
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
        services.AddSingleton<LinkedInDedupService>();
        services.AddSingleton<IHumanBehavior, HumanBehavior>();
        services.AddScoped<LinkedInPaginator>();

        // LinkedInExtractor
        services.AddScoped<LinkedInExtractor>();

        // LinkedInAdapter
        services.AddScoped<LinkedInAdapter>();

        // Field Fillers
        services.AddScoped<IFieldFiller, TextFieldFiller>();
        services.AddScoped<IFieldFiller, SelectFieldFiller>();
        services.AddScoped<IFieldFiller, TypeaheadFieldFiller>();
        services.AddScoped<IFieldFiller, RadioFieldFiller>();
        services.AddScoped<IFieldFiller, FileFieldFiller>();
        services.AddScoped<IFieldFiller, CheckboxFieldFiller>();

        // Description Extractors
        services.AddScoped<IDescriptionExtractor, CssDescriptionExtractor>();
        services.AddScoped<IDescriptionExtractor, RegexDescriptionExtractor>();
        services.AddScoped<IDescriptionExtractor, SemanticDescriptionExtractor>();
        services.AddScoped<IDescriptionExtractor, FallbackDescriptionExtractor>();

        // Step Navigators
        services.AddScoped<IStepNavigator, NextStepNavigator>();
        services.AddScoped<IStepNavigator, ReviewStepNavigator>();
        services.AddScoped<IStepNavigator, SubmitStepNavigator>();

        // Success Detectors
        services.AddScoped<ISuccessDetector, ContainerTextDetector>();
        services.AddScoped<ISuccessDetector, BodyTextDetector>();
        services.AddScoped<ISuccessDetector, PostApplyModalDetector>();
        services.AddScoped<ISuccessDetector, ModalDismissedDetector>();

        return services;
    }

    public static void InitializeDatabase(this IServiceProvider serviceProvider)
    {
        using var context = serviceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
    }
}
