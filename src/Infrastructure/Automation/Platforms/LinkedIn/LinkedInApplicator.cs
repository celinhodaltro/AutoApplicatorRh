using AutoApplicator.Application.Interfaces;
using AutoApplicator.Application.Commands.Questions;
using AutoApplicator.Application.Queries.Questions;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Models;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.StepNavigators;
using AutoApplicator.Infrastructure.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;

public sealed class LinkedInApplicator : IJobApplicator
{
    private readonly IEnumerable<IFieldFiller> _fieldFillers;
    private readonly IEnumerable<IStepNavigator> _stepNavigators;
    private readonly IEnumerable<ISuccessDetector> _successDetectors;
    private readonly IHumanBehavior _behavior;
    private readonly ILogger<LinkedInApplicator> _logger;
    private readonly IMediator _mediator;
    private readonly Dictionary<string, string> _answersUsed = [];

    public PlatformType Platform => PlatformType.LinkedIn;

    async Task<ApplyResult> IJobApplicator.ApplyAsync(IBrowserPage page, JobListing job)
    {
        var innerPage = ((PlaywrightPageAdapter)page).InnerPage;
        return await ApplyAsync(innerPage, job);
    }

    public LinkedInApplicator(
        IEnumerable<IFieldFiller> fieldFillers,
        IEnumerable<IStepNavigator> stepNavigators,
        IEnumerable<ISuccessDetector> successDetectors,
        IHumanBehavior behavior,
        ILogger<LinkedInApplicator> logger,
        IMediator mediator)
    {
        _fieldFillers = fieldFillers;
        _stepNavigators = stepNavigators;
        _successDetectors = successDetectors;
        _behavior = behavior;
        _logger = logger;
        _mediator = mediator;
    }

    public async Task<ApplyResult> ApplyAsync(IPage page, JobListing job)
    {
        _answersUsed.Clear();

        try
        {
            _logger.LogInformation("[{Title}] Opening Easy Apply modal...", job.Title);
            var opened = await OpenEasyApplyModalAsync(page);
            if (!opened)
            {
                _logger.LogWarning("[{Title}] Easy Apply button not found", job.Title);
                return new ApplyResult(false, "Could not find Easy Apply button");
            }

            _logger.LogInformation("[{Title}] Modal opened, starting form fill...", job.Title);
            await _behavior.DelayAsync(200, 500);

            var maxSteps = 6;
            for (var step = 0; step < maxSteps; step++)
            {
                var (fields, hasUnansweredRequired) = await ProcessFormStepAsync(page, job, step);

                if (hasUnansweredRequired)
                    return await HandleUnansweredRequiredAsync(page, job);

                await HandleFileUploadAsync(page, fields);

                await _behavior.DelayAsync(200, 500);

                var submitResult = await HandleSubmitStepAsync(page, job, fields);
                if (submitResult is not null)
                    return submitResult;

                var advanceResult = await HandleAdvanceStepAsync(page, job);
                if (advanceResult is not null)
                    return advanceResult;

                await _behavior.DelayAsync(200, 500);
            }

            await CloseModalAsync(page);
            return new ApplyResult(false, $"Exceeded maximum steps ({maxSteps})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Title}] Application failed", job.Title);
            await CloseModalAsync(page).ConfigureAwait(false);
            return new ApplyResult(false, ex.Message);
        }
    }

    private async Task<(List<FormField> Fields, bool HasUnansweredRequired)> ProcessFormStepAsync(
        IPage page, JobListing job, int step)
    {
        _logger.LogInformation("[{Title}] === Step {Step} ===", job.Title, step + 1);

        var (fields, stepTitle) = await ExtractFormFieldsAsync(page);
        if (fields.Count > 0)
        {
            _logger.LogInformation("[{Title}] Step {Step}: {FieldCount} field(s): {Fields}",
                job.Title, step + 1, fields.Count,
                string.Join(", ", fields.Select(f => $"{f.Label} ({f.Type})")));
        }

        var hasUnansweredRequired = false;
        foreach (var field in fields)
        {
            var filled = await FillFieldAsync(page, field, job, stepTitle);
            if (!filled)
            {
                _logger.LogWarning("[{Title}] ❌ Unanswered required field: \"{Field}\"", job.Title, field.Label);
                if (field.Required)
                {
                    hasUnansweredRequired = true;
                    break; // ← Stop on first unanswered required field
                }
            }
            else
            {
                _logger.LogInformation("[{Title}] ✅ Filled: \"{Field}\"", job.Title, field.Label);
            }
        }

        return (fields, hasUnansweredRequired);
    }

    private async Task<ApplyResult> HandleUnansweredRequiredAsync(IPage page, JobListing job)
    {
        _logger.LogInformation("[{Title}] ⏏️ Skipping — unanswered question found. Moving to next job.", job.Title);
        await CloseModalQuicklyAsync(page);
        return new ApplyResult(false, "Pending questions need configuration", true);
    }

    private async Task HandleFileUploadAsync(IPage page, List<FormField> fields)
    {
        var fileFields = fields.Where(f => f.Type == FormFieldType.File).ToList();
        if (fileFields.Count > 0)
        {
            var resumePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoApplicator", "resume.pdf");
            if (File.Exists(resumePath))
                await UploadResumeAsync(page, resumePath);
        }
    }

    private async Task<ApplyResult?> HandleSubmitStepAsync(IPage page, JobListing job, List<FormField> fields)
    {
        if (fields.Count != 0)
            return null;

        var submitNav = _stepNavigators.FirstOrDefault(s => s is SubmitStepNavigator);
        if (submitNav is null || !await submitNav.CanNavigateAsync(page))
            return null;

        _logger.LogInformation("[{Title}] Submit button found! Submitting...", job.Title);

        var result = await submitNav.NavigateAsync(page);
        if (result == StepResult.Error)
        {
            _logger.LogWarning("[{Title}] ❌ Submit click failed", job.Title);
            return new ApplyResult(false, "Submit click failed");
        }

        await _behavior.DelayAsync(800, 1200);

        if (await CheckSubmissionSuccessAsync(page))
        {
            _logger.LogInformation("[{Title}] ✅ APPLICATION SUBMITTED SUCCESSFULLY!", job.Title);
            return new ApplyResult(true, "Application submitted", false, _answersUsed);
        }

        _logger.LogWarning("[{Title}] ❌ Submit failed — no confirmation", job.Title);
        return new ApplyResult(false, "No confirmation detected");
    }

    private async Task<ApplyResult?> HandleAdvanceStepAsync(IPage page, JobListing job)
    {
        var result = await AdvanceStepAsync(page);
        switch (result)
        {
            case StepResult.Submit:
                _logger.LogInformation("[{Title}] 📤 On submit step", job.Title);
                var submitNav = _stepNavigators.First(s => s is SubmitStepNavigator);
                await submitNav.NavigateAsync(page);
                await _behavior.DelayAsync(800, 1200);
                if (await CheckSubmissionSuccessAsync(page))
                {
                    _logger.LogInformation("[{Title}] ✅ APPLICATION SUBMITTED SUCCESSFULLY!", job.Title);
                    return new ApplyResult(true, "Application submitted", false, _answersUsed);
                }
                return new ApplyResult(false, "No confirmation detected");
            case StepResult.Error:
                _logger.LogWarning("[{Title}] ❌ Error advancing step", job.Title);
                return new ApplyResult(false, "Error advancing step");
            case StepResult.Next:
            case StepResult.Review:
                _logger.LogInformation("[{Title}] ➡️ Advanced to next step", job.Title);
                break;
        }
        return null;
    }

    private async Task<bool> OpenEasyApplyModalAsync(IPage page)
    {
        var alreadyOpen = await page.WaitForAnySelectorAsync( LinkedInSelectors.ModalContainer, 500);
        if (alreadyOpen is not null) return true;

        var selector = await page.WaitForAnySelectorAsync( LinkedInSelectors.EasyApplyButton, 1500);
        if (selector is null) return false;

        await _behavior.HumanClickAsync(page, selector);
        await _behavior.DelayAsync(200, 500);

        var modalOpen = await page.WaitForAnySelectorAsync( LinkedInSelectors.ModalContainer, 1500);
        return modalOpen is not null;
    }

    private async Task<(List<FormField> Fields, string StepTitle)> ExtractFormFieldsAsync(IPage page)
    {
        await _behavior.DelayAsync(100, 300);

        var formRoot = page.Locator(".jobs-easy-apply-modal, .artdeco-modal, [data-test-modal-id=\"easy-apply-modal\"]").First;
        var modalExists = await formRoot.IsVisibleAsync();
        if (!modalExists) return ([], "");

        var stepTitle = await ExtractStepTitleAsync(page);

        var fields = new List<FormField>();
        foreach (var filler in _fieldFillers)
        {
            var extracted = await filler.ExtractAsync(page, formRoot);
            fields.AddRange(extracted);
        }

        return (fields, stepTitle);
    }

    private async Task<string> ExtractStepTitleAsync(IPage page)
    {
        var stepSelectors = LinkedInSelectors.StepTitleSelectors;

        foreach (var sel in stepSelectors)
        {
            try
            {
                var title = await page.Locator(sel).First.InnerTextAsync(new() { Timeout = 500 });
                if (!string.IsNullOrWhiteSpace(title))
                {
                    var stepTitle = title.Trim();
                    _logger.LogDebug("Step title extracted via '{Selector}': \"{Title}\"", sel, stepTitle);
                    return stepTitle;
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Step title not found via selector '{Selector}'", sel); }
        }

        return "";
    }

    private async Task<bool> FillFieldAsync(IPage page, FormField field, JobListing job, string stepTitle)
    {
        _logger.LogInformation("[{Title}] Step '{StepTitle}': Filling field \"{Field}\"", job.Title, stepTitle, field.Label);

        if (field.Type == FormFieldType.File) return true;

        var questionFieldType = MapFieldType(field.Type);

        await _mediator.Send(new UpsertQuestionCommand(
            QuestionText: field.Label,
            FieldType: questionFieldType,
            Options: field.Options,
            Platform: PlatformType.LinkedIn,
            JobTitle: job.Title,
            Company: job.Company,
            Group: stepTitle));

        var existing = await _mediator.Send(new FindQuestionByTextQuery(field.Label));

        if (existing is not null && !string.IsNullOrEmpty(existing.Answer))
            return await FillWithSavedAnswerAsync(page, field, existing);

        if (existing is not null)
        {
            _logger.LogInformation("[Questions] No answer configured, skipping: \"{Label}\"", field.Label);
            return false;
        }

        return IsFieldPrefilledOrOptional(field);
    }

    private static QuestionFieldType MapFieldType(FormFieldType fieldType)
    {
        return fieldType switch
        {
            FormFieldType.Text => QuestionFieldType.Textarea,
            FormFieldType.Textarea => QuestionFieldType.Textarea,
            FormFieldType.Select => QuestionFieldType.Select,
            FormFieldType.Radio => QuestionFieldType.Radio,
            FormFieldType.Checkbox => QuestionFieldType.Checkbox,
            FormFieldType.Typeahead => QuestionFieldType.Textarea,
            _ => QuestionFieldType.Textarea
        };
    }

    private async Task<bool> FillWithSavedAnswerAsync(IPage page, FormField field, CollectedQuestion existing)
    {
        _logger.LogInformation("[Questions] Using saved answer: \"{Label}\" = \"{Answer}\"", field.Label, existing.Answer);
        _answersUsed[field.Label] = existing.Answer;
        await FillFieldValueAsync(page, field, existing.Answer);
        return true;
    }

    private static bool IsFieldPrefilledOrOptional(FormField field)
    {
        if (!string.IsNullOrEmpty(field.CurrentValue) && field.Type != FormFieldType.Select)
            return true;

        return !field.Required;
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

    private async Task UploadResumeAsync(IPage page, string resumePath)
    {
        try
        {
            var fileInput = await page.QuerySelectorAsync(".jobs-easy-apply-modal input[type=\"file\"], .artdeco-modal input[type=\"file\"]");
            if (fileInput is not null)
            {
                await fileInput.SetInputFilesAsync(resumePath);
                _logger.LogInformation("Uploaded resume: {Path}", resumePath);
                await _behavior.DelayAsync(200, 400);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resume upload failed");
        }
    }

    private async Task<StepResult> AdvanceStepAsync(IPage page)
    {
        foreach (var navigator in _stepNavigators)
        {
            if (await navigator.CanNavigateAsync(page))
                return await navigator.NavigateAsync(page);
        }
        return StepResult.Error;
    }

    private async Task<bool> CheckSubmissionSuccessAsync(IPage page)
    {
        await _behavior.DelayAsync(500, 800);

        foreach (var detector in _successDetectors.Where(d => d.Platform == PlatformType.LinkedIn))
        {
            if (await detector.DetectAsync(page))
            {
                _logger.LogInformation("[Success] Detected by {Detector}", detector.GetType().Name);
                return true;
            }
        }
        return false;
    }

    private async Task CloseModalAsync(IPage page)
    {
        try
        {
            var dismissSel = await page.WaitForAnySelectorAsync(LinkedInSelectors.DismissButton, 1000);
            if (dismissSel is not null)
            {
                await _behavior.HumanClickAsync(page, dismissSel);
                await _behavior.DelayAsync(500, 1000);

                var discardSel = await page.WaitForAnySelectorAsync(LinkedInSelectors.DiscardButton, 1000);
                if (discardSel is not null)
                    await _behavior.HumanClickAsync(page, discardSel);
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to close LinkedIn modal (non-critical)"); }
    }

    private async Task CloseModalQuicklyAsync(IPage page)
    {
        try
        {
            // Fast close — no human delays, just dismiss immediately
            var dismissSel = await page.WaitForAnySelectorAsync(LinkedInSelectors.DismissButton, 500);
            if (dismissSel is not null)
            {
                await page.EvaluateAsync(@"(selector) => {
                    const btn = document.querySelector(selector);
                    if (btn) btn.click();
                }", dismissSel);

                // Brief wait for discard dialog if it appears
                var discardSel = await page.WaitForAnySelectorAsync(LinkedInSelectors.DiscardButton, 500);
                if (discardSel is not null)
                {
                    await page.EvaluateAsync(@"(selector) => {
                        const btn = document.querySelector(selector);
                        if (btn) btn.click();
                    }", discardSel);
                }
            }
        }
        catch { /* ignore close errors */ }
    }

}
