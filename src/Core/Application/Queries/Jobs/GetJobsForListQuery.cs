using AutoApplicator.Application.DTOs;
using AutoApplicator.Domain.Interfaces;
using MediatR;

namespace AutoApplicator.Application.Queries.Jobs;

public sealed record GetJobsForListQuery(string? Status = null, string? Platform = null) : IRequest<List<JobListDto>>;

public sealed class GetJobsForListQueryHandler : IRequestHandler<GetJobsForListQuery, List<JobListDto>>
{
    private readonly IJobRepository _repository;

    public GetJobsForListQueryHandler(IJobRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<JobListDto>> Handle(GetJobsForListQuery request, CancellationToken ct)
    {
        var jobs = await _repository.GetAllAsync(ct);

        var query = jobs.AsEnumerable();

        if (!string.IsNullOrEmpty(request.Status))
            query = query.Where(j => j.Status.ToString().Equals(request.Status, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(request.Platform))
            query = query.Where(j => j.Platform.ToString().Equals(request.Platform, StringComparison.OrdinalIgnoreCase));

        return query
            .OrderByDescending(j => j.PostedDate)
            .Select(j => new JobListDto(
                j.Id,
                j.Title,
                j.Company,
                j.Location,
                j.Platform.ToString(),
                j.Status.ToString(),
                j.MatchScore,
                j.PostedDate
            ))
            .ToList();
    }
}
