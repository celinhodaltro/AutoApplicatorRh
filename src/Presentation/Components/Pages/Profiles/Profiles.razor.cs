using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoApplicator.Application.Commands.Profiles;
using AutoApplicator.Application.DTOs;
using AutoApplicator.Application.Queries.Profiles;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Presentation.Components.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace AutoApplicator.App.Components.Pages.Profiles;

public partial class Profiles
{
    [Inject] private IMediator Mediator { get; set; } = default!;
    [Inject] private DialogService DialogService { get; set; } = default!;
    [Inject] private ProfileListViewModel ViewModel { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadProfilesAsync();
    }

    private async Task OpenNewProfileForm()
    {
        var profile = new SearchProfile
        {
            Id = Guid.NewGuid(),
            Name = "",
            Enabled = true,
            Keywords = [],
            Location = [],
            ExperienceLevel = [],
            JobTypes = [],
            ExcludeTerms = [],
        };

        var result = await DialogService.OpenAsync<ProfileEditDialog>(
            "New Profile",
            new Dictionary<string, object?>
            {
                { "Profile", profile },
                { "IsNew", true }
            });

        if (result is true)
        {
            await ViewModel.LoadProfilesAsync();
        }
    }

    private async Task OpenEditProfileForm(ProfileDto profileDto)
    {
        var profile = await Mediator.Send(new GetProfileByIdQuery(profileDto.Id));
        if (profile is null) return;

        var result = await DialogService.OpenAsync<ProfileEditDialog>(
            $"Edit Profile - {profile.Name}",
            new Dictionary<string, object?>
            {
                { "Profile", profile },
                { "IsNew", false }
            });

        if (result is true)
        {
            await ViewModel.LoadProfilesAsync();
        }
    }

    private async Task ToggleProfileEnabled(ProfileDto profileDto, bool value)
    {
        var profile = await Mediator.Send(new GetProfileByIdQuery(profileDto.Id));
        if (profile is null) return;

        profile.Enabled = value;
        profile.UpdatedAt = DateTime.UtcNow;
        await Mediator.Send(new SaveProfileCommand(profile, false));
        await ViewModel.LoadProfilesAsync();
    }

    private async Task ConfirmDeleteProfile(ProfileDto profileDto)
    {
        var confirmed = await DialogService.Confirm(
            $"Are you sure you want to delete profile \"{profileDto.Name}\"?",
            "Confirm Delete",
            new ConfirmOptions
            {
                OkButtonText = "Delete",
                CancelButtonText = "Cancel"
            });

        if (confirmed == true)
        {
            await ViewModel.DeleteProfileAsync(profileDto.Id);
        }
    }
}
