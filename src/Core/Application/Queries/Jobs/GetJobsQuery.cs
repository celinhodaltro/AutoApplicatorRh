using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
        var query = jobRepository.Query();

        if (request.Status.HasValue)
            query = query.Where(j => j.Status == request.Status.Value);

        if (!string.IsNullOrEmpty(request.Platform)
            && Enum.TryParse<PlatformType>(request.Platform, ignoreCase: true, out var platform))
        {
            query = query.Where(j => j.Platform == platform);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        logger.LogInformation("Retrieved {Count} of {Total} job(s) (page {Page}, size {PageSize})",
            items.Count, total, request.Page, request.PageSize);

        return items;
    }
}
