using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using MediatR;

namespace AutoApplicator.Application.Queries.Profiles;

public sealed record GetProfileByIdQuery(Guid Id) : IRequest<SearchProfile?>;

public sealed class GetProfileByIdQueryHandler(
    IProfileRepository profileRepository)
    : IRequestHandler<GetProfileByIdQuery, SearchProfile?>
{
    public async Task<SearchProfile?> Handle(GetProfileByIdQuery request, CancellationToken ct)
    {
        return await profileRepository.GetByIdAsync(request.Id, ct);
    }
}
