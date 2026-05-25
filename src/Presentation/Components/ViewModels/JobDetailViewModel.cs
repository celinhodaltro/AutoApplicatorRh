using AutoApplicator.Application.DTOs;
using AutoApplicator.Application.Queries.Jobs;
using MediatR;

namespace AutoApplicator.Presentation.Components.ViewModels;

public class JobDetailViewModel
{
    private readonly IMediator _mediator;

    public JobDetailViewModel(IMediator mediator) => _mediator = mediator;

    public JobDetailDto? Job { get; private set; }
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task LoadJobAsync(Guid id)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Job = await _mediator.Send(new GetJobDetailQuery(id));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Job = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ApproveAsync()
    {
        if (Job is null) return;
        await _mediator.Send(new AutoApplicator.Application.Commands.Jobs.ApproveJobCommand(Job.Id));
        await LoadJobAsync(Job.Id);
    }

    public async Task RejectAsync()
    {
        if (Job is null) return;
        await _mediator.Send(new AutoApplicator.Application.Commands.Jobs.RejectJobCommand(Job.Id));
        await LoadJobAsync(Job.Id);
    }

    public async Task ApplyAsync()
    {
        if (Job is null) return;
        await _mediator.Send(new AutoApplicator.Application.Commands.Jobs.ApplyToJobCommand(Job.Id));
        await LoadJobAsync(Job.Id);
    }

    public async Task DeleteAsync()
    {
        if (Job is null) return;
        await _mediator.Send(new AutoApplicator.Application.Commands.Jobs.DeleteJobCommand(Job.Id));
        Job = null;
    }
}
