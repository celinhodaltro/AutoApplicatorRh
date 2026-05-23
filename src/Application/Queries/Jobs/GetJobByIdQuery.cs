using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Queries.Jobs;

public sealed record GetJobByIdQuery(Guid Id) : IRequest<JobListing?>;

public sealed class GetJobByIdQueryValidator : AbstractValidator<GetJobByIdQuery>
{
    public GetJobByIdQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id must not be empty.");
    }
}

public sealed class GetJobByIdQueryHandler(
    IJobRepository jobRepository,
    ILogger<GetJobByIdQueryHandler> logger)
    : IRequestHandler<GetJobByIdQuery, JobListing?>
{
    public async Task<JobListing?> Handle(GetJobByIdQuery request, CancellationToken ct)
    {
        var job = await jobRepository.GetByIdAsync(request.Id, ct);

        if (job is null)
        {
            logger.LogWarning("Job {JobId} not found", request.Id);
        }

        return job;
    }
}
