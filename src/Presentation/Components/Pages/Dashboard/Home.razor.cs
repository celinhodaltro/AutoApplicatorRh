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
    private List<ChartDataItem> _jobsByStatusPercentage = [];
    private List<ChartDataItem> _jobsByPlatform = [];
    private List<ChartDataItem> _questionsStatus = [];
    private List<JobListing> _recentJobs = [];
    private List<PipelineStage> _pipelineStages = [];

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

        // Calcular percentuais a partir dos dados carregados
        var totalJobs = data.TotalJobs > 0 ? data.TotalJobs : 1;
        _jobsByStatusPercentage = data.JobsByStatus
            .Select(x => new ChartDataItem
            {
                Category = x.Category,
                Value = (int)Math.Round((double)x.Value / totalJobs * 100)
            })
            .Where(x => x.Value > 0)
            .ToList();

        // Converter PipelineItems para PipelineStages
        var stageOrder = new[] { JobStatus.New, JobStatus.Approved, JobStatus.Pending, JobStatus.Applied, JobStatus.Rejected };
        _pipelineStages = stageOrder
            .Select(status => new PipelineStage
            {
                Status = status,
                Count = data.JobsByStatus.FirstOrDefault(x => x.Category == status.ToString())?.Value ?? 0
            })
            .Where(s => s.Count > 0)
            .ToList();
    }

    private class ChartDataItem
    {
        public string Category { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private class PipelineStage
    {
        public JobStatus Status { get; set; }
        public int Count { get; set; }
    }
}
