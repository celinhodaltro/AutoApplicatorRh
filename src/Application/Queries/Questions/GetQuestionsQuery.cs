using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Queries.Questions;

public sealed record GetQuestionsQuery : IRequest<List<CollectedQuestion>>;

public sealed class GetQuestionsQueryHandler(
    IQuestionRepository questionRepository,
    ILogger<GetQuestionsQueryHandler> logger)
    : IRequestHandler<GetQuestionsQuery, List<CollectedQuestion>>
{
    public async Task<List<CollectedQuestion>> Handle(GetQuestionsQuery request, CancellationToken ct)
    {
        var questions = (await questionRepository.GetAllAsync(ct)).ToList();

        logger.LogInformation("Retrieved {Count} collected question(s)", questions.Count);
        return questions;
    }
}
