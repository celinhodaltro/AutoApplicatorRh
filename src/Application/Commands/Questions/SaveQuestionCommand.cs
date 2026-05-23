using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Questions;

public sealed record SaveQuestionCommand(
    Guid? Id,
    string QuestionText,
    QuestionFieldType FieldType,
    List<string>? Options,
    string? Answer,
    PlatformType? Platform,
    string? JobTitle,
    string? Company) : IRequest<CollectedQuestion>;

public sealed class SaveQuestionCommandValidator : AbstractValidator<SaveQuestionCommand>
{
    public SaveQuestionCommandValidator()
    {
        RuleFor(x => x.QuestionText)
            .NotEmpty()
            .WithMessage("QuestionText must not be empty.");
    }
}

public sealed class SaveQuestionCommandHandler(
    IQuestionRepository questionRepository,
    ILogger<SaveQuestionCommandHandler> logger)
    : IRequestHandler<SaveQuestionCommand, CollectedQuestion>
{
    public async Task<CollectedQuestion> Handle(SaveQuestionCommand request, CancellationToken ct)
    {
        if (request.Id.HasValue && request.Id.Value != Guid.Empty)
        {
            var existing = await questionRepository.GetByIdAsync(request.Id.Value, ct);
            if (existing is not null)
            {
                existing.QuestionText = request.QuestionText;
                existing.FieldType = request.FieldType;
                existing.Options = request.Options ?? [];
                existing.Answer = request.Answer ?? string.Empty;
                existing.Platform = request.Platform;
                existing.JobTitle = request.JobTitle;
                existing.Company = request.Company;
                existing.UpdatedAt = DateTime.UtcNow;

                await questionRepository.UpdateAsync(existing, ct);
                logger.LogInformation("Question {QuestionId} updated", existing.Id);
                return existing;
            }
        }

        var question = new CollectedQuestion
        {
            Id = Guid.NewGuid(),
            QuestionText = request.QuestionText,
            FieldType = request.FieldType,
            Options = request.Options ?? [],
            Answer = request.Answer ?? string.Empty,
            Platform = request.Platform,
            JobTitle = request.JobTitle,
            Company = request.Company,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await questionRepository.AddAsync(question, ct);
        logger.LogInformation("Question {QuestionId} created", question.Id);
        return question;
    }
}
