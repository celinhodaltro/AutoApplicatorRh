using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Jobs;

public sealed record ApproveJobCommand(Guid JobId) : IRequest<JobListing>;

public sealed class ApproveJobCommandValidator : AbstractValidator<ApproveJobCommand>
{
    public ApproveJobCommandValidator()
    {
        RuleFor(x => x.JobId)
            .NotEmpty()
            .WithMessage("JobId must not be empty.");
    }
}

public sealed class ApproveJobCommandHandler(
    IJobRepository jobRepository,
    ILogger<ApproveJobCommandHandler> logger)
    : IRequestHandler<ApproveJobCommand, JobListing>
{
    public async Task<JobListing> Handle(ApproveJobCommand request, CancellationToken ct)
    {
        var job = await jobRepository.GetByIdAsync(request.JobId, ct)
                   ?? throw new KeyNotFoundException($"Job with Id {request.JobId} was not found.");

        job.Status = JobStatus.Approved;
        job.ReviewedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;

        await jobRepository.UpdateAsync(job, ct);

        logger.LogInformation("Job {JobId} '{Title}' approved", job.Id, job.Title);
        return job;
    }
}
