using System.Collections.Concurrent;
using System.Diagnostics;
using AutoApplicator.Application.Interfaces;
using AutoApplicator.Infrastructure.Services.Exceptions;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Services;

public sealed class AutomationService : IAutomationStateService
{
    private readonly JobSearchService _searchService;
    private readonly JobApplyService _applyService;
    private readonly AutomationOrchestrator _orchestrator;
    private readonly NotificationService _notifications;
    private readonly ExceptionHandlerService _exceptionHandler;
    private readonly ILogger<AutomationService> _logger;
    private readonly ConcurrentDictionary<string, string> _profileStatuses = new();
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public AutomationStatusInfo CurrentStatus { get; private set; } = new();
    public IReadOnlyDictionary<string, string> ProfileStatuses => _profileStatuses;

    public event Action? StatusChanged;

    public AutomationService(
        JobSearchService searchService,
        JobApplyService applyService,
        AutomationOrchestrator orchestrator,
        NotificationService notifications,
        ExceptionHandlerService exceptionHandler,
        ILogger<AutomationService> logger)
    {
        _searchService = searchService;
        _applyService = applyService;
        _orchestrator = orchestrator;
        _notifications = notifications;
        _exceptionHandler = exceptionHandler;
        _logger = logger;
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

        // Wire up status callbacks to sub-services
        _searchService.OnProfileStatusUpdate = UpdateProfileStatus;
        _searchService.OnStatusUpdate = UpdateStatus;
        _applyService.OnProfileStatusUpdate = UpdateProfileStatus;
        _applyService.OnStatusUpdate = UpdateStatus;
        _orchestrator.OnProfileStatusUpdate = UpdateProfileStatus;
        _orchestrator.OnStatusUpdate = UpdateStatus;

        try
        {
            if (mode is AutomationMode.Search or AutomationMode.Full)
            {
                if (mode == AutomationMode.Full)
                {
                    await _orchestrator.RunFullAsync(globalEasyApply, maxFullJobs, token);
                }
                else
                {
                    var searchLimit = maxSearchJobs;
                    await _searchService.SearchAllProfilesAsync(globalEasyApply, searchLimit, token);
                }
            }

            if (mode is AutomationMode.Apply && !token.IsCancellationRequested)
            {
                var applyLimit = maxApplyJobs;
                await _applyService.RunApplyAsync(applyLimit, token);
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

    private void UpdateProfileStatus(string profileName, string status)
    {
        _profileStatuses[profileName] = status;
        UpdateStatus($"Profile: {profileName} - {status}", _profileStatuses.Count, 0);
    }

    private void UpdateStatus(string message, int current, int total)
    {
        CurrentStatus = new AutomationStatusInfo(message, current, total);
        StatusChanged?.Invoke();
    }
}
