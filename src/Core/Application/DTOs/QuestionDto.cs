namespace AutoApplicator.Application.DTOs;

public sealed record QuestionDto(
    Guid Id,
    string QuestionText,
    string FieldType,
    List<string>? Options,
    string Answer,
    string? Platform,
    string? Group,
    string? JobTitle,
    string? Company
);
