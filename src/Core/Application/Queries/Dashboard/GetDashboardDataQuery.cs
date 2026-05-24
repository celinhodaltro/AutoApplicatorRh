using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using MediatR;

namespace AutoApplicator.Application.Queries.Dashboard;

public sealed record GetDashboardDataQuery : IRequest<DashboardData>;

public sealed record DashboardData(
    int TotalProfiles,
    int ActiveProfiles,
    int TotalJobs,
    int PendingReview,
    int AppliedJobs,
    int ApprovedJobs,
    int TotalQuestions,
    int UnansweredQuestions,
    int AnsweredQuestions,
    List<JobListing> RecentJobs,
    List<ChartDataItem> JobsByStatus,
    List<ChartDataItem> JobsByPlatform,
    List<ChartDataItem> QuestionsStatus,
    List<PipelineStageItem> PipelineItems);

public sealed record ChartDataItem(string Category, int Value);

public sealed record PipelineStageItem(string Name, int Count, string Color);

public sealed class GetDashboardDataQueryHandler(
    IProfileRepository profileRepo,
    IJobRepository jobRepo,
    IQuestionRepository questionRepo)
    : IRequestHandler<GetDashboardDataQuery, DashboardData>
{
    public async Task<DashboardData> Handle(GetDashboardDataQuery request, CancellationToken ct)
    {
        var profilesTask = profileRepo.GetAllAsync(ct);
        var jobsTask = jobRepo.GetAllAsync(ct);
        var questionsTask = questionRepo.GetAllAsync(ct);

        await Task.WhenAll(profilesTask, jobsTask, questionsTask);

        var profiles = (await profilesTask).ToList();
        var jobs = (await jobsTask).ToList();
        var questions = (await questionsTask).ToList();

        var totalQuestions = questions.Count;
        var answeredQuestions = questions.Count(q => !string.IsNullOrWhiteSpace(q.Answer));
        var unansweredQuestions = totalQuestions - answeredQuestions;

        var jobsByStatus = Enum.GetValues<JobStatus>()
            .Select(status => new ChartDataItem(status.ToString(), jobs.Count(j => j.Status == status)))
            .Where(x => x.Value > 0)
            .ToList();

        var jobsByPlatform = Enum.GetValues<PlatformType>()
            .Select(platform => new ChartDataItem(platform.ToString(), jobs.Count(j => j.Platform == platform)))
            .Where(x => x.Value > 0)
            .ToList();

        var questionsStatus = new List<ChartDataItem>
        {
            new("Answered", answeredQuestions),
            new("Unanswered", unansweredQuestions)
        };

        var pipelineStages = new (JobStatus Status, string Name, string Color)[]
        {
            (JobStatus.New, "New", "#2196F3"),
            (JobStatus.Approved, "Approved", "#4CAF50"),
            (JobStatus.Pending, "Pending", "#9C27B0"),
            (JobStatus.Applied, "Applied", "#2E7D32"),
            (JobStatus.Rejected, "Rejected", "#F44336"),
        };

        var pipelineItems = pipelineStages
            .Select(p => new PipelineStageItem(p.Name, jobs.Count(j => j.Status == p.Status), p.Color))
            .ToList();

        return new DashboardData(
            TotalProfiles: profiles.Count,
            ActiveProfiles: profiles.Count(p => p.Enabled),
            TotalJobs: jobs.Count,
            PendingReview: jobs.Count(j => j.Status is JobStatus.New or JobStatus.Pending),
            AppliedJobs: jobs.Count(j => j.Status == JobStatus.Applied),
            ApprovedJobs: jobs.Count(j => j.Status == JobStatus.Approved),
            TotalQuestions: totalQuestions,
            UnansweredQuestions: unansweredQuestions,
            AnsweredQuestions: answeredQuestions,
            RecentJobs: jobs.OrderByDescending(j => j.CreatedAt).Take(5).ToList(),
            JobsByStatus: jobsByStatus,
            JobsByPlatform: jobsByPlatform,
            QuestionsStatus: questionsStatus,
            PipelineItems: pipelineItems
        );
    }
}
