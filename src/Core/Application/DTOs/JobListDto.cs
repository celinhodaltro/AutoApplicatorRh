namespace AutoApplicator.Application.DTOs;

public sealed record JobListDto(
    Guid Id,
    string Title,
    string Company,
    string Location,
    string Platform,
    string Status,
    int? MatchScore,
    DateTime PostedDate
);
