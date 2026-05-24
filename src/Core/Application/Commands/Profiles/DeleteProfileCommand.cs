using AutoApplicator.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Profiles;

public sealed record DeleteProfileCommand(Guid ProfileId) : IRequest;

public sealed class DeleteProfileCommandHandler(
    IProfileRepository profileRepository,
    ILogger<DeleteProfileCommandHandler> logger)
    : IRequestHandler<DeleteProfileCommand>
{
    public async Task Handle(DeleteProfileCommand request, CancellationToken ct)
    {
        await profileRepository.DeleteAsync(request.ProfileId, ct);
        logger.LogInformation("Deleted profile {ProfileId}", request.ProfileId);
    }
}
