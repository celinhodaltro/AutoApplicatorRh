using AutoApplicator.Infrastructure.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace AutoApplicator.App.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    [Inject] private AutomationService AutomationService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private bool _sidebarExpanded = true;
    private string _currentUrl = "";

    protected override void OnInitialized()
    {
        AutomationService.StatusChanged += OnStatusChanged;
        _currentUrl = Navigation.Uri;
        Navigation.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        _currentUrl = e.Location;
        StateHasChanged();
    }

    private async void OnStatusChanged()
    {
        try { await InvokeAsync(StateHasChanged); }
        catch { }
    }

    private void ToggleSidebar()
    {
        _sidebarExpanded = !_sidebarExpanded;
    }

    private bool IsActive(string path)
    {
        return _currentUrl.EndsWith(path) || _currentUrl.Contains($"/{path}");
    }

    public void Dispose()
    {
        AutomationService.StatusChanged -= OnStatusChanged;
        Navigation.LocationChanged -= OnLocationChanged;
    }
}
