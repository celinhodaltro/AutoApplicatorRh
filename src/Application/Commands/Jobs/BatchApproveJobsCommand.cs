using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Jobs;

public sealed record BatchApproveJobsCommand(List<Guid> JobIds) : IRequest<List<JobListing>>;

public sealed class BatchApproveJobsCommandValidator : AbstractValidator<BatchApproveJobsCommand>
{
    public BatchApproveJobsCommandValidator()
    {
        RuleFor(x => x.JobIds)
            .NotEmpty()
            .WithMessage("At least one JobId must be provided.")
            .Must(x => x.All(id => id != Guid.Empty))
            .WithMessage("All JobIds must be non-empty.");
    }
}

public sealed class BatchApproveJobsCommandHandler(
    IJobRepository jobRepository,
    ILogger<BatchApproveJobsCommandHandler> logger)
    : IRequestHandler<BatchApproveJobsCommand, List<JobListing>>
{
    public async Task<List<JobListing>> Handle(BatchApproveJobsCommand request, CancellationToken ct)
    {
        var approved = new List<JobListing>();

        foreach (var jobId in request.JobIds)
        {
            var job = await jobRepository.GetByIdAsync(jobId, ct);
            if (job is null)
            {
                logger.LogWarning("Job {JobId} not found — skipping", jobId);
                continue;
            }

            job.Status = JobStatus.Approved;
            job.ReviewedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;

            await jobRepository.UpdateAsync(job, ct);
            approved.Add(job);

            logger.LogInformation("Job {JobId} '{Title}' approved (batch)", job.Id, job.Title);
        }

        logger.LogInformation("Batch approval completed: {Approved}/{Total} jobs", approved.Count, request.JobIds.Count);
        return approved;
    }
}
