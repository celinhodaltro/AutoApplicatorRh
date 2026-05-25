using AutoApplicator.Application.DTOs;
using AutoApplicator.Application.Queries.Jobs;
using MediatR;

namespace AutoApplicator.Presentation.Components.ViewModels;

public class JobListViewModel
{
    private readonly IMediator _mediator;

    public JobListViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    public List<JobListDto> Jobs { get; private set; } = [];
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? SelectedStatus { get; set; }
    public string? SelectedPlatform { get; set; }

    public async Task LoadJobsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Jobs = await _mediator.Send(new GetJobsForListQuery(SelectedStatus, SelectedPlatform));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Jobs = [];
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ApproveJobAsync(Guid id)
    {
        await _mediator.Send(new AutoApplicator.Application.Commands.Jobs.ApproveJobCommand(id));
        await LoadJobsAsync();
    }

    public async Task RejectJobAsync(Guid id)
    {
        await _mediator.Send(new AutoApplicator.Application.Commands.Jobs.RejectJobCommand(id));
        await LoadJobsAsync();
    }

    public async Task DeleteJobAsync(Guid id)
    {
        await _mediator.Send(new AutoApplicator.Application.Commands.Jobs.DeleteJobCommand(id));
        await LoadJobsAsync();
    }
}
