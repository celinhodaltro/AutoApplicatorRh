using System;
using System.Threading.Tasks;
using AutoApplicator.Presentation.Components.ViewModels;
using Microsoft.AspNetCore.Components;

namespace AutoApplicator.App.Components.Pages.Jobs;

public partial class JobDetail
{
    [Inject] private JobDetailViewModel ViewModel { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter] public Guid Id { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadJobAsync(Id);
    }

    private async Task ApproveJob()
    {
        await ViewModel.ApproveAsync();
    }

    private async Task RejectJob()
    {
        await ViewModel.RejectAsync();
    }

    private async Task ApplyToJob()
    {
        await ViewModel.ApplyAsync();
    }

    private async Task DeleteJob()
    {
        await ViewModel.DeleteAsync();
        Navigation.NavigateTo("/jobs");
    }

    private void NavigateBackToJobList() => Navigation.NavigateTo("/jobs");
}
