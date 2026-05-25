using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using AutoApplicator.Domain.Models;
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

    private sealed record ProfileResult(int Found, int Applied, int Errors);

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

        var results = await Task.WhenAll(
            profiles.Select(profile =>
                Task.Run(() => ExecuteProfileSearchAsync(profile, globalEasyApply, maxJobs, token), token)));

        var totalFound = results.Sum(r => r.Found);
        var totalApplied = results.Sum(r => r.Applied);
        var totalErrors = results.Sum(r => r.Errors);

        _logger.LogInformation("Full automation complete: {Found} jobs found, {Applied} applied, {Errors} errors across {Profiles} profile(s)",
            totalFound, totalApplied, totalErrors, profiles.Count);

        if (totalApplied > 0)
            _notifications.Add(NotificationType.Success, "Full Automation Complete", $"Applied to {totalApplied} job(s) across {profiles.Count} profiles.", "View Jobs", "/jobs");
        if (totalErrors > 0)
            _notifications.Add(NotificationType.Warning, "Full Automation", $"{totalErrors} error(s) occurred.");
    }

    private async Task<ProfileResult> ExecuteProfileSearchAsync(
        SearchProfile profile, bool globalEasyApply, int maxJobs, CancellationToken token)
    {
        var profilePage = await _playwrightService.CreateNewPageAsync();
        try
        {
            var adapter = _adapterFactory.Create(profile.Platform);

            _logger.LogInformation("[Profile: {Name}] Starting Full search on {Platform}", profile.Name, profile.Platform);
            UpdateProfileStatus(profile.Name, "Full processing...");

            var searchUrl = await _searchService.NavigateToSearchUrlAsync(adapter, profile, profilePage, token);
            await _searchService.WaitForLoginAsync(adapter, profilePage, profile, token);

            UpdateProfileStatus(profile.Name, "Searching...");

            // Fase 1: Collect all jobs from all pages quickly (card-level only)
            var allJobs = await _searchService.CollectAllJobsFromAllPagesAsync(
                adapter, profilePage, profile, maxJobs, globalEasyApply, token);

            _logger.LogInformation("[Profile: {Name}] Collected {Count} jobs across all pages", profile.Name, allJobs.Count);

            // Fase 2: Process each job
            var totalFound = 0;
            var totalApplied = 0;
            var totalErrors = 0;

            for (var i = 0; i < allJobs.Count; i++)
            {
                if (token.IsCancellationRequested) break;

                var extracted = allJobs[i];
                var isEasyApply = extracted.EasyApply;

                if (isEasyApply)
                {
                    // EasyApply: fetch details and apply
                    var (details, updatedExtracted) = await _searchService.FetchJobDetailsAsync(
                        profilePage, adapter, extracted, searchUrl, token);
                    extracted = updatedExtracted;

                    var result = await ApplyToEasyApplyJobAsync(profile, extracted, details, token);
                    totalFound += result.Found;
                    totalApplied += result.Applied;
                    totalErrors += result.Errors;
                }
                else
                {
                    // Non-EasyApply: save without fetching details
                    await SaveNonEasyApplyJobAsync(profile, extracted, null);
                    totalFound++;
                }
            }

            _logger.LogInformation("[Profile: {Name}] Full processing complete: {Applied} applied, {Errors} errors",
                profile.Name, totalApplied, totalErrors);

            UpdateProfileStatus(profile.Name, "Complete");
            return new ProfileResult(totalFound, totalApplied, totalErrors);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Profile: {Name}] Full processing failed", profile.Name);
            _notifications.Add(NotificationType.Error, $"{profile.Platform} Error", $"{profile.Name}: {ex.Message}");
            return new ProfileResult(0, 0, 0);
        }
        finally
        {
            try { await profilePage.CloseAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to close profile page for '{Name}'", profile.Name); }
        }
    }

    private async Task<ProfileResult> ApplyToEasyApplyJobAsync(
        SearchProfile profile, ExtractedJob extracted, JobDetail? details, CancellationToken token)
    {
        IBrowserPage? applyPage = null;
        try
        {
            applyPage = await _playwrightService.CreateNewPageAsync();

            _logger.LogInformation("[Profile: {Name}] Applying to '{Title}' at {Company}", profile.Name, extracted.Title, extracted.Company);

            await applyPage.GoToAsync(extracted.Url);
            await Task.Delay(3000, token);

            var jobListing = CreateJobListing(extracted, profile);

            UpdateProfileStatus(profile.Name, "Applying...");
            var result = await _applyService.ApplyForPlatformAsync(applyPage, jobListing, profile.Platform, token);

            return await HandleApplyResultAsync(jobListing, result, profile, extracted, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Profile: {Name}] Error applying to '{Title}'", profile.Name, extracted.Title);
            return new ProfileResult(0, 0, 1);
        }
        finally
        {
            if (applyPage is not null)
            {
                try { await applyPage.CloseAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to close apply page for '{Title}'", extracted.Title); }
            }
        }
    }

    private async Task<ProfileResult> HandleApplyResultAsync(
        JobListing jobListing, ApplyResult result, SearchProfile profile, ExtractedJob extracted, CancellationToken token)
    {
        if (result.Success)
        {
            jobListing.Status = JobStatus.Applied;
            jobListing.AppliedAt = DateTime.UtcNow;
            jobListing.ApplicationAnswers = result.AnswersUsed;
            _logger.LogInformation("[Profile: {Name}] ✅ Applied: '{Title}'", profile.Name, extracted.Title);
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
        }

        using var saveScope = _scopeFactory.CreateScope();
        var jobRepo = saveScope.ServiceProvider.GetRequiredService<IJobRepository>();
        await jobRepo.AddAsync(jobListing, token);

        var isError = !result.Success && !result.NeedsManualIntervention;
        return new ProfileResult(1, result.Success ? 1 : 0, isError ? 1 : 0);
    }

    private async Task<ProfileResult> SaveNonEasyApplyJobAsync(SearchProfile profile, ExtractedJob extracted, JobDetail? details)
    {
        _logger.LogInformation("[Profile: {Name}] Saving non-Easy-Apply job: '{Title}' at {Company}", profile.Name, extracted.Title, extracted.Company);

        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        await JobSearchService.SaveJobAsync(jobRepo, extracted, details, profile, false);

        return new ProfileResult(1, 0, 0);
    }

    private static JobListing CreateJobListing(ExtractedJob extracted, SearchProfile profile)
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
            EasyApply = true,
            Status = JobStatus.New,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
