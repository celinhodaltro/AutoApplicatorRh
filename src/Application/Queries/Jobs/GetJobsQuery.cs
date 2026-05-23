using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Queries.Jobs;

public sealed record GetJobsQuery(
    JobStatus? Status,
    string? Platform,
    int Page = 1,
    int PageSize = 20) : IRequest<List<JobListing>>;

public sealed class GetJobsQueryValidator : AbstractValidator<GetJobsQuery>
{
    public GetJobsQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be >= 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 200)
            .WithMessage("PageSize must be between 1 and 200.");
    }
}

public sealed class GetJobsQueryHandler(
    IJobRepository jobRepository,
    ILogger<GetJobsQueryHandler> logger)
    : IRequestHandler<GetJobsQuery, List<JobListing>>
{
    public async Task<List<JobListing>> Handle(GetJobsQuery request, CancellationToken ct)
    {
        var jobs = (await jobRepository.GetAllAsync(ct)).AsEnumerable();

        if (request.Status.HasValue)
        {
            jobs = jobs.Where(j => j.Status == request.Status.Value);
        }

        if (!string.IsNullOrEmpty(request.Platform)
            && Enum.TryParse<PlatformType>(request.Platform, ignoreCase: true, out var platform))
        {
            jobs = jobs.Where(j => j.Platform == platform);
        }

        var result = jobs
            .OrderByDescending(j => j.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        logger.LogInformation("Retrieved {Count} job(s) (page {Page}, size {PageSize})",
            result.Count, request.Page, request.PageSize);

        return result;
    }
}
