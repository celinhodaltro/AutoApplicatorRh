using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using AutoApplicator.Infrastructure.Automation.Models;
using AutoApplicator.Infrastructure.Automation.Platforms;
using AutoApplicator.Infrastructure.Services.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Services;

public sealed class JobSearchService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlaywrightService _playwrightService;
    private readonly PlatformAdapterFactory _adapterFactory;
    private readonly NotificationService _notifications;
    private readonly ILogger<JobSearchService> _logger;

    /// <summary>
    /// Callback for profile-level status updates. Signature: (profileName, status).
    /// </summary>
    internal Action<string, string>? OnProfileStatusUpdate { get; set; }

    /// <summary>
    /// Callback for general status updates. Signature: (message, current, total).
    /// </summary>
    internal Action<string, int, int>? OnStatusUpdate { get; set; }

    public JobSearchService(
        IServiceScopeFactory scopeFactory,
        PlaywrightService playwrightService,
        PlatformAdapterFactory adapterFactory,
        NotificationService notifications,
        ILogger<JobSearchService> logger)
    {
        _scopeFactory = scopeFactory;
        _playwrightService = playwrightService;
        _adapterFactory = adapterFactory;
        _notifications = notifications;
        _logger = logger;
    }

    private void UpdateProfileStatus(string profileName, string status)
    {
        OnProfileStatusUpdate?.Invoke(profileName, status);
    }

    private void UpdateStatus(string message, int current, int total)
    {
        OnStatusUpdate?.Invoke(message, current, total);
    }

    // ──────────────────────────────────────────────
    //  Public entry point – standalone search
    // ──────────────────────────────────────────────

    public async Task<int> SearchAllProfilesAsync(bool globalEasyApply, int maxJobs, CancellationToken token)
    {
        List<SearchProfile> profiles;
        using (var scope = _scopeFactory.CreateScope())
        {
            var profileRepo = scope.ServiceProvider.GetRequiredService<IProfileRepository>();
            profiles = (await profileRepo.GetEnabledProfilesAsync()).ToList();
        }

        _logger.LogInformation("Found {ProfileCount} enabled profile(s) to search", profiles.Count);

        if (profiles.Count == 0)
            throw new NoEnabledProfilesException();

        await _playwrightService.InitializeAsync();

        UpdateStatus($"Searching {profiles.Count} profile(s) in parallel...", 0, profiles.Count);

        var totalFound = 0;
        var tasks = profiles.Select(profile =>
            Task.Run(async () =>
            {
                var profilePage = await _playwrightService.CreateNewPageAsync();
                try
                {
                    var result = await SearchProfileAsync(
                        profile, profilePage, globalEasyApply, maxJobs, token);
                    Interlocked.Add(ref totalFound, result);
                }
                finally
                {
                    try { await profilePage.CloseAsync(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to close profile page for '{Profile}'", profile.Name); }
                }
            }, token));

        await Task.WhenAll(tasks);

        _logger.LogInformation("Search complete: {TotalJobs} job(s) found across {Profiles} profile(s)", totalFound, profiles.Count);

        if (totalFound > 0)
            _notifications.Add(NotificationType.Success, "Search Complete", $"Found {totalFound} job(s) across {profiles.Count} profile(s).", "View Jobs", "/jobs");
        else
            _notifications.Add(NotificationType.Info, "Search Complete", "No new jobs found.");

        return totalFound;
    }

    // ──────────────────────────────────────────────
    //  Internal – per-profile search (used by orchestrator)
    // ──────────────────────────────────────────────

    internal async Task<int> SearchProfileAsync(
        SearchProfile profile, IBrowserPage page,
        bool globalEasyApply, int maxJobs, CancellationToken token)
    {
        var adapter = _adapterFactory.Create(profile.Platform);

        _logger.LogInformation("[Profile: {Name}] Searching on {Platform}", profile.Name, profile.Platform);
        UpdateStatus($"Searching: {profile.Name}...", 0, 0);
        UpdateProfileStatus(profile.Name, "Searching...");

        try
        {
            var searchUrl = await NavigateToSearchUrlAsync(adapter, profile, page, token);
            await WaitForLoginAsync(adapter, page, profile, token);

            // Fase 1: Collect all jobs from all pages quickly (card-level only, no details)
            var allJobs = await CollectAllJobsFromAllPagesAsync(adapter, page, profile, maxJobs, globalEasyApply, token);

            if (allJobs.Count == 0)
            {
                _logger.LogInformation("[Profile: {Name}] No jobs found", profile.Name);
                UpdateProfileStatus(profile.Name, "Complete");
                return 0;
            }

            _logger.LogInformation("[Profile: {Name}] Collected {Total} job(s) from all pages", profile.Name, allJobs.Count);

            // Fase 2: Save all jobs in batch (without details)
            UpdateProfileStatus(profile.Name, "Saving jobs...");
            await SaveJobsBatchAsync(allJobs, profile);

            _logger.LogInformation("[Profile: {Name}] Saved {Count} job(s)", profile.Name, allJobs.Count);

            UpdateProfileStatus(profile.Name, "Complete");
            return allJobs.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Profile: {Name}] Failed", profile.Name);
            _notifications.Add(NotificationType.Error, $"{profile.Platform} Error", $"{profile.Name}: {ex.Message}");
            return 0;
        }
    }

    // ──────────────────────────────────────────────
    //  Fase 1: Collect all jobs from all pages (card-level only, no details)
    // ──────────────────────────────────────────────

    internal async Task<List<ExtractedJob>> CollectAllJobsFromAllPagesAsync(
        IPlatformAdapter adapter, IBrowserPage page, SearchProfile profile,
        int maxJobs, bool globalEasyApply, CancellationToken token)
    {
        var allJobs = new List<ExtractedJob>();
        var pageNum = 1;

        while (!token.IsCancellationRequested && allJobs.Count < maxJobs)
        {
            if (pageNum == 1)
            {
                // Page 1: navigate with DOMContentLoaded (faster than fixed 3s delay)
                var searchUrl = adapter.BuildSearchUrl(profile);
                var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
                await innerPage.GotoAsync(searchUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            }
            else
            {
                await adapter.NavigateToPageAsync(page, profile, pageNum);
                await Task.Delay(1500, token);
            }

            var jobs = await adapter.ExtractListingsAsync(page);

            if (globalEasyApply)
                jobs = jobs.Where(j => j.EasyApply).ToList();

            _logger.LogInformation("[Profile: {Name}] Page {PageNum}: collected {Count} jobs. Total: {Total}",
                profile.Name, pageNum, jobs.Count, allJobs.Count);

            allJobs.AddRange(jobs);

            if (jobs.Count == 0 || jobs.Count < 25)
                break;

            pageNum++;
        }

        return allJobs;
    }

    // ──────────────────────────────────────────────
    //  Fase 2: Save jobs in batch (without details)
    // ──────────────────────────────────────────────

    private async Task SaveJobsBatchAsync(List<ExtractedJob> jobs, SearchProfile profile)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        foreach (var extracted in jobs)
        {
            var job = new JobListing
            {
                Id = Guid.NewGuid(),
                ExternalId = extracted.ExternalId,
                Platform = profile.Platform,
                ProfileId = profile.Id,
                Url = extracted.Url,
                Title = extracted.Title,
                Company = extracted.Company,
                Location = extracted.Location,
                Description = string.Empty,
                Salary = null,
                JobType = string.Join(", ", profile.JobTypes),
                PostedDate = DateTime.UtcNow,
                EasyApply = extracted.EasyApply,
                Status = JobStatus.New,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await jobRepo.AddAsync(job, default);
        }
    }

    // ──────────────────────────────────────────────
    //  Internal helpers (used by orchestrator)
    // ──────────────────────────────────────────────

    internal async Task<string> NavigateToSearchUrlAsync(
        IPlatformAdapter adapter, SearchProfile profile, IBrowserPage page,
        CancellationToken token)
    {
        var searchUrl = adapter.BuildSearchUrl(profile);
        _logger.LogInformation("[Profile: {Name}] URL: {SearchUrl}", profile.Name, searchUrl);

        await page.GoToAsync(searchUrl);
        await Task.Delay(3000, token);

        return searchUrl;
    }

    internal async Task WaitForLoginAsync(IPlatformAdapter adapter, IBrowserPage page, SearchProfile profile, CancellationToken token)
    {
        var auth = await adapter.IsAuthenticatedAsync(page);
        if (auth.IsAuthenticated)
            return;

        var platformName = profile.Platform.ToString();
        var loginUrl = auth.LoginUrl;

        await NotifyLoginRequiredAndNavigateAsync(page, platformName, loginUrl);
        await PollForLoginCompletionAsync(adapter, page, platformName, loginUrl, token);
    }

    private async Task NotifyLoginRequiredAndNavigateAsync(IBrowserPage page, string platformName, string loginUrl)
    {
        _notifications.Add(NotificationType.Warning,
            $"{platformName} Login Required",
            $"Login required for {platformName}. Please log in in the browser window.",
            $"Open {platformName}",
            loginUrl);

        UpdateStatus($"Waiting for {platformName} login...", 0, 0);
        _logger.LogWarning("Login required for {Platform}. Navigated to login URL: {LoginUrl}", platformName, loginUrl);

        await page.GoToAsync(loginUrl);
    }

    private async Task PollForLoginCompletionAsync(
        IPlatformAdapter adapter, IBrowserPage page, string platformName, string loginUrl, CancellationToken token)
    {
        const int CheckIntervalMs = 2000;
        const int NotifyIntervalMs = 60_000;

        var lastNotifyTime = DateTime.UtcNow;
        await Task.Delay(5000, CancellationToken.None);

        while (!token.IsCancellationRequested)
        {
            var auth = await adapter.IsAuthenticatedAsync(page);

            if (auth.IsAuthenticated)
            {
                await _playwrightService.SaveCookiesAsync();

                _notifications.Add(NotificationType.Success, $"{platformName} Login", "Login successful! Continuing automation...");
                UpdateStatus($"{platformName} login OK!", 0, 0);
                _logger.LogInformation("Login detected for {Platform}. Continuing automation.", platformName);
                return;
            }

            if ((DateTime.UtcNow - lastNotifyTime).TotalMilliseconds >= NotifyIntervalMs)
            {
                _notifications.Add(NotificationType.Warning,
                    $"{platformName} Login Required",
                    $"Still waiting for {platformName} login...",
                    $"Open {platformName}",
                    loginUrl);

                lastNotifyTime = DateTime.UtcNow;
            }

            await Task.Delay(CheckIntervalMs, CancellationToken.None);
        }
    }

    internal async Task<(JobDetail? Details, ExtractedJob UpdatedExtracted)> FetchJobDetailsAsync(
        IBrowserPage page, IPlatformAdapter adapter, ExtractedJob extracted,
        string searchUrl, CancellationToken token)
    {
        JobDetail? details = null;
        try
        {
            details = await adapter.ExtractJobDetailsAsync(page, extracted.Url);

            if (details is not null)
            {
                if (!string.IsNullOrEmpty(details.Title)) extracted = extracted with { Title = details.Title };
                if (!string.IsNullOrEmpty(details.Company)) extracted = extracted with { Company = details.Company };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed fetching details for '{Title}'", extracted.Title);
        }

        try
        {
            await page.GoToAsync(searchUrl);
            await Task.Delay(2000, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to navigate back to search results");
        }

        return (details, extracted);
    }

    internal static async Task SaveJobAsync(
        IJobRepository jobRepo, ExtractedJob extracted, JobDetail? details,
        SearchProfile profile, bool isEasyApply)
    {
        var job = new JobListing
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
            EasyApply = isEasyApply,
            Status = JobStatus.New,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await jobRepo.AddAsync(job, default);
    }
}
