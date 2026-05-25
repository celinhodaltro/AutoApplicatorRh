using AutoApplicator.Domain.Enums;

namespace AutoApplicator.Application.DTOs;

public sealed record JobDetailDto(
    Guid Id,
    string Title,
    string Company,
    string Location,
    string Platform,
    string Status,
    int? MatchScore,
    DateTime PostedDate,
    string Description,
    string? Salary,
    string? Url,
    string? UserNotes,
    string JobType,
    bool EasyApply,
    Guid ProfileId,
    List<string> Skills,
    string? Summary,
    string? MatchReasoning,
    List<string> RedFlags,
    List<string> Highlights,
    string? ResumeUsed,
    string? CoverLetterUsed
);
