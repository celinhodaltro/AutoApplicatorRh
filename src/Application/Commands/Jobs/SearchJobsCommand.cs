using System.Text.RegularExpressions;
using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Jobs;

public sealed record SearchJobsCommand(Guid? ProfileId, string? Platform) : IRequest<List<JobListing>>;

public sealed class SearchJobsCommandValidator : AbstractValidator<SearchJobsCommand>
{
    public SearchJobsCommandValidator()
    {
        RuleFor(x => x.ProfileId)
            .NotEmpty()
            .When(x => x.Platform is null)
            .WithMessage("Provide at least a ProfileId or a Platform filter.");

        RuleFor(x => x.Platform)
            .NotEmpty()
            .When(x => x.ProfileId is null || x.ProfileId == Guid.Empty)
            .WithMessage("Provide at least a ProfileId or a Platform filter.");
    }
}

public sealed class SearchJobsCommandHandler(
    IProfileRepository profileRepository,
    IJobRepository jobRepository,
    IPlaywrightService playwrightService,
    ILogger<SearchJobsCommandHandler> logger)
    : IRequestHandler<SearchJobsCommand, List<JobListing>>
{
    public async Task<List<JobListing>> Handle(SearchJobsCommand request, CancellationToken ct)
    {
        var profiles = await ResolveProfilesAsync(request, ct);

        if (profiles.Count == 0)
        {
            logger.LogWarning("No profiles found matching the filters");
            return [];
        }

        var results = new List<JobListing>();

        foreach (var profile in profiles)
        {
            logger.LogInformation("Searching jobs for profile '{ProfileName}' on {Platform}",
                profile.Name, profile.Platform);

            var jobs = await SearchForProfileAsync(profile, ct);
            results.AddRange(jobs);
        }

        logger.LogInformation("Found {Count} job(s) across {ProfileCount} profile(s)", results.Count, profiles.Count);
        return results;
    }

    private async Task<List<SearchProfile>> ResolveProfilesAsync(SearchJobsCommand request, CancellationToken ct)
    {
        if (request.ProfileId.HasValue && request.ProfileId.Value != Guid.Empty)
        {
            var profile = await profileRepository.GetByIdAsync(request.ProfileId.Value, ct);
            return profile is not null ? [profile] : [];
        }

        var allProfiles = (await profileRepository.GetAllAsync(ct)).ToList();

        if (!string.IsNullOrEmpty(request.Platform)
            && Enum.TryParse<PlatformType>(request.Platform, ignoreCase: true, out var platform))
        {
            return allProfiles.Where(p => p.Platform == platform && p.Enabled).ToList();
        }

        return allProfiles.Where(p => p.Enabled).ToList();
    }

    private async Task<List<JobListing>> SearchForProfileAsync(SearchProfile profile, CancellationToken ct)
    {
        await playwrightService.InitializeAsync();

        var searchUrl = BuildSearchUrl(profile);
        logger.LogInformation("Navigating to {SearchUrl}", searchUrl);

        await playwrightService.NavigateAsync(searchUrl);

        await Task.Delay(2000, ct);

        var html = await playwrightService.GetHtmlAsync();
        var jobs = ParseJobListings(html, profile);

        foreach (var job in jobs)
        {
            await jobRepository.AddAsync(job, ct);
        }

        logger.LogInformation("Saved {Count} job(s) for profile '{ProfileName}'", jobs.Count, profile.Name);
        return jobs;
    }

    private static string BuildSearchUrl(SearchProfile profile)
    {
        var keywords = Uri.EscapeDataString(string.Join(" ", profile.Keywords));
        var location = Uri.EscapeDataString(string.Join(" ", profile.Location));

        return profile.Platform switch
        {
            PlatformType.LinkedIn =>
                $"https://www.linkedin.com/jobs/search/?keywords={keywords}&location={location}",
            PlatformType.Indeed =>
                $"https://br.indeed.com/jobs?q={keywords}&l={location}",
            PlatformType.Gupy =>
                $"https://portal.gupy.io/job-search?term={keywords}",
            _ => $"https://www.linkedin.com/jobs/search/?keywords={keywords}&location={location}"
        };
    }

    private static List<JobListing> ParseJobListings(string html, SearchProfile profile)
    {
        var jobs = new List<JobListing>();

        var jobCards = Regex.Split(html, "<(?:div|li|article)[^>]*class=\"[^\"]*?(?:job-card|job-result|job-search-card)[^\"]*?\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        for (var i = 1; i < jobCards.Length; i++)
        {
            var card = jobCards[i];

            var title = ExtractText(card, "h3", "class=\"[^\"]*?job-title[^\"]*?\"")
                       ?? ExtractText(card, "a", "class=\"[^\"]*?job-title[^\"]*?\"")
                       ?? $"Job #{i}";
            var company = ExtractText(card, "span", "class=\"[^\"]*?company-name[^\"]*?\"")
                         ?? ExtractText(card, "a", "class=\"[^\"]*?company[^\"]*?\"")
                         ?? "";
            var location = ExtractText(card, "span", "class=\"[^\"]*?location[^\"]*?\"")
                          ?? "";
            var linkMatch = Regex.Match(card, "href=\"(https?://[^\"]+?)\"", RegexOptions.IgnoreCase);
            var url = linkMatch.Success ? linkMatch.Groups[1].Value : "";

            var externalId = Guid.NewGuid().ToString("N")[..12];

            var job = new JobListing
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Platform = profile.Platform,
                ProfileId = profile.Id,
                Url = url,
                Title = title,
                Company = company,
                Location = location,
                Description = "",
                JobType = string.Join(", ", profile.JobTypes),
                PostedDate = DateTime.UtcNow,
                EasyApply = profile.EasyApplyOnly,
                Status = JobStatus.New,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            jobs.Add(job);
        }

        return jobs;
    }

    private static string? ExtractText(string html, string tag, string attributePattern)
    {
        var pattern = $"<{tag}\\s+{attributePattern}[^>]*>\\s*(.*?)\\s*</{tag}>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
        {
            var text = match.Groups[1].Value;
            text = Regex.Replace(text, "<[^>]+>", "");
            return System.Net.WebUtility.HtmlDecode(text.Trim());
        }
        return null;
    }
}
