using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using AutoApplicator.Domain.Models;
using AutoApplicator.Infrastructure.Services.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Services;

public sealed class JobApplyService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlaywrightService _playwrightService;
    private readonly NotificationService _notifications;
    private readonly ILogger<JobApplyService> _logger;

    /// <summary>
    /// Callback for profile-level status updates. Signature: (profileName, status).
    /// </summary>
    internal Action<string, string>? OnProfileStatusUpdate { get; set; }

    /// <summary>
    /// Callback for general status updates. Signature: (message, current, total).
    /// </summary>
    internal Action<string, int, int>? OnStatusUpdate { get; set; }

    public JobApplyService(
        IServiceScopeFactory scopeFactory,
        PlaywrightService playwrightService,
        NotificationService notifications,
        ILogger<JobApplyService> logger)
    {
        _scopeFactory = scopeFactory;
        _playwrightService = playwrightService;
        _notifications = notifications;
        _logger = logger;
    }

    private void UpdateStatus(string message, int current, int total)
    {
        OnStatusUpdate?.Invoke(message, current, total);
    }

    // ──────────────────────────────────────────────
    //  Public entry point – standalone apply
    // ──────────────────────────────────────────────

    public async Task RunApplyAsync(int maxJobs, CancellationToken token)
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
                IBrowserPage? applyPage = null;
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

    // ──────────────────────────────────────────────
    //  Internal – called by orchestrator for inline apply
    // ──────────────────────────────────────────────

    internal async Task<ApplyResult> ApplyForPlatformAsync(IBrowserPage page, JobListing job, PlatformType platform, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();

        var applicators = scope.ServiceProvider.GetRequiredService<IEnumerable<IJobApplicator>>();
        var applicator = applicators.FirstOrDefault(a => a.Platform == platform);

        if (applicator is null)
        {
            _logger.LogWarning("Apply not supported for {Platform}", platform);
            return new ApplyResult(false, $"Apply not supported for {platform}");
        }

        return await applicator.ApplyAsync(page, job);
    }

    internal async Task UpdateJobAfterApplyAsync(JobListing job, ApplyResult result, CancellationToken token)
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

    // ──────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────

    private async Task<List<JobListing>> GetJobsToApplyAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var allJobs = (await jobRepo.GetAllAsync(default)).ToList();
        return allJobs.Where(j => j.Status is JobStatus.New or JobStatus.Approved).ToList();
    }

    private async Task<ApplyResult> ProcessJobApplicationAsync(IBrowserPage page, JobListing job, CancellationToken token)
    {
        await page.GoToAsync(job.Url);
        await Task.Delay(3000, token);

        return await ApplyForPlatformAsync(page, job, job.Platform, token);
    }
}
