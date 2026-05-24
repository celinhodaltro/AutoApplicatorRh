using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using MediatR;

namespace AutoApplicator.Application.Queries.Questions;

public sealed record FindQuestionByTextQuery(string QuestionText) : IRequest<CollectedQuestion?>;

public sealed class FindQuestionByTextQueryHandler(IQuestionRepository questionRepo)
    : IRequestHandler<FindQuestionByTextQuery, CollectedQuestion?>
{
    public async Task<CollectedQuestion?> Handle(FindQuestionByTextQuery request, CancellationToken ct)
        => await questionRepo.FindByTextAsync(request.QuestionText);
}
