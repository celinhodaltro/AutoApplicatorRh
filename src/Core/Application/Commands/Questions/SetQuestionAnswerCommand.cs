using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Questions;

public sealed record SetQuestionAnswerCommand(Guid QuestionId, string Answer) : IRequest<CollectedQuestion>;

public sealed class SetQuestionAnswerCommandValidator : AbstractValidator<SetQuestionAnswerCommand>
{
    public SetQuestionAnswerCommandValidator()
    {
        RuleFor(x => x.QuestionId)
            .NotEmpty()
            .WithMessage("QuestionId must not be empty.");

        RuleFor(x => x.Answer)
            .NotEmpty()
            .WithMessage("Answer must not be empty.");
    }
}

public sealed class SetQuestionAnswerCommandHandler(
    IQuestionRepository questionRepository,
    ILogger<SetQuestionAnswerCommandHandler> logger)
    : IRequestHandler<SetQuestionAnswerCommand, CollectedQuestion>
{
    public async Task<CollectedQuestion> Handle(SetQuestionAnswerCommand request, CancellationToken ct)
    {
        var question = await questionRepository.GetByIdAsync(request.QuestionId, ct)
                        ?? throw new KeyNotFoundException($"Question with Id {request.QuestionId} was not found.");

        question.Answer = request.Answer;
        question.UpdatedAt = DateTime.UtcNow;

        await questionRepository.UpdateAsync(question, ct);

        logger.LogInformation("Answer set for question {QuestionId}", question.Id);
        return question;
    }
}
