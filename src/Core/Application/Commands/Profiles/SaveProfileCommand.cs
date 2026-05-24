using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Profiles;

public sealed record SaveProfileCommand(SearchProfile Profile, bool IsNew) : IRequest;

public sealed class SaveProfileCommandHandler(
    IProfileRepository profileRepository,
    ILogger<SaveProfileCommandHandler> logger)
    : IRequestHandler<SaveProfileCommand>
{
    public async Task Handle(SaveProfileCommand request, CancellationToken ct)
    {
        if (request.IsNew)
            await profileRepository.AddAsync(request.Profile, ct);
        else
            await profileRepository.UpdateAsync(request.Profile, ct);

        logger.LogInformation("{Action} profile {ProfileId} '{Name}'",
            request.IsNew ? "Created" : "Updated",
            request.Profile.Id,
            request.Profile.Name);
    }
}
