using AutoApplicator.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Jobs;

public sealed record DeleteJobCommand(Guid JobId) : IRequest;

public sealed class DeleteJobCommandHandler(
    IJobRepository jobRepository,
    ILogger<DeleteJobCommandHandler> logger)
    : IRequestHandler<DeleteJobCommand>
{
    public async Task Handle(DeleteJobCommand request, CancellationToken ct)
    {
        await jobRepository.DeleteAsync(request.JobId, ct);
        logger.LogInformation("Deleted job {JobId}", request.JobId);
    }
}
