namespace AutoApplicator.Application.DTOs;

public sealed record DashboardData(
    int TotalProfiles,
    int ActiveProfiles,
    int TotalJobs,
    int PendingReview,
    int AppliedJobs,
    int ApprovedJobs,
    int TotalQuestions,
    int UnansweredQuestions,
    List<JobListDto> RecentJobs,
    List<PipelineStageDto> PipelineStages,
    List<ChartItemDto> JobsByStatus,
    List<ChartItemDto> QuestionsStatus
);

public sealed record PipelineStageDto(string Status, int Count);

public sealed record ChartItemDto(string Category, int Value);
