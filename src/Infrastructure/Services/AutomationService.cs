using System.Diagnostics;
using System.Threading.Channels;
using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
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
        var jobChannel = Channel.CreateBounded<ExtractedJob>(new BoundedChannelOptions(20)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        var searchTask = RunFullSearchAsync(jobChannel.Writer, token, globalEasyApply, maxJobs);
        var applyTask = RunFullApplyAsync(jobChannel.Reader, token);

        await Task.WhenAll(searchTask, applyTask);
    }

    private async Task RunFullSearchAsync(ChannelWriter<ExtractedJob> writer, CancellationToken token, bool globalEasyApply, int maxJobs)
    {
        List<SearchProfile> profiles;
        using (var scope = _scopeFactory.CreateScope())
        {
            var profileRepo = scope.ServiceProvider.GetRequiredService<IProfileRepository>();
            profiles = (await profileRepo.GetEnabledProfilesAsync()).ToList();
        }

        _logger.LogInformation("Found {ProfileCount} enabled profile(s) for Full Search", profiles.Count);

        if (profiles.Count == 0)
            throw new NoEnabledProfilesException();

        await _playwrightService.InitializeAsync();
        var page = _playwrightService.GetPage()
            ?? throw new InvalidOperationException("Playwright page not available");

        UpdateStatus($"Full Search: searching {profiles.Count} profile(s)...", 0, profiles.Count);

        var totalFound = 0;
        for (var i = 0; i < profiles.Count; i++)
        {
            if (token.IsCancellationRequested) break;

            var profile = profiles[i];
            var adapter = _adapterFactory.Create(profile.Platform);

            _logger.LogInformation("[Full Search] Searching '{ProfileName}' on {Platform}", profile.Name, profile.Platform);
            UpdateStatus($"Full Search: {profile.Name} on {profile.Platform}...", i + 1, profiles.Count);

            try
            {
                var searchUrl = await NavigateToSearchUrlAsync(adapter, profile, page, i + 1, profiles.Count, token);
                await CheckAuthenticationAsync(adapter, page, profile);

                UpdateStatus($"Full Search: extracting listings for {profile.Name}...", i + 1, profiles.Count);
                var extractedJobs = await adapter.ExtractListingsAsync(page);

                _logger.LogInformation("[Full Search] Extracted {Total} jobs on {Platform} for '{Name}'", extractedJobs.Count, profile.Platform, profile.Name);

                var limit = Math.Min(extractedJobs.Count, maxJobs);
                var totalExtracted = 0;
                for (var j = 0; j < limit; j++)
                {
                    if (token.IsCancellationRequested) break;

                    var extracted = extractedJobs[j];

                    UpdateStatus($"Full Search: checking {extracted.Title} ({j + 1}/{limit})", i + 1, profiles.Count);

                    var isEasyApply = extracted.EasyApply;

                    if (globalEasyApply && !isEasyApply)
                    {
                        _logger.LogInformation("[Full Search] Skipping non-Easy-Apply: '{Title}'", extracted.Title);
                        continue;
                    }

                    var (details, updatedExtracted) = await FetchJobDetailsAsync(page, adapter, extracted, searchUrl, token);
                    extracted = updatedExtracted;

                    if (isEasyApply)
                    {
                        // Send to apply queue via channel
                        _logger.LogInformation("[Full Search] Found Easy Apply job '{Title}' at {Company}, sending to apply queue", extracted.Title, extracted.Company);
                        await writer.WriteAsync(extracted, token);
                        totalFound++;
                        totalExtracted++;
                    }
                    else
                    {
                        // Save as New (same as regular search)
                        _logger.LogInformation("[Full Search] Saving non-Easy-Apply job: '{Title}' at {Company}", extracted.Title, extracted.Company);
                        using var scope = _scopeFactory.CreateScope();
                        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                        await SaveJobAsync(jobRepo, extracted, details, profile, false);
                        totalFound++;
                        totalExtracted++;
                    }
                }

                // Pagination: navigate to next pages using direct URL navigation
                var pageNum = 1;

                while (!token.IsCancellationRequested && totalExtracted < maxJobs)
                {
                    pageNum++;
                    _logger.LogInformation("[Full Search] Page {PageNum}: URL = {Url}", pageNum, adapter.BuildSearchUrl(profile, pageNum));

                    await adapter.NavigateToPageAsync(page, profile, pageNum);
                    await Task.Delay(2000, token);

                    var moreJobs = await adapter.ExtractListingsAsync(page);
                    _logger.LogInformation("[Full Search] Page {PageNum}: found {Count} job(s) for '{ProfileName}'", pageNum, moreJobs.Count, profile.Name);

                    // No more results → end pagination
                    if (moreJobs.Count == 0)
                    {
                        _logger.LogInformation("[Full Search] No results on page {PageNum}, ending pagination", pageNum);
                        break;
                    }

                    // Calculate how many more jobs we can process on this page
                    var remaining = maxJobs - totalExtracted;
                    var jobsToProcess = Math.Min(moreJobs.Count, remaining);

                    for (var j = 0; j < jobsToProcess; j++)
                    {
                        if (token.IsCancellationRequested) break;

                        var extracted = moreJobs[j];
                        UpdateStatus($"Full Search: checking {extracted.Title} (page {pageNum})", i + 1, profiles.Count);

                        var isEasyApply = extracted.EasyApply;

                        if (globalEasyApply && !isEasyApply)
                        {
                            _logger.LogInformation("[Full Search] Skipping non-Easy-Apply: '{Title}'", extracted.Title);
                            continue;
                        }

                        var (details, updatedExtracted) = await FetchJobDetailsAsync(page, adapter, extracted, searchUrl, token);
                        extracted = updatedExtracted;

                        if (isEasyApply)
                        {
                            _logger.LogInformation("[Full Search] Found Easy Apply job '{Title}' at {Company}, sending to apply queue", extracted.Title, extracted.Company);
                            await writer.WriteAsync(extracted, token);
                        }
                        else
                        {
                            _logger.LogInformation("[Full Search] Saving non-Easy-Apply job: '{Title}' at {Company}", extracted.Title, extracted.Company);
                            using var saveScope = _scopeFactory.CreateScope();
                            var jobRepo = saveScope.ServiceProvider.GetRequiredService<IJobRepository>();
                            await SaveJobAsync(jobRepo, extracted, details, profile, false);
                        }

                        totalExtracted++;
                    }

                    // Stop pagination if we processed enough jobs
                    if (totalExtracted >= maxJobs)
                    {
                        _logger.LogInformation("[Full Search] Reached max jobs limit ({MaxJobs}) on page {PageNum}", maxJobs, pageNum);
                        break;
                    }

                    // Less than 25 results = likely last page
                    if (moreJobs.Count < 25)
                    {
                        _logger.LogInformation("[Full Search] Less than 25 results on page {PageNum}, assuming last page for '{ProfileName}'", pageNum, profile.Name);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Full Search] Failed for '{Name}'", profile.Name);
                _notifications.Add(NotificationType.Error, $"{profile.Platform} Error", $"{profile.Name}: {ex.Message}");
                UpdateStatus($"Full Search error: {profile.Name}", i + 1, profiles.Count);
            }
        }

        writer.Complete();
        _logger.LogInformation("Full Search complete: {TotalJobs} job(s) sent to apply queue", totalFound);
    }

    private async Task RunFullApplyAsync(ChannelReader<ExtractedJob> reader, CancellationToken token)
    {
        var appliedCount = 0;
        var pendingCount = 0;
        var errorCount = 0;
        var totalProcessed = 0;

        await foreach (var extracted in reader.ReadAllAsync(token))
        {
            totalProcessed++;
            UpdateStatus($"Full Apply: processing {extracted.Title} ({totalProcessed})", totalProcessed, 0);

            Microsoft.Playwright.IPage? tab = null;
            try
            {
                // Create new tab
                tab = await _playwrightService.CreateNewPageAsync();
                _logger.LogInformation("[Full Apply] Processing job '{Title}' at {Company} in new tab", extracted.Title, extracted.Company);

                // Navigate to job URL
                await tab.GotoAsync(extracted.Url, new() { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
                await Task.Delay(3000, token);

                // Convert ExtractedJob to JobListing for the applicator
                var jobListing = new JobListing
                {
                    Id = Guid.NewGuid(),
                    ExternalId = extracted.ExternalId,
                    Platform = PlatformType.LinkedIn,
                    Url = extracted.Url,
                    Title = extracted.Title,
                    Company = extracted.Company,
                    Location = extracted.Location,
                    EasyApply = true,
                    Status = JobStatus.New,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Apply using LinkedInApplicator
                using var scope = _scopeFactory.CreateScope();
                var linkedInApplicator = scope.ServiceProvider.GetRequiredService<LinkedInApplicator>();
                var result = await linkedInApplicator.ApplyAsync(tab, jobListing);

                if (result.Success)
                {
                    appliedCount++;
                    jobListing.Status = JobStatus.Applied;
                    jobListing.AppliedAt = DateTime.UtcNow;
                    jobListing.ApplicationAnswers = result.AnswersUsed;
                    _logger.LogInformation("[Full Apply] ✅ Applied: '{Title}' at {Company}", extracted.Title, extracted.Company);
                    _notifications.Add(NotificationType.Success, "Applied", $"{extracted.Title} at {extracted.Company}");
                }
                else if (result.NeedsManualIntervention)
                {
                    pendingCount++;
                    jobListing.Status = JobStatus.Pending;
                    jobListing.UserNotes = result.ErrorMessage;
                    _logger.LogInformation("[Full Apply] ⏭️ Pending: '{Title}' (needs answers)", extracted.Title);
                    _notifications.Add(NotificationType.Warning, "Pending", $"{extracted.Title} — configure answers");
                }
                else
                {
                    errorCount++;
                    jobListing.Status = JobStatus.Error;
                    jobListing.UserNotes = result.ErrorMessage;
                    _logger.LogWarning("[Full Apply] ❌ Failed: '{Title}' at {Company}: {Error}", extracted.Title, extracted.Company, result.ErrorMessage);
                }

                // Save job result
                using var saveScope = _scopeFactory.CreateScope();
                var jobRepo = saveScope.ServiceProvider.GetRequiredService<IJobRepository>();
                await jobRepo.AddAsync(jobListing, token);

                // Delay before next tab to avoid overloading LinkedIn
                if (!token.IsCancellationRequested)
                    await Task.Delay(2000 + Random.Shared.Next(0, 2000), token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "[Full Apply] Error processing '{Title}'", extracted.Title);
            }
            finally
            {
                if (tab is not null)
                {
                    try { await tab.CloseAsync(); } catch { /* ignore close error */ }
                }
            }
        }

        _logger.LogInformation("Full Apply complete: {Applied} applied, {Pending} pending, {Errors} errors, {Total} total",
            appliedCount, pendingCount, errorCount, totalProcessed);

        if (appliedCount > 0)
            _notifications.Add(NotificationType.Success, "Full Apply Complete", $"Applied to {appliedCount} job(s).", "View Jobs", "/jobs");
        if (pendingCount > 0)
            _notifications.Add(NotificationType.Warning, "Pending Answers", $"{pendingCount} job(s) need answers configured.", "Questions", "/questions");
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
        var page = _playwrightService.GetPage()
            ?? throw new InvalidOperationException("Playwright page not available");

        UpdateStatus($"Searching {profiles.Count} profile(s)...", 0, profiles.Count);

        var totalFound = 0;
        for (var i = 0; i < profiles.Count; i++)
        {
            if (token.IsCancellationRequested) break;
            var result = await SearchProfileAsync(profiles[i], page, i + 1, profiles.Count, globalEasyApply, maxJobs, token);
            totalFound += result;
        }

        _logger.LogInformation("Search complete: {TotalJobs} job(s) found across {Profiles} profile(s)", totalFound, profiles.Count);

        if (totalFound > 0)
            _notifications.Add(NotificationType.Success, "Search Complete", $"Found {totalFound} job(s).", "View Jobs", "/jobs");
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
        var page = _playwrightService.GetPage()
            ?? throw new InvalidOperationException("Playwright page not available");

        UpdateStatus($"Applying to {jobsToApply.Count} job(s)...", 0, jobsToApply.Count);

        var appliedCount = 0;
        var pendingCount = 0;

        var limit = Math.Min(jobsToApply.Count, maxJobs);
        for (var i = 0; i < limit; i++)
        {
            if (token.IsCancellationRequested) break;
            var job = jobsToApply[i];

            UpdateStatus($"Applying: {job.Title} ({i + 1}/{jobsToApply.Count})", i + 1, jobsToApply.Count);
            _logger.LogInformation("[{Current}/{Total}] Applying to '{Title}' at {Company}", i + 1, jobsToApply.Count, job.Title, job.Company);

            try
            {
                var result = await ProcessJobApplicationAsync(page, job, i + 1, jobsToApply.Count, token);
                await UpdateJobAfterApplyAsync(job, result, i + 1, jobsToApply.Count);

                if (result.Success) appliedCount++;
                else if (result.NeedsManualIntervention) pendingCount++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Current}/{Total}] Error applying to '{Title}'", i + 1, jobsToApply.Count, job.Title);
            }
        }

        _logger.LogInformation("Apply complete: {Applied} applied, {Pending} pending, {Total} total", appliedCount, pendingCount, jobsToApply.Count);

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

    private async Task<ApplyResult> ProcessJobApplicationAsync(Microsoft.Playwright.IPage page, JobListing job, int current, int total, CancellationToken token)
    {
        await page.GotoAsync(job.Url, new() { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
        await Task.Delay(3000, token);

        if (job.Platform == PlatformType.LinkedIn)
        {
            using var scope = _scopeFactory.CreateScope();
            var linkedInApplicator = scope.ServiceProvider.GetRequiredService<LinkedInApplicator>();
            return await linkedInApplicator.ApplyAsync(page, job);
        }

        _logger.LogWarning("[{Current}/{Total}] Apply not yet supported for {Platform}", current, total, job.Platform);
        return new ApplyResult(false, $"Apply not supported for {job.Platform}");
    }

    private async Task UpdateJobAfterApplyAsync(JobListing job, ApplyResult result, int current, int total)
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
            _logger.LogInformation("[{Current}/{Total}] ✅ Applied: '{Title}'", current, total, job.Title);
            _notifications.Add(NotificationType.Success, "Applied", $"{job.Title} at {job.Company}");
        }
        else if (result.NeedsManualIntervention)
        {
            dbJob.Status = JobStatus.Pending;
            dbJob.UserNotes = result.ErrorMessage;
            _logger.LogInformation("[{Current}/{Total}] ⏭️ Saved for later: '{Title}' (needs answers)", current, total, job.Title);
            _notifications.Add(NotificationType.Warning, "Pending", $"{job.Title} — configure answers in Questions tab");
        }
        else
        {
            dbJob.Status = JobStatus.Error;
            dbJob.UserNotes = result.ErrorMessage;
            _logger.LogWarning("[{Current}/{Total}] ❌ Failed: '{Title}': {Error}", current, total, job.Title, result.ErrorMessage);
        }

        dbJob.UpdatedAt = DateTime.UtcNow;
        await jobRepo.UpdateAsync(dbJob, default);
    }

    private async Task<int> SearchProfileAsync(
        SearchProfile profile, Microsoft.Playwright.IPage page,
        int current, int total, bool globalEasyApply, int maxJobs, CancellationToken token)
    {
        var adapter = _adapterFactory.Create(profile.Platform);

        _logger.LogInformation("[{Current}/{Total}] Searching '{ProfileName}' on {Platform}", current, total, profile.Name, profile.Platform);
        UpdateStatus($"Searching {profile.Name} on {profile.Platform}...", current, total);

        try
        {
            var searchUrl = await NavigateToSearchUrlAsync(adapter, profile, page, current, total, token);
            await CheckAuthenticationAsync(adapter, page, profile);

            UpdateStatus($"Extracting listings for {profile.Name}...", current, total);
            var extractedJobs = await adapter.ExtractListingsAsync(page);

            _logger.LogInformation("[{Current}/{Total}] Extracted {Total} jobs on {Platform} for '{Name}'", current, total, extractedJobs.Count, profile.Platform, profile.Name);

            using var scope = _scopeFactory.CreateScope();
            var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

            var savedCount = 0;
            var limit = Math.Min(extractedJobs.Count, maxJobs);
            for (var i = 0; i < limit; i++)
            {
                if (token.IsCancellationRequested) break;

                var extracted = extractedJobs[i];

                UpdateStatus($"Opening: {extracted.Title} ({i + 1}/{extractedJobs.Count})", current, total);

                var isEasyApply = extracted.EasyApply;

                if (globalEasyApply && !isEasyApply)
                {
                    _logger.LogInformation("[{Current}/{Total}] Skipping non-Easy-Apply: '{Title}'", current, total, extracted.Title);
                    continue;
                }

                var (details, updatedExtracted) = await FetchJobDetailsAsync(page, adapter, extracted, searchUrl, token);
                extracted = updatedExtracted;

                _logger.LogInformation("[{Current}/{Total}] Saving job: '{Title}' at {Company} | EasyApply: {EasyApply} | Desc: {DescLen} chars",
                    current, total, extracted.Title, extracted.Company, isEasyApply, details?.Description?.Length ?? 0);

                await SaveJobAsync(jobRepo, extracted, details, profile, isEasyApply);
                savedCount++;
            }

            // Pagination: navigate to next pages using direct URL navigation
            var pageNum = 1;
            var totalExtracted = savedCount;

            while (!token.IsCancellationRequested && totalExtracted < maxJobs)
            {
                pageNum++;
                _logger.LogInformation("[{Current}/{Total}] Page {PageNum}: URL = {Url}", current, total, pageNum, adapter.BuildSearchUrl(profile, pageNum));

                await adapter.NavigateToPageAsync(page, profile, pageNum);
                await Task.Delay(2000, token);

                var moreJobs = await adapter.ExtractListingsAsync(page);
                _logger.LogInformation("[{Current}/{Total}] Page {PageNum}: found {Count} job(s) for '{ProfileName}'", current, total, pageNum, moreJobs.Count, profile.Name);

                // No more results → end pagination
                if (moreJobs.Count == 0)
                {
                    _logger.LogInformation("[{Current}/{Total}] No results on page {PageNum}, ending pagination", current, total, pageNum);
                    break;
                }

                // Calculate how many more jobs we can process on this page
                var remaining = maxJobs - totalExtracted;
                var jobsToProcess = Math.Min(moreJobs.Count, remaining);

                for (var k = 0; k < jobsToProcess; k++)
                {
                    if (token.IsCancellationRequested) break;

                    var extracted = moreJobs[k];
                    UpdateStatus($"Opening: {extracted.Title} (page {pageNum})", current, total);

                    var isEasyApply = extracted.EasyApply;

                    if (globalEasyApply && !isEasyApply)
                    {
                        _logger.LogInformation("[{Current}/{Total}] Skipping non-Easy-Apply: '{Title}'", current, total, extracted.Title);
                        continue;
                    }

                    var (details, updatedExtracted) = await FetchJobDetailsAsync(page, adapter, extracted, searchUrl, token);
                    extracted = updatedExtracted;

                    _logger.LogInformation("[{Current}/{Total}] Saving job: '{Title}' at {Company} | EasyApply: {EasyApply} | Desc: {DescLen} chars",
                        current, total, extracted.Title, extracted.Company, isEasyApply, details?.Description?.Length ?? 0);

                    await SaveJobAsync(jobRepo, extracted, details, profile, isEasyApply);
                    totalExtracted++;
                }

                // Stop pagination if we processed enough jobs
                if (totalExtracted >= maxJobs)
                {
                    _logger.LogInformation("[{Current}/{Total}] Reached max jobs limit ({MaxJobs}) on page {PageNum}", current, total, maxJobs, pageNum);
                    break;
                }

                // Less than 25 results = likely last page
                if (moreJobs.Count < 25)
                {
                    _logger.LogInformation("[{Current}/{Total}] Less than 25 results on page {PageNum}, assuming last page for '{ProfileName}'", current, total, pageNum, profile.Name);
                    break;
                }
            }

            _logger.LogInformation("[{Current}/{Total}] Finished pagination for '{ProfileName}': extracted {Total} job(s) across {Pages} page(s)",
                current, total, profile.Name, totalExtracted, pageNum);

            _logger.LogInformation("[{Current}/{Total}] Saved {Count} job(s) for '{Name}'", current, total, savedCount, profile.Name);
            return savedCount;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Current}/{Total}] Failed for '{Name}'", current, total, profile.Name);
            _notifications.Add(NotificationType.Error, $"{profile.Platform} Error", $"{profile.Name}: {ex.Message}");
            UpdateStatus($"Error: {profile.Name}", current, total);
            return 0;
        }
    }

    private async Task<string> NavigateToSearchUrlAsync(
        IPlatformAdapter adapter, SearchProfile profile, Microsoft.Playwright.IPage page,
        int current, int total, CancellationToken token)
    {
        var searchUrl = adapter.BuildSearchUrl(profile);
        _logger.LogInformation("[{Current}/{Total}] URL: {SearchUrl}", current, total, searchUrl);

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
