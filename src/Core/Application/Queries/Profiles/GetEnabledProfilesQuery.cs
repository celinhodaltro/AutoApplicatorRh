using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using MediatR;

namespace AutoApplicator.Application.Queries.Profiles;

public sealed record GetEnabledProfilesQuery : IRequest<List<SearchProfile>>;

public sealed class GetEnabledProfilesQueryHandler(
    IProfileRepository profileRepository)
    : IRequestHandler<GetEnabledProfilesQuery, List<SearchProfile>>
{
    public async Task<List<SearchProfile>> Handle(GetEnabledProfilesQuery request, CancellationToken ct)
    {
        var profiles = await profileRepository.GetAllAsync(ct);
        return profiles.Where(p => p.Enabled).OrderBy(p => p.Name).ToList();
    }
}
