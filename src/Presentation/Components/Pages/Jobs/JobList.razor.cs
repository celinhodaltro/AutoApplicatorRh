using AutoApplicator.Domain.Enums;
using AutoApplicator.Presentation.Components.ViewModels;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace AutoApplicator.App.Components.Pages.Jobs;

public partial class JobList
{
    [Inject] private JobListViewModel ViewModel { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private HashSet<Guid> _selectedIds = [];
    private bool _selectAll;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadJobsAsync();
    }

    private async Task FilterByStatus(string? status)
    {
        ViewModel.SelectedStatus = status;
        await ViewModel.LoadJobsAsync();
        _selectedIds.Clear();
        _selectAll = false;
    }

    private async Task ApproveSelected()
    {
        foreach (var id in _selectedIds.ToList())
            await ViewModel.ApproveJobAsync(id);
        _selectedIds.Clear();
        _selectAll = false;
    }

    private async Task RejectSelected()
    {
        foreach (var id in _selectedIds.ToList())
            await ViewModel.RejectJobAsync(id);
        _selectedIds.Clear();
        _selectAll = false;
    }

    private async Task DeleteSelected()
    {
        foreach (var id in _selectedIds.ToList())
            await ViewModel.DeleteJobAsync(id);
        _selectedIds.Clear();
        _selectAll = false;
    }

    private async Task ApproveSingle(Guid id)
    {
        await ViewModel.ApproveJobAsync(id);
    }

    private async Task RejectSingle(Guid id)
    {
        await ViewModel.RejectJobAsync(id);
    }

    private void ToggleSelection(Guid id, bool isSelected)
    {
        if (isSelected) _selectedIds.Add(id);
        else _selectedIds.Remove(id);
        _selectAll = _selectedIds.Count == ViewModel.Jobs.Count && ViewModel.Jobs.Count > 0;
    }

    private void ToggleSelectAll(ChangeEventArgs e)
    {
        var isChecked = (bool)(e.Value ?? false);
        _selectAll = isChecked;

        if (isChecked)
            _selectedIds = [.. ViewModel.Jobs.Select(j => j.Id)];
        else
            _selectedIds.Clear();
    }

}
