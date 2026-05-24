using AutoApplicator.Application.Commands.Questions;
using AutoApplicator.Application.Queries.Questions;
using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.StepNavigators;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;

public sealed class LinkedInApplicator
{
    private readonly IEnumerable<IFieldFiller> _fieldFillers;
    private readonly IEnumerable<IStepNavigator> _stepNavigators;
    private readonly IEnumerable<ISuccessDetector> _successDetectors;
    private readonly IHumanBehavior _behavior;
    private readonly ILogger<LinkedInApplicator> _logger;
    private readonly IMediator _mediator;
    private readonly Dictionary<string, string> _answersUsed = [];

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
            await _behavior.DelayAsync(1000, 2000);

            var maxSteps = 10;
            for (var step = 0; step < maxSteps; step++)
            {
                var (fields, hasUnansweredRequired) = await ProcessFormStepAsync(page, job, step);

                if (hasUnansweredRequired)
                    return await HandleUnansweredRequiredAsync(page, job);

                await HandleFileUploadAsync(page, fields);

                await _behavior.DelayAsync(500, 1000);

                var submitResult = await HandleSubmitStepAsync(page, job, fields);
                if (submitResult is not null)
                    return submitResult;

                var advanceResult = await HandleAdvanceStepAsync(page, job);
                if (advanceResult is not null)
                    return advanceResult;

                await _behavior.DelayAsync(1000, 2000);
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
                if (field.Required) hasUnansweredRequired = true;
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
        _logger.LogInformation("[{Title}] ⏭️ Skipping — configure answers in Questions tab", job.Title);
        await CloseModalAsync(page);
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

        await _behavior.DelayAsync(3000, 4000);

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
                await _behavior.DelayAsync(3000, 4000);
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
        var alreadyOpen = await page.WaitForAnySelectorAsync( LinkedInSelectors.ModalContainer, 1000);
        if (alreadyOpen is not null) return true;

        var selector = await page.WaitForAnySelectorAsync( LinkedInSelectors.EasyApplyButton, 3000);
        if (selector is null) return false;

        await _behavior.HumanClickAsync(page, selector);
        await _behavior.DelayAsync(1000, 2000);

        var modalOpen = await page.WaitForAnySelectorAsync( LinkedInSelectors.ModalContainer, 3000);
        return modalOpen is not null;
    }

    private async Task<(List<FormField> Fields, string StepTitle)> ExtractFormFieldsAsync(IPage page)
    {
        await _behavior.DelayAsync(1000, 1500);

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
            catch { /* try next selector */ }
        }

        return "";
    }

    private async Task<bool> FillFieldAsync(IPage page, FormField field, JobListing job, string stepTitle)
    {
        _logger.LogInformation("[{Title}] Step '{StepTitle}': Filling field \"{Field}\"", job.Title, stepTitle, field.Label);

        if (field.Type == FormFieldType.File) return true;

        var questionFieldType = MapFieldType(field.Type);

        // Envia comando para upsert via MediatR (persistência desacoplada)
        await _mediator.Send(new UpsertQuestionCommand(
            QuestionText: field.Label,
            FieldType: questionFieldType,
            Options: field.Options,
            Platform: PlatformType.LinkedIn,
            JobTitle: job.Title,
            Company: job.Company,
            Group: stepTitle));

        // Busca a pergunta (incluindo Answer) via query
        var existing = await _mediator.Send(new FindQuestionByTextQuery(field.Label));

        if (existing is not null && !string.IsNullOrEmpty(existing.Answer))
            return await FillWithSavedAnswerAsync(page, field, existing);

        if (existing is not null)
        {
            _logger.LogInformation("[Questions] Sem resposta configurada, pulando: \"{Label}\"", field.Label);
            return false;
        }

        return HandlePrefilledField(field);
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
        _logger.LogInformation("[Questions] Usando resposta salva: \"{Label}\" = \"{Answer}\"", field.Label, existing.Answer);
        _answersUsed[field.Label] = existing.Answer;
        await FillFieldValueAsync(page, field, existing.Answer);
        return true;
    }

    private static bool HandlePrefilledField(FormField field)
    {
        // New question: if field is already pre-filled by LinkedIn (and not a select), skip it
        if (!string.IsNullOrEmpty(field.CurrentValue) && field.Type != FormFieldType.Select)
        {
            return true;
        }

        // No saved answer available, check if required
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

    private async Task UploadResumeAsync(IPage page, string resumePath)
    {
        try
        {
            var fileInput = await page.QuerySelectorAsync(".jobs-easy-apply-modal input[type=\"file\"], .artdeco-modal input[type=\"file\"]");
            if (fileInput is not null)
            {
                await fileInput.SetInputFilesAsync(resumePath);
                _logger.LogInformation("Uploaded resume: {Path}", resumePath);
                await _behavior.DelayAsync(1000, 2000);
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
        await _behavior.DelayAsync(2000, 3000);

        foreach (var detector in _successDetectors)
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
            var dismissSel = await page.WaitForAnySelectorAsync(LinkedInSelectors.DismissButton, 2000);
            if (dismissSel is not null)
            {
                await _behavior.HumanClickAsync(page, dismissSel);
                await _behavior.DelayAsync(500, 1000);

                var discardSel = await page.WaitForAnySelectorAsync(LinkedInSelectors.DiscardButton, 2000);
                if (discardSel is not null)
                    await _behavior.HumanClickAsync(page, discardSel);
            }
        }
        catch { /* close modal, ignore error */ }
    }

}
