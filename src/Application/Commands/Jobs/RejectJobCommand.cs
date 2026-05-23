using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Jobs;

public sealed record RejectJobCommand(Guid JobId) : IRequest<JobListing>;

public sealed class RejectJobCommandValidator : AbstractValidator<RejectJobCommand>
{
    public RejectJobCommandValidator()
    {
        RuleFor(x => x.JobId)
            .NotEmpty()
            .WithMessage("JobId must not be empty.");
    }
}

public sealed class RejectJobCommandHandler(
    IJobRepository jobRepository,
    ILogger<RejectJobCommandHandler> logger)
    : IRequestHandler<RejectJobCommand, JobListing>
{
    public async Task<JobListing> Handle(RejectJobCommand request, CancellationToken ct)
    {
        var job = await jobRepository.GetByIdAsync(request.JobId, ct)
                   ?? throw new KeyNotFoundException($"Job with Id {request.JobId} was not found.");

        job.Status = JobStatus.Rejected;
        job.ReviewedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;

        await jobRepository.UpdateAsync(job, ct);

        logger.LogInformation("Job {JobId} '{Title}' rejected", job.Id, job.Title);
        return job;
    }
}
