using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace AutoApplicator.App.Components.Pages.Profiles;

public partial class ProfileEditDialog
{
    [Inject] private DialogService DialogService { get; set; } = default!;
    [Inject] private IProfileRepository ProfileRepo { get; set; } = default!;

    [Parameter] public SearchProfile Profile { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    private SearchProfile _editingProfile = default!;
    private string _formPlatform = "LinkedIn";
    private string _formKeywords = "";
    private string _formLocation = "";
    private string _formDatePosted = "Any";
    private string _formExperienceLevel = "";
    private string _formJobTypes = "";
    private string _formExcludeTerms = "";

    private List<SelectOption> _platformOptions = [];
    private List<SelectOption> _datePostedOptions = [];

    protected override void OnInitialized()
    {
        _platformOptions = Enum.GetValues<PlatformType>()
            .Cast<PlatformType>()
            .Select(p => new SelectOption(p.ToString(), p.ToString()))
            .ToList();

        _datePostedOptions =
        [
            new("Any", "Any"),
            new("Past 24 Hours", "Past 24 Hours"),
            new("Past Week", "Past Week"),
            new("Past Month", "Past Month"),
        ];

        _editingProfile = new SearchProfile
        {
            Id = Profile.Id,
            Name = Profile.Name,
            Enabled = Profile.Enabled,
            Platform = Profile.Platform,
            Keywords = [.. Profile.Keywords],
            Location = [.. Profile.Location],
            DatePosted = Profile.DatePosted,
            ExperienceLevel = [.. Profile.ExperienceLevel],
            JobTypes = [.. Profile.JobTypes],
            SalaryMin = Profile.SalaryMin,
            EasyApplyOnly = Profile.EasyApplyOnly,
            RemoteOnly = Profile.RemoteOnly,
            ExcludeTerms = [.. Profile.ExcludeTerms],
            ResumeFile = Profile.ResumeFile,
            CoverLetterTemplate = Profile.CoverLetterTemplate,
            DefaultAnswers = new Dictionary<string, string>(Profile.DefaultAnswers),
            CreatedAt = Profile.CreatedAt,
        };

        LoadForm();
    }

    private void LoadForm()
    {
        _formPlatform = _editingProfile.Platform.ToString();
        _formKeywords = string.Join(", ", _editingProfile.Keywords);
        _formLocation = string.Join(", ", _editingProfile.Location);
        _formDatePosted = string.IsNullOrEmpty(_editingProfile.DatePosted) ? "Any" : _editingProfile.DatePosted;
        _formExperienceLevel = string.Join(", ", _editingProfile.ExperienceLevel);
        _formJobTypes = string.Join(", ", _editingProfile.JobTypes);
        _formExcludeTerms = string.Join(", ", _editingProfile.ExcludeTerms);
    }

    private void SyncFormToProfile()
    {
        _editingProfile.Platform = Enum.Parse<PlatformType>(_formPlatform);
        _editingProfile.Keywords = SplitCommas(_formKeywords);
        _editingProfile.Location = SplitCommas(_formLocation);
        _editingProfile.DatePosted = _formDatePosted == "Any" ? "" : _formDatePosted;
        _editingProfile.ExperienceLevel = SplitCommas(_formExperienceLevel);
        _editingProfile.JobTypes = SplitCommas(_formJobTypes);
        _editingProfile.ExcludeTerms = SplitCommas(_formExcludeTerms);
    }

    private async Task Save()
    {
        SyncFormToProfile();
        _editingProfile.UpdatedAt = DateTime.UtcNow;

        if (IsNew)
        {
            _editingProfile.CreatedAt = DateTime.UtcNow;
            await ProfileRepo.AddAsync(_editingProfile, default);
        }
        else
        {
            await ProfileRepo.UpdateAsync(_editingProfile, default);
        }

        DialogService.Close(true);
    }

    private void Cancel() => DialogService.Close(false);

    private static List<string> SplitCommas(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private record SelectOption(string Text, string Value);
}
