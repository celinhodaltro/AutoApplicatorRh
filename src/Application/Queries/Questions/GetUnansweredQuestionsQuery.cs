using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Queries.Questions;

public sealed record GetUnansweredQuestionsQuery : IRequest<List<CollectedQuestion>>;

public sealed class GetUnansweredQuestionsQueryHandler(
    IQuestionRepository questionRepository,
    ILogger<GetUnansweredQuestionsQueryHandler> logger)
    : IRequestHandler<GetUnansweredQuestionsQuery, List<CollectedQuestion>>
{
    public async Task<List<CollectedQuestion>> Handle(GetUnansweredQuestionsQuery request, CancellationToken ct)
    {
        var questions = (await questionRepository.GetAllAsync(ct))
            .Where(q => string.IsNullOrWhiteSpace(q.Answer))
            .ToList();

        logger.LogInformation("Retrieved {Count} unanswered question(s)", questions.Count);
        return questions;
    }
}
