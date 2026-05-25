using AutoApplicator.Application.DTOs;
using AutoApplicator.Domain.Interfaces;
using MediatR;

namespace AutoApplicator.Application.Queries.Jobs;

public sealed record GetJobDetailQuery(Guid Id) : IRequest<JobDetailDto?>;

public sealed class GetJobDetailQueryHandler : IRequestHandler<GetJobDetailQuery, JobDetailDto?>
{
    private readonly IJobRepository _repository;

    public GetJobDetailQueryHandler(IJobRepository repository) => _repository = repository;

    public async Task<JobDetailDto?> Handle(GetJobDetailQuery request, CancellationToken ct)
    {
        var job = await _repository.GetByIdAsync(request.Id, ct);
        if (job is null) return null;

        return new JobDetailDto(
            job.Id, job.Title, job.Company, job.Location,
            job.Platform.ToString(), job.Status.ToString(),
            job.MatchScore, job.PostedDate, job.Description,
            job.Salary, job.Url, job.UserNotes,
            job.JobType, job.EasyApply, job.ProfileId,
            job.Skills, job.Summary, job.MatchReasoning,
            job.RedFlags, job.Highlights,
            job.ResumeUsed, job.CoverLetterUsed
        );
    }
}
