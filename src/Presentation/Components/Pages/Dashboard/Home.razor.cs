using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace AutoApplicator.App.Components.Pages.Dashboard;

public partial class Home
{
    [Inject] private IProfileRepository ProfileRepo { get; set; } = default!;
    [Inject] private IJobRepository JobRepo { get; set; } = default!;
    [Inject] private IQuestionRepository QuestionRepo { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private bool _loading = true;

    private int _totalProfiles;
    private int _activeProfiles;
    private int _totalJobs;
    private int _pendingReview;
    private int _appliedJobs;
    private int _approvedJobs;
    private int _totalQuestions;
    private int _unansweredQuestions;

    private List<ChartDataItem> _jobsByStatus = [];
    private List<ChartDataItem> _jobsByPlatform = [];
    private List<ChartDataItem> _questionsStatus = [];
    private List<JobListing> _recentJobs = [];

    protected override async Task OnInitializedAsync()
    {
        try { await LoadDashboardData(); }
        finally { _loading = false; }
    }

    private async Task LoadDashboardData()
    {
        var profilesTask = ProfileRepo.GetAllAsync(default);
        var jobsTask = JobRepo.GetAllAsync(default);
        var questionsTask = QuestionRepo.GetAllAsync(default);

        await Task.WhenAll(profilesTask, jobsTask, questionsTask);

        var profiles = (await profilesTask).ToList();
        var jobs = (await jobsTask).ToList();
        var questions = (await questionsTask).ToList();

        _totalProfiles = profiles.Count;
        _activeProfiles = profiles.Count(p => p.Enabled);
        _totalJobs = jobs.Count;
        _pendingReview = jobs.Count(j => j.Status is JobStatus.New or JobStatus.Reviewed);
        _appliedJobs = jobs.Count(j => j.Status == JobStatus.Applied);
        _approvedJobs = jobs.Count(j => j.Status == JobStatus.Approved);
        _totalQuestions = questions.Count;
        _unansweredQuestions = questions.Count(q => string.IsNullOrWhiteSpace(q.Answer));

        _jobsByStatus = Enum.GetValues<JobStatus>()
            .Select(status => new ChartDataItem { Category = status.ToString(), Value = jobs.Count(j => j.Status == status) })
            .Where(x => x.Value > 0).ToList();

        _jobsByPlatform = Enum.GetValues<PlatformType>()
            .Select(platform => new ChartDataItem { Category = platform.ToString(), Value = jobs.Count(j => j.Platform == platform) })
            .Where(x => x.Value > 0).ToList();

        var answered = questions.Count(q => !string.IsNullOrWhiteSpace(q.Answer));
        _questionsStatus = [new() { Category = "Answered", Value = answered }, new() { Category = "Unanswered", Value = questions.Count - answered }];

        _recentJobs = jobs.OrderByDescending(j => j.CreatedAt).Take(5).ToList();
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

    private class ChartDataItem
    {
        public string Category { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
