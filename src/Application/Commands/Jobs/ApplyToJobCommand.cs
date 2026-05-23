using AutoApplicator.Application.Interfaces;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Application.Commands.Jobs;

public sealed record ApplyToJobCommand(Guid JobId) : IRequest<JobListing>;

public sealed class ApplyToJobCommandValidator : AbstractValidator<ApplyToJobCommand>
{
    public ApplyToJobCommandValidator()
    {
        RuleFor(x => x.JobId)
            .NotEmpty()
            .WithMessage("JobId must not be empty.");
    }
}

public sealed class ApplyToJobCommandHandler(
    IJobRepository jobRepository,
    IPlaywrightService playwrightService,
    ILogger<ApplyToJobCommandHandler> logger)
    : IRequestHandler<ApplyToJobCommand, JobListing>
{
    public async Task<JobListing> Handle(ApplyToJobCommand request, CancellationToken ct)
    {
        var job = await jobRepository.GetByIdAsync(request.JobId, ct)
                   ?? throw new KeyNotFoundException($"Job with Id {request.JobId} was not found.");

        if (job.Status is JobStatus.Applied)
        {
            logger.LogWarning("Job {JobId} '{Title}' already applied", job.Id, job.Title);
            return job;
        }

        logger.LogInformation("Starting apply flow for job {JobId} '{Title}' at {Company}",
            job.Id, job.Title, job.Company);

        await playwrightService.InitializeAsync();

        await playwrightService.NavigateAsync(job.Url);
        await Task.Delay(3000, ct);

        if (job.EasyApply)
        {
            await HandleEasyApplyFlow(job, ct);
        }
        else
        {
            logger.LogInformation("Job {JobId} does not have Easy Apply — manual flow required", job.Id);
        }

        if (ct.IsCancellationRequested) return job;

        var screenshotPath = Path.Combine(Path.GetTempPath(), $"apply_{job.Id:N}.png");
        await playwrightService.ScreenshotAsync(screenshotPath);
        logger.LogInformation("Screenshot saved to {Path}", screenshotPath);

        job.Status = JobStatus.Applied;
        job.AppliedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;

        await jobRepository.UpdateAsync(job, ct);

        logger.LogInformation("Job {JobId} '{Title}' applied successfully", job.Id, job.Title);
        return job;
    }

    private async Task HandleEasyApplyFlow(JobListing job, CancellationToken ct)
    {
        var easyApplySelectors = new[]
        {
            "button[aria-label*='Easy Apply' i]",
            "button:has-text('Easy Apply')",
            ".jobs-easy-apply-button",
            "button[data-control-name='easy_apply']"
        };

        foreach (var selector in easyApplySelectors)
        {
            if (await playwrightService.IsVisibleAsync(selector))
            {
                logger.LogInformation("Clicking Easy Apply button with selector '{Selector}'", selector);
                await playwrightService.ClickAsync(selector);
                await Task.Delay(1500, ct);

                await FillEasyApplyForms(job, ct);
                return;
            }
        }

        logger.LogWarning("Easy Apply button not found for job {JobId}", job.Id);
    }

    private async Task FillEasyApplyForms(JobListing job, CancellationToken ct)
    {
        var maxSteps = 10;

        for (var step = 0; step < maxSteps; step++)
        {
            if (ct.IsCancellationRequested) break;

            var nextButton = await FindNextButton();
            var submitButton = await FindSubmitButton();

            if (submitButton is not null)
            {
                logger.LogInformation("Submitting Easy Apply form at step {Step}", step + 1);

                if (job.ApplicationAnswers is not null)
                {
                    await FillFormFields(job.ApplicationAnswers, ct);
                }

                await playwrightService.ClickAsync(submitButton);
                await Task.Delay(2000, ct);
                return;
            }

            if (nextButton is not null)
            {
                if (job.ApplicationAnswers is not null)
                {
                    await FillFormFields(job.ApplicationAnswers, ct);
                }

                await playwrightService.ClickAsync(nextButton);
                await Task.Delay(1500, ct);
            }
            else
            {
                logger.LogWarning("No navigation button found at step {Step}", step + 1);
                break;
            }
        }
    }

    private async Task<string?> FindNextButton()
    {
        var nextSelectors = new[]
        {
            "button[aria-label='Next']",
            "button:has-text('Next')",
            ".artdeco-button--primary:has-text('Next')"
        };

        foreach (var selector in nextSelectors)
        {
            if (await playwrightService.IsVisibleAsync(selector))
                return selector;
        }

        return null;
    }

    private async Task<string?> FindSubmitButton()
    {
        var submitSelectors = new[]
        {
            "button[aria-label='Submit']",
            "button:has-text('Submit')",
            "button[aria-label='Review']",
            "button:has-text('Review')",
            ".artdeco-button--primary:has-text('Submit')"
        };

        foreach (var selector in submitSelectors)
        {
            if (await playwrightService.IsVisibleAsync(selector))
                return selector;
        }

        return null;
    }

    private async Task FillFormFields(Dictionary<string, string> answers, CancellationToken ct)
    {
        foreach (var (question, answer) in answers)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var escapedQuestion = question.Replace("'", "\\'");
                var inputSelector = $"input[aria-label*='{escapedQuestion}' i]";
                var textareaSelector = $"textarea[aria-label*='{escapedQuestion}' i]";
                var selectSelector = $"select[aria-label*='{escapedQuestion}' i]";

                if (await playwrightService.IsVisibleAsync(inputSelector))
                {
                    await playwrightService.TypeAsync(inputSelector, answer);
                }
                else if (await playwrightService.IsVisibleAsync(textareaSelector))
                {
                    await playwrightService.TypeAsync(textareaSelector, answer);
                }
                else if (await playwrightService.IsVisibleAsync(selectSelector))
                {
                    await playwrightService.SelectOptionAsync(selectSelector, answer);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fill field for question '{Question}'", question);
            }
        }
    }
}
