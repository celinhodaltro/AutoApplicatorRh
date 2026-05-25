using AutoApplicator.Application.DTOs;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using MediatR;

namespace AutoApplicator.Application.Queries.Dashboard;

public sealed record GetDashboardDataQuery : IRequest<DashboardData>;

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
            .Select(status => new ChartItemDto(status.ToString(), jobs.Count(j => j.Status == status)))
            .Where(x => x.Value > 0)
            .ToList();

        var questionsStatus = new List<ChartItemDto>
        {
            new("Answered", answeredQuestions),
            new("Unanswered", unansweredQuestions)
        };

        var pipelineStages = new List<PipelineStageDto>
        {
            new("New", jobs.Count(j => j.Status == JobStatus.New)),
            new("Approved", jobs.Count(j => j.Status == JobStatus.Approved)),
            new("Pending", jobs.Count(j => j.Status == JobStatus.Pending)),
            new("Applied", jobs.Count(j => j.Status == JobStatus.Applied)),
            new("Rejected", jobs.Count(j => j.Status == JobStatus.Rejected)),
        };

        pipelineStages = pipelineStages.Where(s => s.Count > 0).ToList();

        var recentJobs = jobs
            .OrderByDescending(j => j.CreatedAt)
            .Take(5)
            .Select(j => new JobListDto(
                j.Id, j.Title, j.Company, j.Location,
                j.Platform.ToString(), j.Status.ToString(),
                j.MatchScore, j.PostedDate
            ))
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
            RecentJobs: recentJobs,
            PipelineStages: pipelineStages,
            JobsByStatus: jobsByStatus,
            QuestionsStatus: questionsStatus
        );
    }
}
