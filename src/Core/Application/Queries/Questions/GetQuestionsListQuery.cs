using AutoApplicator.Application.DTOs;
using AutoApplicator.Domain.Interfaces;
using MediatR;

namespace AutoApplicator.Application.Queries.Questions;

public sealed record GetQuestionsListQuery : IRequest<List<QuestionDto>>;

public sealed class GetQuestionsListQueryHandler : IRequestHandler<GetQuestionsListQuery, List<QuestionDto>>
{
    private readonly IQuestionRepository _repository;

    public GetQuestionsListQueryHandler(IQuestionRepository repository) => _repository = repository;

    public async Task<List<QuestionDto>> Handle(GetQuestionsListQuery request, CancellationToken ct)
    {
        var questions = await _repository.GetAllAsync(ct);
        return questions.Select(q => new QuestionDto(
            q.Id, q.QuestionText, q.FieldType.ToString(),
            q.Options, q.Answer, q.Platform?.ToString(),
            q.Group, q.JobTitle, q.Company
        )).ToList();
    }
}
