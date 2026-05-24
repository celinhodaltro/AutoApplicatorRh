using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using MediatR;

namespace AutoApplicator.Application.Commands.Questions;

public sealed record UpsertQuestionCommand(
    string QuestionText,
    QuestionFieldType FieldType,
    List<string>? Options,
    PlatformType? Platform,
    string? JobTitle,
    string? Company,
    string? Group) : IRequest<Guid>;

public sealed class UpsertQuestionCommandHandler(IQuestionRepository questionRepo)
    : IRequestHandler<UpsertQuestionCommand, Guid>
{
    public async Task<Guid> Handle(UpsertQuestionCommand request, CancellationToken ct)
    {
        var existing = await questionRepo.FindByTextAsync(request.QuestionText);

        if (existing is not null)
        {
            existing.Options = request.Options ?? [];
            existing.FieldType = request.FieldType;
            existing.JobTitle = request.JobTitle;
            existing.Company = request.Company;
            existing.Group = request.Group;
            existing.UpdatedAt = DateTime.UtcNow;
            await questionRepo.UpdateAsync(existing, ct);
            return existing.Id;
        }

        var question = new CollectedQuestion
        {
            Id = Guid.NewGuid(),
            QuestionText = request.QuestionText,
            FieldType = request.FieldType,
            Options = request.Options ?? [],
            Platform = request.Platform,
            JobTitle = request.JobTitle,
            Company = request.Company,
            Group = request.Group,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await questionRepo.AddAsync(question, ct);
        return question.Id;
    }
}
