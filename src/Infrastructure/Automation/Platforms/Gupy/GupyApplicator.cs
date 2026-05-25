using AutoApplicator.Application.Interfaces;
using AutoApplicator.Application.Commands.Questions;
using AutoApplicator.Application.Queries.Questions;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Models;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Models;
using AutoApplicator.Infrastructure.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Gupy;

public sealed class GupyApplicator : IJobApplicator
{
    private readonly ILogger<GupyApplicator> _logger;
    private readonly IHumanBehavior _behavior;
    private readonly IMediator _mediator;
    private readonly IEnumerable<IFieldFiller> _fieldFillers;
    private readonly IEnumerable<ISuccessDetector> _successDetectors;
    private readonly Dictionary<string, string> _answersUsed = [];

    public PlatformType Platform => PlatformType.Gupy;

    async Task<ApplyResult> IJobApplicator.ApplyAsync(IBrowserPage page, JobListing job)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        return await ApplyAsync(innerPage, job);
    }

    public GupyApplicator(
        ILogger<GupyApplicator> logger,
        IHumanBehavior behavior,
        IMediator mediator,
        IEnumerable<IFieldFiller> fieldFillers,
        IEnumerable<ISuccessDetector> successDetectors)
    {
        _logger = logger;
        _behavior = behavior;
        _mediator = mediator;
        _fieldFillers = fieldFillers;
        _successDetectors = successDetectors;
    }

    public async Task<ApplyResult> ApplyAsync(IPage page, JobListing job)
    {
        _answersUsed.Clear();

        try
        {
            await NavigateToJobUrlAsync(page, job);
            if (HasLoginRedirect(page, job, out var loginResult)) return loginResult;

            await ScrollToApplyButtonAsync(page);
            if (await TryClickApplyButtonAsync(page, job) is false)
                return new ApplyResult(false, "Apply button not found");

            if (HasLoginRedirect(page, job, out var postClickLoginResult)) return postClickLoginResult;

            return await FillFormStepsAsync(page, job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Title}] Gupy application failed", job.Title);
            return new ApplyResult(false, ex.Message);
        }
    }

    private async Task NavigateToJobUrlAsync(IPage page, JobListing job)
    {
        _logger.LogInformation("[{Title}] Navigating to Gupy job page...", job.Title);
        await page.GotoAsync(job.Url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await _behavior.DelayAsync(2000, 3000);
    }

    private static bool HasLoginRedirect(IPage page, JobListing job, out ApplyResult result)
    {
        if (page.Url.Contains(GupySelectors.LoginPageIndicator))
        {
            result = new ApplyResult(false, "Gupy login required. Please log in first.", true);
            return true;
        }
        result = null!;
        return false;
    }

    private async Task ScrollToApplyButtonAsync(IPage page)
    {
        await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
        await _behavior.DelayAsync(1000, 1500);
    }

    private async Task<bool> TryClickApplyButtonAsync(IPage page, JobListing job)
    {
        var applyBtn = await page.QuerySelectorAsync(GupySelectors.ApplyButton);
        if (applyBtn is null)
        {
            _logger.LogWarning("[{Title}] Apply button not found", job.Title);
            return false;
        }

        await applyBtn.ClickAsync();
        await _behavior.DelayAsync(3000, 4000);
        return true;
    }

    private async Task<ApplyResult> FillFormStepsAsync(IPage page, JobListing job)
    {
        var maxSteps = 10;
        for (var step = 0; step < maxSteps; step++)
        {
            var hasUnansweredRequired = await ProcessFormStepAsync(page, job, step);
            if (hasUnansweredRequired)
            {
                _logger.LogInformation("[{Title}] ⏭️ Skipping — configure answers in Questions tab", job.Title);
                return new ApplyResult(false, "Pending questions need configuration", true);
            }

            await _behavior.DelayAsync(500, 1000);

            var submitResult = await HandleSubmitAsync(page, job);
            if (submitResult is not null)
                return submitResult;

            await _behavior.DelayAsync(1000, 2000);
        }

        return new ApplyResult(false, $"Exceeded maximum steps ({maxSteps})");
    }

    private async Task<bool> ProcessFormStepAsync(IPage page, JobListing job, int step)
    {
        _logger.LogInformation("[{Title}] === Gupy Step {Step} ===", job.Title, step + 1);

        var fields = await ExtractFormFieldsAsync(page);
        if (fields.Count > 0)
        {
            _logger.LogInformation("[{Title}] Step {Step}: {FieldCount} field(s): {Fields}",
                job.Title, step + 1, fields.Count,
                string.Join(", ", fields.Select(f => $"{f.Label} ({f.Type})")));
        }

        var hasUnansweredRequired = false;
        foreach (var field in fields)
        {
            var filled = await FillFieldAsync(page, field, job);
            if (!filled)
            {
                _logger.LogWarning("[{Title}] ❌ Unanswered required field: \"{Field}\"", job.Title, field.Label);
                if (field.Required) hasUnansweredRequired = true;
            }
            else
            {
                _logger.LogInformation("[{Title}] ✅ Filled: \"{Field}\"", job.Title, field.Label);
            }
        }

        return hasUnansweredRequired;
    }

    private async Task<List<FormField>> ExtractFormFieldsAsync(IPage page)
    {
        var fields = new List<FormField>();

        var formRoot = page.Locator("form, [data-testid=\"apply-form\"], .curriculum-content, .sc-hOynoF").First;
        if (!await formRoot.IsVisibleAsync())
            formRoot = page.Locator("body").First;

        foreach (var filler in _fieldFillers)
        {
            var extracted = await filler.ExtractAsync(page, formRoot);
            fields.AddRange(extracted);
        }

        return fields;
    }

    private async Task<bool> FillFieldAsync(IPage page, FormField field, JobListing job)
    {
        _logger.LogInformation("[{Title}] Filling field \"{Field}\"", job.Title, field.Label);

        var existing = await _mediator.Send(new FindQuestionByTextQuery(field.Label));

        if (existing is not null && !string.IsNullOrEmpty(existing.Answer))
        {
            _logger.LogInformation("[Questions] Using saved answer: \"{Label}\" = \"{Answer}\"", field.Label, existing.Answer);
            _answersUsed[field.Label] = existing.Answer;
            await FillFieldValueAsync(page, field, existing.Answer);
            return true;
        }

        if (existing is not null)
        {
            _logger.LogInformation("[Questions] No answer configured, skipping: \"{Label}\"", field.Label);
            return false;
        }

        var questionFieldType = field.Type switch
        {
            FormFieldType.Radio => QuestionFieldType.Radio,
            FormFieldType.Text => QuestionFieldType.Textarea,
            FormFieldType.Typeahead => QuestionFieldType.Textarea,
            _ => QuestionFieldType.Textarea
        };

        await _mediator.Send(new UpsertQuestionCommand(
            QuestionText: field.Label,
            FieldType: questionFieldType,
            Options: field.Options,
            Platform: PlatformType.Gupy,
            JobTitle: job.Title,
            Company: job.Company,
            Group: "Gupy Application"));

        if (field.Required) return false;
        return true;
    }

    private async Task FillFieldValueAsync(IPage page, FormField field, string answer)
    {
        var filler = _fieldFillers.FirstOrDefault(f => f.FieldType == field.Type);
        if (filler is not null)
        {
            await filler.FillAsync(page, field, answer);
        }
        else
        {
            _logger.LogWarning("No filler found for field type: {Type}", field.Type);
        }
    }

    private async Task<bool> TryClickWithFallbackStrategiesAsync(IPage page, IElementHandle element, string title)
    {
        bool clicked = false;
        try
        {
            await element.ClickAsync();
            clicked = true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[{Title}] Normal click failed: {Error}", title, ex.Message);
        }

        if (!clicked)
        {
            try
            {
                await element.ClickAsync(new() { Force = true });
                clicked = true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[{Title}] Force click failed: {Error}", title, ex.Message);
            }
        }

        if (!clicked)
        {
            try
            {
                await page.EvaluateAsync(@"(el) => el.click()", element);
                clicked = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[{Title}] All click strategies failed: {Error}", title, ex.Message);
            }
        }

        return clicked;
    }

    private async Task<ApplyResult?> HandleSubmitAsync(IPage page, JobListing job)
    {
        var hasSubmitBtn = await TryClickSubmitButtonAsync(page, job);

        await TryClickFinishApplicationButtonAsync(page, job);

        var successResult = await TryDetectSubmissionSuccessAsync(page, job);
        if (successResult is not null)
            return successResult;

        if (hasSubmitBtn)
        {
            var nextSubmit = await page.QuerySelectorAsync(GupySelectors.SubmitButton);
            if (nextSubmit is not null)
            {
                _logger.LogInformation("[{Title}] More steps to go...", job.Title);
                return null;
            }
        }

        return null;
    }

    private async Task<bool> TryClickSubmitButtonAsync(IPage page, JobListing job)
    {
        var submitBtn = await page.QuerySelectorAsync(GupySelectors.SubmitButton)
                    ?? await page.QuerySelectorAsync(GupySelectors.ContinueButton)
                    ?? await page.QuerySelectorAsync(GupySelectors.ResponderAgoraButton);

        if (submitBtn is not null)
        {
            _logger.LogInformation("[{Title}] Clicking submit/continue...", job.Title);
            await TryClickWithFallbackStrategiesAsync(page, submitBtn, job.Title);
            await _behavior.DelayAsync(1000, 1500);
            return true;
        }

        return false;
    }

    private async Task TryClickFinishApplicationButtonAsync(IPage page, JobListing job)
    {
        try
        {
            var finalizarBtn = page.Locator("button:has-text(\"Finalizar candidatura\"), button#dialog-give-up-personalization-step").First;
            await finalizarBtn.WaitForAsync(new() { Timeout = 1500 });

            if (await finalizarBtn.IsVisibleAsync())
            {
                _logger.LogInformation("[{Title}] 'Finalizar candidatura' detected! Clicking...", job.Title);

                try { await finalizarBtn.ClickAsync(); }
                catch (Exception innerEx)
                {
                    _logger.LogDebug(innerEx, "Normal click failed, trying Force click");
                    await finalizarBtn.ClickAsync(new() { Force = true });
                }

                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[{Title}] 'Finalizar candidatura' button not yet available", job.Title);
        }
    }

    private async Task<ApplyResult?> TryDetectSubmissionSuccessAsync(IPage page, JobListing job)
    {
        try
        {
            var successTitle = page.Locator(GupySelectors.ApplicationSuccessTitle).First;
            await successTitle.WaitForAsync(new() { Timeout = 2000 });
            if (await successTitle.IsVisibleAsync())
            {
                _logger.LogInformation("[{Title}] ✅ Candidatura finalizada com sucesso!", job.Title);
                return new ApplyResult(true, "Application submitted", false, _answersUsed);
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "[{Title}] Success confirmation not yet visible", job.Title); }

        return null;
    }

    private async Task<bool> CheckSubmissionSuccessAsync(IPage page)
    {
        await _behavior.DelayAsync(2000, 3000);

        foreach (var detector in _successDetectors.Where(d => d.Platform == PlatformType.Gupy))
        {
            if (await detector.DetectAsync(page))
            {
                _logger.LogInformation("[Success] Detected by {Detector}", detector.GetType().Name);
                return true;
            }
        }
        return false;
    }
}
