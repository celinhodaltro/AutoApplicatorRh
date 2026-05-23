using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Queries.Profiles;

public sealed record GetProfilesQuery : IRequest<List<SearchProfile>>;

public sealed class GetProfilesQueryHandler(
    IProfileRepository profileRepository,
    ILogger<GetProfilesQueryHandler> logger)
    : IRequestHandler<GetProfilesQuery, List<SearchProfile>>
{
    public async Task<List<SearchProfile>> Handle(GetProfilesQuery request, CancellationToken ct)
    {
        var profiles = (await profileRepository.GetAllAsync(ct)).ToList();

        logger.LogInformation("Retrieved {Count} search profile(s)", profiles.Count);
        return profiles;
    }
}
