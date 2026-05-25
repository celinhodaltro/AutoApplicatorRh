using AutoApplicator.Application.DTOs;
using AutoApplicator.Domain.Interfaces;
using MediatR;

namespace AutoApplicator.Application.Queries.Profiles;

public sealed record GetProfilesListQuery : IRequest<List<ProfileDto>>;

public sealed class GetProfilesListQueryHandler : IRequestHandler<GetProfilesListQuery, List<ProfileDto>>
{
    private readonly IProfileRepository _repository;

    public GetProfilesListQueryHandler(IProfileRepository repository) => _repository = repository;

    public async Task<List<ProfileDto>> Handle(GetProfilesListQuery request, CancellationToken ct)
    {
        var profiles = await _repository.GetAllAsync(ct);
        return profiles.Select(p => new ProfileDto(
            p.Id, p.Name, p.Enabled, p.Platform.ToString(),
            p.Keywords, p.Location
        )).ToList();
    }
}
