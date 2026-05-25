using AutoApplicator.Application.DTOs;
using AutoApplicator.Application.Queries.Dashboard;
using MediatR;

namespace AutoApplicator.Presentation.Components.ViewModels;

public class DashboardViewModel
{
    private readonly IMediator _mediator;

    public DashboardViewModel(IMediator mediator) => _mediator = mediator;

    public DashboardData? Data { get; private set; }
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Data = await _mediator.Send(new GetDashboardDataQuery());
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Data = null;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
