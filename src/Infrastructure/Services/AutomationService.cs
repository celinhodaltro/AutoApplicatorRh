using System.Diagnostics;
using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using AutoApplicator.Infrastructure.Automation.Models;
using AutoApplicator.Infrastructure.Automation.Platforms;
using AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;
using AutoApplicator.Infrastructure.Services.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Services;

public sealed class AutomationService : IAutomationStateService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlaywrightService _playwrightService;
    private readonly PlatformAdapterFactory _adapterFactory;
    private readonly NotificationService _notifications;
    private readonly ILogger<AutomationService> _logger;
    private readonly ExceptionHandlerService _exceptionHandler;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public AutomationStatusInfo CurrentStatus { get; private set; } = new();

    public event Action? StatusChanged;

    public AutomationService(
        IServiceScopeFactory scopeFactory,
        PlaywrightService playwrightService,
        PlatformAdapterFactory adapterFactory,
        NotificationService notifications,
        ILogger<AutomationService> logger,
        ExceptionHandlerService exceptionHandler)
    {
        _scopeFactory = scopeFactory;
        _playwrightService = playwrightService;
        _adapterFactory = adapterFactory;
        _notifications = notifications;
        _logger = logger;
        _exceptionHandler = exceptionHandler;
    }

    public async Task StartAsync(AutomationMode mode = AutomationMode.Search, bool globalEasyApply = false,
        int maxSearchJobs = 25, int maxApplyJobs = 10, int maxFullJobs = 20)
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

        var modeLabel = mode switch
        {
            AutomationMode.Search => "SEARCH",
            AutomationMode.Apply => "APPLY",
            AutomationMode.Full => "FULL (search + apply)",
            _ => "UNKNOWN"
        };

        _logger.LogInformation("===== AUTOMATION STARTED [{Mode}] =====", modeLabel);
        UpdateStatus($"Starting {modeLabel}...", 0, 0);

        if (globalEasyApply)
            _logger.LogInformation("Easy Apply filter ON — skipping non-Easy-Apply jobs");

        try
        {
            if (mode is AutomationMode.Search or AutomationMode.Full)
            {
                if (mode == AutomationMode.Full)
                {
                    await RunFullAsync(token, globalEasyApply, maxFullJobs);
                }
                else
                {
                    var searchLimit = maxSearchJobs;
                    await RunSearchAsync(token, globalEasyApply, searchLimit);
                }
            }

            if (mode is AutomationMode.Apply && !token.IsCancellationRequested)
            {
                var applyLimit = maxApplyJobs;
                await RunApplyAsync(token, applyLimit);
            }

            stopwatch.Stop();
            _logger.LogInformation("===== AUTOMATION COMPLETED [{Mode}] in {Elapsed} =====", modeLabel, stopwatch.Elapsed);
            _notifications.Add(NotificationType.Success, $"{modeLabel} Complete", $"Finished in {stopwatch.Elapsed.TotalMinutes:F1} min.");
            UpdateStatus("Automation completed.", 0, 0);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning("===== AUTOMATION CANCELLED [{Mode}] after {Elapsed} =====", modeLabel, stopwatch.Elapsed);
            _notifications.Add(NotificationType.Warning, "Automation Cancelled", $"Stopped after {stopwatch.Elapsed.TotalMinutes:F1} min.");
            UpdateStatus("Cancelled.", 0, 0);
        }
        catch (AutomationException autoEx)
        {
            stopwatch.Stop();
            _exceptionHandler.Handle(autoEx);
            UpdateStatus(autoEx.UserMessage, 0, 0);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "===== AUTOMATION FAILED [{Mode}] after {Elapsed} =====", modeLabel, stopwatch.Elapsed);
            if (!_exceptionHandler.TryHandle(ex))
                _notifications.Add(NotificationType.Error, $"{modeLabel} Failed", ex.Message);
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
        _notifications.Add(NotificationType.Warning, "Automation", "Stopping...");
        UpdateStatus("Stopping...", 0, 0);
    }

    private async Task RunFullAsync(CancellationToken token, bool globalEasyApply, int maxJobs)
    {
        List<SearchProfile> profiles;
        using (var scope = _scopeFactory.CreateScope())
        {
            var profileRepo = scope.ServiceProvider.GetRequiredService<IProfileRepository>();
            profiles = (await profileRepo.GetEnabledProfilesAsync()).ToList();
        }

        _logger.LogInformation("Found {ProfileCount} enabled profile(s) for Full mode", profiles.Count);

        if (profiles.Count == 0)
            throw new NoEnabledProfilesException();

        await _playwrightService.InitializeAsync();

        UpdateStatus($"Full: processing {profiles.Count} profile(s) in parallel...", 0, profiles.Count);

        var totalFound = 0;
        var totalApplied = 0;
        var totalErrors = 0;

        var tasks = profiles.Select(profile =>
            Task.Run(async () =>
            {
                var profilePage = await _playwrightService.CreateNewPageAsync();
                try
                {
                    // === SEARCH PHASE ===
                    var adapter = _adapterFactory.Create(profile.Platform);

                    _logger.LogInformation("[Profile: {Name}] Starting Full search on {Platform}", profile.Name, profile.Platform);

                    var searchUrl = await NavigateToSearchUrlAsync(adapter, profile, profilePage, token);
                    await CheckAuthenticationAsync(adapter, profilePage, profile);

                    var extractedJobs = await adapter.ExtractListingsAsync(profilePage);

                    _logger.LogInformation("[Profile: {Name}] Extracted {Count} jobs", profile.Name, extractedJobs.Count);

                    var limit = Math.Min(extractedJobs.Count, maxJobs);
                    var profileApplied = 0;
                    var profileErrors = 0;

                    // Process first page
                    for (var j = 0; j < limit; j++)
                    {
                        if (token.IsCancellationRequested) break;

                        var extracted = extractedJobs[j];
                        var isEasyApply = extracted.EasyApply;

                        if (globalEasyApply && !isEasyApply)
                        {
                            _logger.LogInformation("[Profile: {Name}] Skipping non-Easy-Apply: '{Title}'", profile.Name, extracted.Title);
                            continue;
                        }

                        var (details, updatedExtracted) = await FetchJobDetailsAsync(profilePage, adapter, extracted, searchUrl, token);
                        extracted = updatedExtracted;

                        if (isEasyApply)
                        {
                            // Apply in a new page
                            Microsoft.Playwright.IPage? applyPage = null;
                            try
                            {
                                applyPage = await _playwrightService.CreateNewPageAsync();

                                _logger.LogInformation("[Profile: {Name}] Applying to '{Title}' at {Company}", profile.Name, extracted.Title, extracted.Company);

                                await applyPage.GotoAsync(extracted.Url, new() { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
                                await Task.Delay(3000, token);

                                var jobListing = new JobListing
                                {
                                    Id = Guid.NewGuid(),
                                    ExternalId = extracted.ExternalId,
                                    Platform = profile.Platform,
                                    ProfileId = profile.Id,
                                    Url = extracted.Url,
                                    Title = extracted.Title,
                                    Company = extracted.Company,
                                    Location = extracted.Location,
                                    EasyApply = true,
                                    Status = JobStatus.New,
                                    CreatedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow
                                };

                                using var scope = _scopeFactory.CreateScope();
                                var linkedInApplicator = scope.ServiceProvider.GetRequiredService<LinkedInApplicator>();
                                var result = await linkedInApplicator.ApplyAsync(applyPage, jobListing);

                                if (result.Success)
                                {
                                    jobListing.Status = JobStatus.Applied;
                                    jobListing.AppliedAt = DateTime.UtcNow;
                                    jobListing.ApplicationAnswers = result.AnswersUsed;
                                    _logger.LogInformation("[Profile: {Name}] ✅ Applied: '{Title}'", profile.Name, extracted.Title);
                                    Interlocked.Increment(ref totalApplied);
                                    profileApplied++;
                                }
                                else if (result.NeedsManualIntervention)
                                {
                                    jobListing.Status = JobStatus.Pending;
                                    jobListing.UserNotes = result.ErrorMessage;
                                    _logger.LogInformation("[Profile: {Name}] ⏭️ Pending: '{Title}' (needs answers)", profile.Name, extracted.Title);
                                }
                                else
                                {
                                    jobListing.Status = JobStatus.Error;
                                    jobListing.UserNotes = result.ErrorMessage;
                                    _logger.LogWarning("[Profile: {Name}] ❌ Failed: '{Title}': {Error}", profile.Name, extracted.Title, result.ErrorMessage);
                                    Interlocked.Increment(ref totalErrors);
                                    profileErrors++;
                                }

                                using var saveScope = _scopeFactory.CreateScope();
                                var jobRepo = saveScope.ServiceProvider.GetRequiredService<IJobRepository>();
                                await jobRepo.AddAsync(jobListing, token);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref totalErrors);
                                _logger.LogError(ex, "[Profile: {Name}] Error applying to '{Title}'", profile.Name, extracted.Title);
                            }
                            finally
                            {
                                if (applyPage is not null)
                                {
                                    try { await applyPage.CloseAsync(); } catch { /* ignore */ }
                                }
                            }
                        }
                        else
                        {
                            // Save as New
                            _logger.LogInformation("[Profile: {Name}] Saving non-Easy-Apply job: '{Title}' at {Company}", profile.Name, extracted.Title, extracted.Company);
                            using var scope = _scopeFactory.CreateScope();
                            var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                            await SaveJobAsync(jobRepo, extracted, details, profile, false);
                        }

                        Interlocked.Increment(ref totalFound);
                    }

                    // Pagination: navigate to next pages
                    var pageNum = 1;
                    var totalExtracted = limit;

                    while (!token.IsCancellationRequested && totalExtracted < maxJobs)
                    {
                        pageNum++;
                        _logger.LogInformation("[Profile: {Name}] Page {PageNum} for pagination", profile.Name, pageNum);

                        await adapter.NavigateToPageAsync(profilePage, profile, pageNum);
                        await Task.Delay(2000, token);

                        var moreJobs = await adapter.ExtractListingsAsync(profilePage);
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

                            var (details, updatedExtracted) = await FetchJobDetailsAsync(profilePage, adapter, extracted, searchUrl, token);
                            extracted = updatedExtracted;

                            if (isEasyApply)
                            {
                                // Apply in a new page
                                Microsoft.Playwright.IPage? applyPage = null;
                                try
                                {
                                    applyPage = await _playwrightService.CreateNewPageAsync();

                                    _logger.LogInformation("[Profile: {Name}] Applying to '{Title}' at {Company}", profile.Name, extracted.Title, extracted.Company);

                                    await applyPage.GotoAsync(extracted.Url, new() { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
                                    await Task.Delay(3000, token);

                                    var jobListing = new JobListing
                                    {
                                        Id = Guid.NewGuid(),
                                        ExternalId = extracted.ExternalId,
                                        Platform = profile.Platform,
                                        ProfileId = profile.Id,
                                        Url = extracted.Url,
                                        Title = extracted.Title,
                                        Company = extracted.Company,
                                        Location = extracted.Location,
                                        EasyApply = true,
                                        Status = JobStatus.New,
                                        CreatedAt = DateTime.UtcNow,
                                        UpdatedAt = DateTime.UtcNow
                                    };

                                    using var scope = _scopeFactory.CreateScope();
                                    var linkedInApplicator = scope.ServiceProvider.GetRequiredService<LinkedInApplicator>();
                                    var result = await linkedInApplicator.ApplyAsync(applyPage, jobListing);

                                    if (result.Success)
                                    {
                                        jobListing.Status = JobStatus.Applied;
                                        jobListing.AppliedAt = DateTime.UtcNow;
                                        jobListing.ApplicationAnswers = result.AnswersUsed;
                                        _logger.LogInformation("[Profile: {Name}] ✅ Applied: '{Title}'", profile.Name, extracted.Title);
                                        Interlocked.Increment(ref totalApplied);
                                        profileApplied++;
                                    }
                                    else if (result.NeedsManualIntervention)
                                    {
                                        jobListing.Status = JobStatus.Pending;
                                        jobListing.UserNotes = result.ErrorMessage;
                                        _logger.LogInformation("[Profile: {Name}] ⏭️ Pending: '{Title}' (needs answers)", profile.Name, extracted.Title);
                                    }
                                    else
                                    {
                                        jobListing.Status = JobStatus.Error;
                                        jobListing.UserNotes = result.ErrorMessage;
                                        _logger.LogWarning("[Profile: {Name}] ❌ Failed: '{Title}': {Error}", profile.Name, extracted.Title, result.ErrorMessage);
                                        Interlocked.Increment(ref totalErrors);
                                        profileErrors++;
                                    }

                                    using var saveScope = _scopeFactory.CreateScope();
                                    var jobRepo = saveScope.ServiceProvider.GetRequiredService<IJobRepository>();
                                    await jobRepo.AddAsync(jobListing, token);
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception ex)
                                {
                                    Interlocked.Increment(ref totalErrors);
                                    _logger.LogError(ex, "[Profile: {Name}] Error applying to '{Title}'", profile.Name, extracted.Title);
                                }
                                finally
                                {
                                    if (applyPage is not null)
                                    {
                                        try { await applyPage.CloseAsync(); } catch { /* ignore */ }
                                    }
                                }
                            }
                            else
                            {
                                // Save as New
                                _logger.LogInformation("[Profile: {Name}] Saving non-Easy-Apply job: '{Title}' at {Company}", profile.Name, extracted.Title, extracted.Company);
                                using var scope = _scopeFactory.CreateScope();
                                var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                                await SaveJobAsync(jobRepo, extracted, details, profile, false);
                            }

                            totalExtracted++;
                            Interlocked.Increment(ref totalFound);
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

                    _logger.LogInformation("[Profile: {Name}] Full processing complete: {Applied} applied, {Errors} errors",
                        profile.Name, profileApplied, profileErrors);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Profile: {Name}] Full processing failed", profile.Name);
                    _notifications.Add(NotificationType.Error, $"{profile.Platform} Error", $"{profile.Name}: {ex.Message}");
                }
                finally
                {
                    try { await profilePage.CloseAsync(); } catch { /* ignore */ }
                }
            }, token));

        await Task.WhenAll(tasks);

        _logger.LogInformation("Full automation complete: {Found} jobs found, {Applied} applied, {Errors} errors across {Profiles} profile(s)",
            totalFound, totalApplied, totalErrors, profiles.Count);

        if (totalApplied > 0)
            _notifications.Add(NotificationType.Success, "Full Automation Complete", $"Applied to {totalApplied} job(s) across {profiles.Count} profiles.", "View Jobs", "/jobs");
        if (totalErrors > 0)
            _notifications.Add(NotificationType.Warning, "Full Automation", $"{totalErrors} error(s) occurred.");
    }

    private async Task RunSearchAsync(CancellationToken token, bool globalEasyApply, int maxJobs = 25)
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
    }

    private async Task RunApplyAsync(CancellationToken token, int maxJobs = 10)
    {
        var jobsToApply = await GetJobsToApplyAsync();

        _logger.LogInformation("Found {Count} job(s) to apply", jobsToApply.Count);

        if (jobsToApply.Count == 0)
            throw new NoApprovedJobsException();

        await _playwrightService.InitializeAsync();

        UpdateStatus($"Applying to up to {Math.Min(jobsToApply.Count, maxJobs)} job(s) in parallel...", 0, jobsToApply.Count);

        var limit = Math.Min(jobsToApply.Count, maxJobs);
        var jobsToProcess = jobsToApply.Take(limit).ToList();

        var appliedCount = 0;
        var pendingCount = 0;
        var errorCount = 0;
        var processedCount = 0;

        var tasks = jobsToProcess.Select(job =>
            Task.Run(async () =>
            {
                Microsoft.Playwright.IPage? applyPage = null;
                try
                {
                    applyPage = await _playwrightService.CreateNewPageAsync();

                    var result = await ProcessJobApplicationAsync(applyPage, job, token);
                    await UpdateJobAfterApplyAsync(job, result, token);

                    if (result.Success) Interlocked.Increment(ref appliedCount);
                    else if (result.NeedsManualIntervention) Interlocked.Increment(ref pendingCount);
                    else Interlocked.Increment(ref errorCount);

                    var current = Interlocked.Increment(ref processedCount);
                    UpdateStatus($"Applying: {job.Title} ({current}/{jobsToProcess.Count})", current, jobsToProcess.Count);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    _logger.LogError(ex, "Error applying to '{Title}'", job.Title);
                }
                finally
                {
                    if (applyPage is not null)
                    {
                        try { await applyPage.CloseAsync(); } catch { /* ignore */ }
                    }
                }
            }, token));

        await Task.WhenAll(tasks);

        _logger.LogInformation("Apply complete: {Applied} applied, {Pending} pending, {Errors} errors, {Total} total",
            appliedCount, pendingCount, errorCount, jobsToProcess.Count);

        if (appliedCount > 0)
            _notifications.Add(NotificationType.Success, "Apply Complete", $"Applied to {appliedCount} job(s).", "View Jobs", "/jobs");
        if (pendingCount > 0)
            _notifications.Add(NotificationType.Warning, "Pending Answers", $"{pendingCount} job(s) need answers configured.", "Questions", "/questions");
        if (appliedCount == 0 && pendingCount == 0)
            _notifications.Add(NotificationType.Warning, "Apply Complete", "No jobs were applied.");
    }

    private async Task<List<JobListing>> GetJobsToApplyAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var allJobs = (await jobRepo.GetAllAsync(default)).ToList();
        return allJobs.Where(j => j.Status is JobStatus.New or JobStatus.Approved).ToList();
    }

    private async Task<ApplyResult> ProcessJobApplicationAsync(Microsoft.Playwright.IPage page, JobListing job, CancellationToken token)
    {
        await page.GotoAsync(job.Url, new() { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
        await Task.Delay(3000, token);

        if (job.Platform == PlatformType.LinkedIn)
        {
            using var scope = _scopeFactory.CreateScope();
            var linkedInApplicator = scope.ServiceProvider.GetRequiredService<LinkedInApplicator>();
            return await linkedInApplicator.ApplyAsync(page, job);
        }

        _logger.LogWarning("Apply not yet supported for {Platform}", job.Platform);
        return new ApplyResult(false, $"Apply not supported for {job.Platform}");
    }

    private async Task UpdateJobAfterApplyAsync(JobListing job, ApplyResult result, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var dbJob = await jobRepo.GetByIdAsync(job.Id, default);
        if (dbJob is null) return;

        if (result.Success)
        {
            dbJob.Status = JobStatus.Applied;
            dbJob.AppliedAt = DateTime.UtcNow;
            dbJob.ApplicationAnswers = result.AnswersUsed;
            _logger.LogInformation("✅ Applied: '{Title}' at {Company}", job.Title, job.Company);
            _notifications.Add(NotificationType.Success, "Applied", $"{job.Title} at {job.Company}");
        }
        else if (result.NeedsManualIntervention)
        {
            dbJob.Status = JobStatus.Pending;
            dbJob.UserNotes = result.ErrorMessage;
            _logger.LogInformation("⏭️ Saved for later: '{Title}' (needs answers)", job.Title);
            _notifications.Add(NotificationType.Warning, "Pending", $"{job.Title} — configure answers in Questions tab");
        }
        else
        {
            dbJob.Status = JobStatus.Error;
            dbJob.UserNotes = result.ErrorMessage;
            _logger.LogWarning("❌ Failed: '{Title}': {Error}", job.Title, result.ErrorMessage);
        }

        dbJob.UpdatedAt = DateTime.UtcNow;
        await jobRepo.UpdateAsync(dbJob, default);
    }

    private async Task<int> SearchProfileAsync(
        SearchProfile profile, Microsoft.Playwright.IPage page,
        bool globalEasyApply, int maxJobs, CancellationToken token)
    {
        var adapter = _adapterFactory.Create(profile.Platform);

        _logger.LogInformation("[Profile: {Name}] Searching on {Platform}", profile.Name, profile.Platform);
        UpdateStatus($"Searching: {profile.Name}...", 0, 0);

        try
        {
            var searchUrl = await NavigateToSearchUrlAsync(adapter, profile, page, token);
            await CheckAuthenticationAsync(adapter, page, profile);

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

                // No more results → end pagination
                if (moreJobs.Count == 0)
                {
                    _logger.LogInformation("[Profile: {Name}] No results on page {PageNum}, ending pagination", profile.Name, pageNum);
                    break;
                }

                // Calculate how many more jobs we can process on this page
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

                    await SaveJobAsync(jobRepo, extracted, details, profile, isEasyApply);
                    totalExtracted++;
                }

                // Stop pagination if we processed enough jobs
                if (totalExtracted >= maxJobs)
                {
                    _logger.LogInformation("[Profile: {Name}] Reached max jobs limit ({MaxJobs}) on page {PageNum}", profile.Name, maxJobs, pageNum);
                    break;
                }

                // Less than 25 results = likely last page
                if (moreJobs.Count < 25)
                {
                    _logger.LogInformation("[Profile: {Name}] Less than 25 results on page {PageNum}, assuming last page", profile.Name, pageNum);
                    break;
                }
            }

            _logger.LogInformation("[Profile: {Name}] Finished pagination: extracted {Total} job(s) across {Pages} page(s)",
                profile.Name, totalExtracted, pageNum);

            _logger.LogInformation("[Profile: {Name}] Saved {Count} job(s)", profile.Name, savedCount);
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

    private async Task<string> NavigateToSearchUrlAsync(
        IPlatformAdapter adapter, SearchProfile profile, Microsoft.Playwright.IPage page,
        CancellationToken token)
    {
        var searchUrl = adapter.BuildSearchUrl(profile);
        _logger.LogInformation("[Profile: {Name}] URL: {SearchUrl}", profile.Name, searchUrl);

        await page.GotoAsync(searchUrl, new() { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
        await Task.Delay(3000, token);

        return searchUrl;
    }

    private static async Task CheckAuthenticationAsync(IPlatformAdapter adapter, Microsoft.Playwright.IPage page, SearchProfile profile)
    {
        var auth = await adapter.IsAuthenticatedAsync(page);
        if (!auth.IsAuthenticated)
            throw new LoginRequiredException(profile.Platform.ToString(), auth.LoginUrl);
    }

    private async Task<(JobDetail? Details, ExtractedJob UpdatedExtracted)> FetchJobDetailsAsync(
        Microsoft.Playwright.IPage page, IPlatformAdapter adapter, ExtractedJob extracted,
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
            await page.GotoAsync(searchUrl, new() { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
            await Task.Delay(2000, token);
        }
        catch
        {
            _logger.LogWarning("Failed to navigate back to search results");
        }

        return (details, extracted);
    }

    private static async Task SaveJobAsync(
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

    private void UpdateStatus(string message, int current, int total)
    {
        CurrentStatus = new AutomationStatusInfo(message, current, total);
        StatusChanged?.Invoke();
    }
}
