using AutoApplicator.Application;
using AutoApplicator.Infrastructure;
using Microsoft.Extensions.Logging;
using Radzen;
using Serilog;
using Serilog.Events;

namespace AutoApplicator.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        EnsurePlaywrightBrowsers();

        var logsBase = Path.Combine(FileSystem.AppDataDirectory, "Logs");
        var servicesLog = Path.Combine(logsBase, "Services", "log-.txt");
        var errorLog = Path.Combine(logsBase, "Error", "log-.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(servicesLog)!);
        Directory.CreateDirectory(Path.GetDirectoryName(errorLog)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .WriteTo.File(
                servicesLog,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Properties:j}{NewLine}")
            .WriteTo.File(
                errorLog,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                restrictedToMinimumLevel: LogEventLevel.Error,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}{NewLine}{Properties:j}{NewLine}")
            .CreateLogger();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "AutoApplicator.db");
        var connectionString = $"Data Source={dbPath}";

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(connectionString);
        builder.Services.AddRadzenComponents();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);

        // ViewModels
        builder.Services.AddTransient<AutoApplicator.Presentation.Components.ViewModels.JobListViewModel>();
        builder.Services.AddTransient<AutoApplicator.Presentation.Components.ViewModels.JobDetailViewModel>();
        builder.Services.AddTransient<AutoApplicator.Presentation.Components.ViewModels.ProfileListViewModel>();
        builder.Services.AddTransient<AutoApplicator.Presentation.Components.ViewModels.QuestionListViewModel>();
        builder.Services.AddTransient<AutoApplicator.Presentation.Components.ViewModels.DashboardViewModel>();

        var app = builder.Build();

        app.Services.InitializeDatabase();

        Log.Information("Application started. Database: {DbPath}. Logs: {LogsPath}", dbPath, logsBase);

        return app;
    }

    private static void EnsurePlaywrightBrowsers()
    {
        var browserDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ms-playwright");

        if (Directory.Exists(browserDir))
        {
            var hasBrowser = Directory.GetDirectories(browserDir)
                .Any(dir => File.Exists(Path.Combine(dir, "chrome-win64", "chrome.exe")));

            if (hasBrowser) return;
        }

        Console.WriteLine("Installing Playwright browsers...");
        Microsoft.Playwright.Program.Main(["install"]);
        Console.WriteLine("Playwright browsers installed.");
    }
}
