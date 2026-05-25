using AutoApplicator.Application.DTOs;
using AutoApplicator.Application.Queries.Profiles;
using MediatR;

namespace AutoApplicator.Presentation.Components.ViewModels;

public class ProfileListViewModel
{
    private readonly IMediator _mediator;

    public ProfileListViewModel(IMediator mediator) => _mediator = mediator;

    public List<ProfileDto> Profiles { get; private set; } = [];
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task LoadProfilesAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Profiles = await _mediator.Send(new GetProfilesListQuery());
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Profiles = [];
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task DeleteProfileAsync(Guid id)
    {
        await _mediator.Send(new AutoApplicator.Application.Commands.Profiles.DeleteProfileCommand(id));
        await LoadProfilesAsync();
    }
}
