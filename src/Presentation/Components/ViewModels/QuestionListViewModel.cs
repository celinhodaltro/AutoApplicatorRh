using AutoApplicator.Application.DTOs;
using AutoApplicator.Application.Queries.Questions;
using MediatR;

namespace AutoApplicator.Presentation.Components.ViewModels;

public class QuestionListViewModel
{
    private readonly IMediator _mediator;

    public QuestionListViewModel(IMediator mediator) => _mediator = mediator;

    public List<QuestionDto> Questions { get; private set; } = [];
    public List<QuestionDto> Unanswered => Questions.Where(q => string.IsNullOrWhiteSpace(q.Answer)).ToList();
    public List<QuestionDto> Answered => Questions.Where(q => !string.IsNullOrWhiteSpace(q.Answer)).ToList();
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task LoadQuestionsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Questions = await _mediator.Send(new GetQuestionsListQuery());
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Questions = [];
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SaveAnswerAsync(Guid questionId, string answer)
    {
        await _mediator.Send(new AutoApplicator.Application.Commands.Questions.SetQuestionAnswerCommand(questionId, answer));
        await LoadQuestionsAsync();
    }

    public async Task DeleteQuestionAsync(Guid questionId)
    {
        await _mediator.Send(new AutoApplicator.Application.Commands.Questions.DeleteQuestionCommand(questionId));
        await LoadQuestionsAsync();
    }
}
