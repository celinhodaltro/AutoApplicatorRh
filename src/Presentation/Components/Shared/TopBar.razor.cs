using System;
using System.Threading.Tasks;
using AutoApplicator.Application.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.App.Components.Shared;

public partial class TopBar : IDisposable
{
    [Inject] private IAutomationStateService AutomationService { get; set; } = default!;
    [Inject] private ILogger<TopBar> Logger { get; set; } = default!;

    [Parameter] public EventCallback OnToggleSidebar { get; set; }

    private AutomationMode _mode = AutomationMode.Search;
    private bool _subscribed;

    protected override void OnInitialized()
    {
        if (!_subscribed)
        {
            AutomationService.StatusChanged += OnAutomationStatusChanged;
            _subscribed = true;
        }
    }

    private async void OnAutomationStatusChanged()
    {
        try { await InvokeAsync(StateHasChanged); }
        catch (Exception ex) { Logger.LogDebug(ex, "StateHasChanged failed on status change"); }
    }

    private async Task StartAutomation()
    {
        var globalEasyApply = Preferences.Get("global_easy_apply", false);
        await Task.Run(() => AutomationService.StartAsync(_mode, globalEasyApply));
    }

    private void StopAutomation()
    {
        AutomationService.Stop();
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            AutomationService.StatusChanged -= OnAutomationStatusChanged;
            _subscribed = false;
        }
    }
}
