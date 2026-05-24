using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Application.Queries.Dashboard;
using MediatR;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace AutoApplicator.App.Components.Pages.Dashboard;

public partial class Home
{
    [Inject] private IMediator Mediator { get; set; } = default!;
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
    private List<PipelineItem> _pipelineItems = [];

    protected override async Task OnInitializedAsync()
    {
        try { await LoadDashboardData(); }
        finally { _loading = false; }
    }

    private async Task LoadDashboardData()
    {
        var data = await Mediator.Send(new GetDashboardDataQuery());

        _totalProfiles = data.TotalProfiles;
        _activeProfiles = data.ActiveProfiles;
        _totalJobs = data.TotalJobs;
        _pendingReview = data.PendingReview;
        _appliedJobs = data.AppliedJobs;
        _approvedJobs = data.ApprovedJobs;
        _totalQuestions = data.TotalQuestions;
        _unansweredQuestions = data.UnansweredQuestions;
        _recentJobs = data.RecentJobs;

        _jobsByStatus = data.JobsByStatus
            .Select(x => new ChartDataItem { Category = x.Category, Value = x.Value })
            .ToList();

        _jobsByPlatform = data.JobsByPlatform
            .Select(x => new ChartDataItem { Category = x.Category, Value = x.Value })
            .ToList();

        _questionsStatus = data.QuestionsStatus
            .Select(x => new ChartDataItem { Category = x.Category, Value = x.Value })
            .ToList();

        _pipelineItems = data.PipelineItems
            .Select(p => new PipelineItem { Name = p.Name, Count = p.Count, Color = p.Color })
            .ToList();
    }

    private class ChartDataItem
    {
        public string Category { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private class PipelineItem
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public string Color { get; set; } = string.Empty;
    }
}
