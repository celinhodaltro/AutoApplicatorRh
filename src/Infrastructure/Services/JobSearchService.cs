using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using AutoApplicator.Infrastructure.Automation.Models;
using AutoApplicator.Infrastructure.Automation.Platforms;
using AutoApplicator.Infrastructure.Services.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
                    try { await profilePage.CloseAsync(); } catch { /* ignore */ }
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

            var extractedJobs = await adapter.ExtractListingsAsync(page);

            _logger.LogInformation("[Profile: {Name}] Extracted {Total} jobs on {Platform}", profile.Name, extractedJobs.Count, profile.Platform);

            using var scope = _scopeFactory.CreateScope();
            var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

            var savedCount = 0;
            var limit = Math.Min(extractedJobs.Count, maxJobs);
            for (var i = 0; i < limit; i++)
            {
                if (token.IsCancellationRequested) break;

                var extracted = extractedJobs[i];

                var isEasyApply = extracted.EasyApply;

                if (globalEasyApply && !isEasyApply)
                {
                    _logger.LogInformation("[Profile: {Name}] Skipping non-Easy-Apply: '{Title}'", profile.Name, extracted.Title);
                    continue;
                }

                var (details, updatedExtracted) = await FetchJobDetailsAsync(page, adapter, extracted, searchUrl, token);
                extracted = updatedExtracted;

                _logger.LogInformation("[Profile: {Name}] Saving job: '{Title}' at {Company} | EasyApply: {EasyApply} | Desc: {DescLen} chars",
                    profile.Name, extracted.Title, extracted.Company, isEasyApply, details?.Description?.Length ?? 0);

                UpdateProfileStatus(profile.Name, "Saving jobs...");
                await SaveJobAsync(jobRepo, extracted, details, profile, isEasyApply);
                savedCount++;
            }

            // Pagination: navigate to next pages using direct URL navigation
            var pageNum = 1;
            var totalExtracted = savedCount;

            while (!token.IsCancellationRequested && totalExtracted < maxJobs)
            {
                pageNum++;
                _logger.LogInformation("[Profile: {Name}] Page {PageNum}: URL = {Url}", profile.Name, pageNum, adapter.BuildSearchUrl(profile, pageNum));

                await adapter.NavigateToPageAsync(page, profile, pageNum);
                await Task.Delay(2000, token);

                var moreJobs = await adapter.ExtractListingsAsync(page);
                _logger.LogInformation("[Profile: {Name}] Page {PageNum}: found {Count} job(s)", profile.Name, pageNum, moreJobs.Count);

                if (moreJobs.Count == 0)
                {
                    _logger.LogInformation("[Profile: {Name}] No results on page {PageNum}, ending pagination", profile.Name, pageNum);
                    break;
                }

                var remaining = maxJobs - totalExtracted;
                var jobsToProcess = Math.Min(moreJobs.Count, remaining);

                for (var k = 0; k < jobsToProcess; k++)
                {
                    if (token.IsCancellationRequested) break;

                    var extracted = moreJobs[k];

                    var isEasyApply = extracted.EasyApply;

                    if (globalEasyApply && !isEasyApply)
                    {
                        _logger.LogInformation("[Profile: {Name}] Skipping non-Easy-Apply: '{Title}'", profile.Name, extracted.Title);
                        continue;
                    }

                    var (details, updatedExtracted) = await FetchJobDetailsAsync(page, adapter, extracted, searchUrl, token);
                    extracted = updatedExtracted;

                    _logger.LogInformation("[Profile: {Name}] Saving job: '{Title}' at {Company} | EasyApply: {EasyApply} | Desc: {DescLen} chars",
                        profile.Name, extracted.Title, extracted.Company, isEasyApply, details?.Description?.Length ?? 0);

                    UpdateProfileStatus(profile.Name, "Saving jobs...");
                    await SaveJobAsync(jobRepo, extracted, details, profile, isEasyApply);
                    totalExtracted++;
                }

                if (totalExtracted >= maxJobs)
                {
                    _logger.LogInformation("[Profile: {Name}] Reached max jobs limit ({MaxJobs}) on page {PageNum}", profile.Name, maxJobs, pageNum);
                    break;
                }

                if (moreJobs.Count < 25)
                {
                    _logger.LogInformation("[Profile: {Name}] Less than 25 results on page {PageNum}, assuming last page", profile.Name, pageNum);
                    break;
                }
            }

            _logger.LogInformation("[Profile: {Name}] Finished pagination: extracted {Total} job(s) across {Pages} page(s)",
                profile.Name, totalExtracted, pageNum);

            _logger.LogInformation("[Profile: {Name}] Saved {Count} job(s)", profile.Name, savedCount);

            UpdateProfileStatus(profile.Name, "Complete");
            return savedCount;
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

        const int checkIntervalMs = 2000;
        const int notifyIntervalMs = 60_000;

        var platformName = profile.Platform.ToString();
        var loginUrl = auth.LoginUrl;

        _notifications.Add(NotificationType.Warning,
            $"{platformName} Login Required",
            $"Login required for {platformName}. Please log in in the browser window.",
            $"Open {platformName}",
            loginUrl);

        UpdateStatus($"Waiting for {platformName} login...", 0, 0);
        _logger.LogWarning("Login required for {Platform}. Navigated to login URL: {LoginUrl}", platformName, loginUrl);

        await page.GoToAsync(loginUrl);

        var lastNotifyTime = DateTime.UtcNow;

        await Task.Delay(5000, CancellationToken.None);
        while (!token.IsCancellationRequested)
        {
            auth = await adapter.IsAuthenticatedAsync(page);

            if (auth.IsAuthenticated)
            {
                await _playwrightService.SaveCookiesAsync();

                _notifications.Add(NotificationType.Success,
                    $"{platformName} Login",
                    "Login successful! Continuing automation...");

                UpdateStatus($"{platformName} login OK!", 0, 0);
                _logger.LogInformation("Login detected for {Platform}. Continuing automation.", platformName);
                return;
            }

            if ((DateTime.UtcNow - lastNotifyTime).TotalMilliseconds >= notifyIntervalMs)
            {
                _notifications.Add(NotificationType.Warning,
                    $"{platformName} Login Required",
                    $"Still waiting for {platformName} login...",
                    $"Open {platformName}",
                    loginUrl);

                lastNotifyTime = DateTime.UtcNow;
            }

            await Task.Delay(checkIntervalMs, CancellationToken.None);
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
        catch
        {
            _logger.LogWarning("Failed to navigate back to search results");
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
