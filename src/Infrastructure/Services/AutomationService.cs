using System.Diagnostics;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using AutoApplicator.Infrastructure.Automation.Platforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Services;

public sealed class AutomationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlaywrightService _playwrightService;
    private readonly PlatformAdapterFactory _adapterFactory;
    private readonly NotificationService _notifications;
    private readonly ILogger<AutomationService> _logger;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public AutomationStatus CurrentStatus { get; private set; } = new();

    public event Action? StatusChanged;

    public AutomationService(
        IServiceScopeFactory scopeFactory,
        PlaywrightService playwrightService,
        PlatformAdapterFactory adapterFactory,
        NotificationService notifications,
        ILogger<AutomationService> logger)
    {
        _scopeFactory = scopeFactory;
        _playwrightService = playwrightService;
        _adapterFactory = adapterFactory;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task StartAsync(bool globalEasyApply = false)
    {
        if (IsRunning)
        {
            _logger.LogWarning("Start requested but automation is already running");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        var token = _cts.Token;

        UpdateStatus("Initializing...", 0, 0);
        _logger.LogInformation("===== AUTOMATION STARTED =====");

        if (globalEasyApply)
            _logger.LogInformation("Global Easy Apply filter is ON — skipping non-Easy-Apply jobs");

        try
        {
            List<SearchProfile> profiles;
            using (var scope = _scopeFactory.CreateScope())
            {
                var profileRepo = scope.ServiceProvider.GetRequiredService<IProfileRepository>();
                profiles = (await profileRepo.GetEnabledProfilesAsync()).ToList();
            }

            _logger.LogInformation("Found {ProfileCount} enabled profile(s) to process", profiles.Count);

            if (profiles.Count == 0)
            {
                _logger.LogWarning("No enabled profiles found.");
                _notifications.Add(NotificationType.Warning, "No Profiles", "Create and enable a profile first.", "Go to Profiles", "/profiles");
                UpdateStatus("No enabled profiles found.", 0, 0);
                return;
            }

            UpdateStatus($"Processing {profiles.Count} profile(s)...", 0, profiles.Count);
            await _playwrightService.InitializeAsync();
            var page = _playwrightService.GetPage()
                ?? throw new InvalidOperationException("Playwright page not available after initialization");

            var totalJobsFound = 0;
            var profilesWithIssues = 0;

            for (var i = 0; i < profiles.Count; i++)
            {
                if (token.IsCancellationRequested) break;

                var profile = profiles[i];
                var result = await ProcessProfileAsync(profile, page, i + 1, profiles.Count, globalEasyApply, token);
                totalJobsFound += result.JobsFound;
                if (!result.Success) profilesWithIssues++;
            }

            stopwatch.Stop();

            if (token.IsCancellationRequested)
            {
                _logger.LogWarning("===== AUTOMATION CANCELLED after {Elapsed} =====", stopwatch.Elapsed);
                _notifications.Add(NotificationType.Warning, "Automation Cancelled", $"Stopped after {stopwatch.Elapsed.TotalMinutes:F1} min.");
                UpdateStatus("Automation cancelled.", 0, 0);
            }
            else
            {
                var summary = profiles.Count == 1
                    ? $"Processed 1 profile, found {totalJobsFound} job(s)."
                    : $"Processed {profiles.Count} profiles, found {totalJobsFound} job(s).{(profilesWithIssues > 0 ? $" {profilesWithIssues} had issues." : "")}";

                _logger.LogInformation("===== AUTOMATION COMPLETED: {ProfileCount} profile(s), {TotalJobs} job(s) found in {Elapsed} =====", profiles.Count, totalJobsFound, stopwatch.Elapsed);

                var type = profilesWithIssues > 0 ? NotificationType.Warning : NotificationType.Success;
                _notifications.Add(type, "Automation Complete", summary, "View Jobs", "/jobs");

                UpdateStatus($"Completed: {totalJobsFound} job(s) found.", profiles.Count, profiles.Count);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "===== AUTOMATION FAILED after {Elapsed}: {ErrorMessage} =====", stopwatch.Elapsed, ex.Message);
            _notifications.Add(NotificationType.Error, "Automation Failed", ex.Message);
            UpdateStatus($"Error: {ex.Message}", 0, 0);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _notifications.Add(NotificationType.Warning, "Automation", "Stopping automation...");
        UpdateStatus("Stopping automation...", 0, 0);
    }

    private async Task<ProfileResult> ProcessProfileAsync(
        SearchProfile profile, Microsoft.Playwright.IPage page,
        int current, int total, bool globalEasyApply, CancellationToken token)
    {
        var adapter = _adapterFactory.Create(profile.Platform);

        _logger.LogInformation("[{Current}/{Total}] Processing profile '{ProfileName}' | Platform: {Platform}", current, total, profile.Name, profile.Platform);
        UpdateStatus($"Checking '{profile.Name}' on {profile.Platform}...", current, total);

        try
        {
            var searchUrl = adapter.BuildSearchUrl(profile);
            _logger.LogInformation("[{Current}/{Total}] Navigating to search URL: {SearchUrl}", current, total, searchUrl);

            await page.GotoAsync(searchUrl, new() { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
            await Task.Delay(3000, token);

            var auth = await adapter.IsAuthenticatedAsync(page);
            if (!auth.IsAuthenticated)
            {
                _logger.LogWarning("[{Current}/{Total}] {Platform}: {Message}", current, total, profile.Platform, auth.Message);
                _notifications.Add(NotificationType.Error, $"{profile.Platform} - Login Required", auth.Message, "Open Login", auth.LoginUrl);
                UpdateStatus($"Login required for {profile.Name} on {profile.Platform}", current, total);
                return new ProfileResult(0, false);
            }

            UpdateStatus($"Extracting job listings for '{profile.Name}'...", current, total);
            var extractedJobs = await adapter.ExtractListingsAsync(page);

            var filteredJobs = globalEasyApply
                ? extractedJobs.Where(j => j.EasyApply).ToList()
                : extractedJobs;

            if (globalEasyApply && extractedJobs.Count != filteredJobs.Count)
                _logger.LogInformation("[{Current}/{Total}] Filtered to {KeepCount}/{TotalCount} Easy Apply jobs for '{ProfileName}'",
                    current, total, filteredJobs.Count, extractedJobs.Count, profile.Name);

            using var scope = _scopeFactory.CreateScope();
            var jobRepository = scope.ServiceProvider.GetRequiredService<IJobRepository>();

            var savedCount = 0;
            for (var i = 0; i < filteredJobs.Count; i++)
            {
                if (token.IsCancellationRequested) break;

                var extracted = filteredJobs[i];
                UpdateStatus($"Fetching details: {extracted.Title} ({i + 1}/{filteredJobs.Count})", current, total);

                JobDetail? details = null;
                try
                {
                    details = await adapter.ExtractJobDetailsAsync(page, extracted.Url);
                    _logger.LogInformation("[{Current}/{Total}] Fetched description for '{Title}' ({Length} chars)",
                        current, total, extracted.Title,
                        details.Description?.Length ?? 0);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[{Current}/{Total}] Failed to fetch details for '{Title}'", current, total, extracted.Title);
                }

                var job = CreateJobListing(extracted, profile, details);
                await jobRepository.AddAsync(job, default);
                savedCount++;
            }

            _logger.LogInformation("[{Current}/{Total}] Saved {JobCount} job(s) for '{ProfileName}'", current, total, savedCount, profile.Name);
            UpdateStatus($"Saved {savedCount} job(s) for '{profile.Name}'", current, total);

            if (savedCount > 0)
                _notifications.Add(NotificationType.Success, $"{profile.Platform} - {profile.Name}", $"Saved {savedCount} job(s).");

            return new ProfileResult(savedCount, true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Current}/{Total}] Failed for '{ProfileName}'", current, total, profile.Name);
            _notifications.Add(NotificationType.Error, $"{profile.Platform} Error", $"{profile.Name}: {ex.Message}");
            UpdateStatus($"Failed: {profile.Name} - {ex.Message}", current, total);
            return new ProfileResult(0, false);
        }
    }

    private static JobListing CreateJobListing(ExtractedJob extracted, SearchProfile profile, JobDetail? details = null)
    {
        return new JobListing
        {
            Id = Guid.NewGuid(),
            ExternalId = extracted.ExternalId,
            Platform = profile.Platform,
            ProfileId = profile.Id,
            Url = extracted.Url,
            Title = extracted.Title,
            Company = extracted.Company,
            Location = extracted.Location,
            Description = details?.Description ?? string.Empty,
            Salary = details?.Salary,
            JobType = string.Join(", ", profile.JobTypes),
            PostedDate = DateTime.UtcNow,
            EasyApply = extracted.EasyApply,
            Status = JobStatus.New,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private void UpdateStatus(string message, int current, int total)
    {
        CurrentStatus = new AutomationStatus(message, current, total);
        StatusChanged?.Invoke();
    }

    private sealed record ProfileResult(int JobsFound, bool Success);
}

public sealed record AutomationStatus(string Message, int Current, int Total)
{
    public AutomationStatus() : this("Idle", 0, 0) { }
    public string Progress => Total > 0 ? $"{Current}/{Total}" : "";
}
