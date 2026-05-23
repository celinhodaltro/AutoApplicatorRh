using AutoApplicator.Application.Commands.Jobs;
using AutoApplicator.Application.Queries.Jobs;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace AutoApplicator.App.Components.Pages.Jobs;

public partial class JobList
{
    [Inject] private IMediator Mediator { get; set; } = default!;
    [Inject] private IJobRepository JobRepo { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private List<JobListing> _allJobs = [];
    private List<JobListing> _filteredJobs = [];
    private HashSet<Guid> _selectedIds = [];
    private JobStatus? _statusFilter;
    private bool _selectAll;

    protected override async Task OnInitializedAsync() => await LoadJobs();

    private async Task LoadJobs()
    {
        _allJobs = await Mediator.Send(new GetJobsQuery(Status: null, Platform: null, Page: 1, PageSize: 200));
        _selectAll = false;
        _selectedIds.Clear();
        ApplyFilter();
    }

    private void FilterByStatus(JobStatus? status)
    {
        _statusFilter = status;
        _selectAll = false;
        _selectedIds.Clear();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _filteredJobs = _statusFilter.HasValue
            ? _allJobs.Where(j => j.Status == _statusFilter.Value).ToList()
            : [.. _allJobs];
    }

    private void ToggleSelection(Guid id, bool isSelected)
    {
        if (isSelected) _selectedIds.Add(id);
        else _selectedIds.Remove(id);
        _selectAll = _selectedIds.Count == _filteredJobs.Count && _filteredJobs.Count > 0;
    }

    private void ToggleSelectAll(ChangeEventArgs e)
    {
        var isChecked = (bool)(e.Value ?? false);
        _selectAll = isChecked;

        if (isChecked)
            _selectedIds = [.. _filteredJobs.Select(j => j.Id)];
        else
            _selectedIds.Clear();
    }

    private async Task BatchApprove()
    {
        await Mediator.Send(new BatchApproveJobsCommand(_selectedIds.ToList()));
        await LoadJobs();
    }

    private async Task BatchReject()
    {
        foreach (var id in _selectedIds.ToList())
            await Mediator.Send(new RejectJobCommand(id));
        await LoadJobs();
    }

    private async Task BatchDelete()
    {
        foreach (var id in _selectedIds.ToList())
            await JobRepo.DeleteAsync(id, default);
        await LoadJobs();
    }

    private async Task ApproveSingle(JobListing job)
    {
        await Mediator.Send(new ApproveJobCommand(job.Id));
        await LoadJobs();
    }

    private async Task RejectSingle(JobListing job)
    {
        await Mediator.Send(new RejectJobCommand(job.Id));
        await LoadJobs();
    }

    private static BadgeStyle GetStatusBadge(JobStatus status) => status switch
    {
        JobStatus.New => BadgeStyle.Info,
        JobStatus.Reviewed => BadgeStyle.Warning,
        JobStatus.Approved => BadgeStyle.Success,
        JobStatus.Rejected => BadgeStyle.Danger,
        JobStatus.Applied => BadgeStyle.Primary,
        JobStatus.Skipped => BadgeStyle.Light,
        JobStatus.Error => BadgeStyle.Danger,
        _ => BadgeStyle.Light,
    };
}
