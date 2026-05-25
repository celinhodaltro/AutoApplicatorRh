namespace AutoApplicator.Application.DTOs;

public sealed record ProfileDto(
    Guid Id,
    string Name,
    bool Enabled,
    string Platform,
    List<string> Keywords,
    List<string> Location
);
