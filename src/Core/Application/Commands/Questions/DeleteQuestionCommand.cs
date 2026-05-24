using AutoApplicator.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Questions;

public sealed record DeleteQuestionCommand(Guid QuestionId) : IRequest;

public sealed class DeleteQuestionCommandHandler(
    IQuestionRepository questionRepository,
    ILogger<DeleteQuestionCommandHandler> logger)
    : IRequestHandler<DeleteQuestionCommand>
{
    public async Task Handle(DeleteQuestionCommand request, CancellationToken ct)
    {
        await questionRepository.DeleteAsync(request.QuestionId, ct);
        logger.LogInformation("Question {QuestionId} deleted", request.QuestionId);
    }
}
