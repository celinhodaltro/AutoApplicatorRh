using AutoApplicator.Application.DTOs;
using AutoApplicator.Presentation.Components.ViewModels;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace AutoApplicator.App.Components.Pages.Dashboard;

public partial class Home
{
    [Inject] private DashboardViewModel ViewModel { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private List<ChartItemDto> _jobsByStatusPercentage = [];

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadAsync();
        CalculatePercentages();
    }

    private void CalculatePercentages()
    {
        if (ViewModel.Data is null) return;

        var totalJobs = ViewModel.Data.TotalJobs > 0 ? ViewModel.Data.TotalJobs : 1;
        _jobsByStatusPercentage = ViewModel.Data.JobsByStatus
            .Select(x => new ChartItemDto(
                x.Category,
                (int)Math.Round((double)x.Value / totalJobs * 100)
            ))
            .Where(x => x.Value > 0)
            .ToList();
    }
}
