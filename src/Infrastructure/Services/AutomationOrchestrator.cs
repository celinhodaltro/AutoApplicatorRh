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

public sealed class AutomationOrchestrator
{
    private readonly JobSearchService _searchService;
    private readonly JobApplyService _applyService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlaywrightService _playwrightService;
    private readonly PlatformAdapterFactory _adapterFactory;
    private readonly NotificationService _notifications;
    private readonly ExceptionHandlerService _exceptionHandler;
    private readonly ILogger<AutomationOrchestrator> _logger;

    /// <summary>
    /// Callback for profile-level status updates. Signature: (profileName, status).
    /// </summary>
    internal Action<string, string>? OnProfileStatusUpdate { get; set; }

    /// <summary>
    /// Callback for general status updates. Signature: (message, current, total).
    /// </summary>
    internal Action<string, int, int>? OnStatusUpdate { get; set; }

    public AutomationOrchestrator(
        JobSearchService searchService,
        JobApplyService applyService,
        IServiceScopeFactory scopeFactory,
        PlaywrightService playwrightService,
        PlatformAdapterFactory adapterFactory,
        NotificationService notifications,
        ExceptionHandlerService exceptionHandler,
        ILogger<AutomationOrchestrator> logger)
    {
        _searchService = searchService;
        _applyService = applyService;
        _scopeFactory = scopeFactory;
        _playwrightService = playwrightService;
        _adapterFactory = adapterFactory;
        _notifications = notifications;
        _exceptionHandler = exceptionHandler;
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
    //  Public entry point – Full (search + apply)
    // ──────────────────────────────────────────────

    public async Task RunFullAsync(bool globalEasyApply, int maxJobs, CancellationToken token)
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
                    UpdateProfileStatus(profile.Name, "Full processing...");

                    var searchUrl = await _searchService.NavigateToSearchUrlAsync(adapter, profile, profilePage, token);
                    await _searchService.WaitForLoginAsync(adapter, profilePage, profile, token);

                    UpdateProfileStatus(profile.Name, "Searching...");
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

                        var (details, updatedExtracted) = await _searchService.FetchJobDetailsAsync(profilePage, adapter, extracted, searchUrl, token);
                        extracted = updatedExtracted;

                        if (isEasyApply)
                        {
                            // Apply in a new page
                            IBrowserPage? applyPage = null;
                            try
                            {
                                applyPage = await _playwrightService.CreateNewPageAsync();

                                _logger.LogInformation("[Profile: {Name}] Applying to '{Title}' at {Company}", profile.Name, extracted.Title, extracted.Company);

                                await applyPage.GoToAsync(extracted.Url);
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

                                UpdateProfileStatus(profile.Name, "Applying...");
                                var result = await _applyService.ApplyForPlatformAsync(applyPage, jobListing, profile.Platform, token);

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
                            await JobSearchService.SaveJobAsync(jobRepo, extracted, details, profile, false);
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

                            var (details, updatedExtracted) = await _searchService.FetchJobDetailsAsync(profilePage, adapter, extracted, searchUrl, token);
                            extracted = updatedExtracted;

                            if (isEasyApply)
                            {
                                // Apply in a new page
                                IBrowserPage? applyPage = null;
                                try
                                {
                                    applyPage = await _playwrightService.CreateNewPageAsync();

                                    _logger.LogInformation("[Profile: {Name}] Applying to '{Title}' at {Company}", profile.Name, extracted.Title, extracted.Company);

                                    await applyPage.GoToAsync(extracted.Url);
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

                                    UpdateProfileStatus(profile.Name, "Applying...");
                                    var result = await _applyService.ApplyForPlatformAsync(applyPage, jobListing, profile.Platform, token);

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
                                await JobSearchService.SaveJobAsync(jobRepo, extracted, details, profile, false);
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

                    UpdateProfileStatus(profile.Name, "Complete");
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
}
