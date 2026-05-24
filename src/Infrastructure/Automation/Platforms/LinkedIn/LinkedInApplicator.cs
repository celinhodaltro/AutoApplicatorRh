using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;

public sealed class LinkedInApplicator
{
    private readonly ILogger<LinkedInApplicator> _logger;
    private readonly HumanBehavior _behavior;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<string, string> _answersUsed = [];

    private static readonly string[] EasyApplyButton =
    [
        "button.jobs-apply-button",
        "a.jobs-apply-button",
        "button[aria-label*=\"Easy Apply\"]",
        "a[aria-label*=\"Easy Apply\"]",
        "button[aria-label*=\"Candidatura\"]",
        "a[aria-label*=\"Candidatura\"]",
        "button[aria-label*=\"candidatura\"]",
        "a[aria-label*=\"candidatura\"]",
        "a[href*=\"apply/?openSDUIApplyFlow\"]",
        ".jobs-apply-button--top-card button",
        ".jobs-apply-button--top-card a",
        "button.jobs-s-apply",
        "a.jobs-s-apply"
    ];

    private static readonly string[] ModalContainer =
    [
        ".jobs-easy-apply-modal",
        "[data-test-modal-id=\"easy-apply-modal\"]",
        ".artdeco-modal--layer-default"
    ];

    private static readonly string[] NextButton =
    [
        "button[aria-label=\"Continue to next step\"]",
        "button[aria-label=\"Avançar para próxima etapa\"]",
        "button[aria-label*=\"Avançar\"]",
        "button[aria-label*=\"Continuar\"]",
        "button[aria-label*=\"Próxima\"]",
        "button:has-text(\"Avançar\")",
        "button:has-text(\"Continuar\")",
        "button[data-easy-apply-next-button]",
        "button[data-easy-apply-next-button]:not([disabled])",
        ".artdeco-modal footer button.artdeco-button--primary:not([disabled])"
    ];

    private static readonly string[] ReviewButton =
    [
        "button[aria-label=\"Review your application\"]",
        "button[aria-label=\"Revise sua candidatura\"]",
        "button[data-easy-apply-review-button]",
        "button[data-live-test-easy-apply-review-button]",
        ".artdeco-modal footer button:has-text(\"Revisar\")",
        ".artdeco-modal footer button:has-text(\"Review\")",
        ".artdeco-modal footer button:has-text(\"Rever\")"
    ];

    private static readonly string[] SubmitButton =
    [
        "button[aria-label=\"Submit application\"]",
        "button[aria-label=\"Enviar candidatura\"]",
        "button[data-easy-apply-submit-button]",
        "button[data-live-test-easy-apply-submit-button]",
        ".artdeco-modal footer button.artdeco-button--primary:last-of-type:has-text(\"Enviar\")",
        ".artdeco-modal footer button.artdeco-button--primary:last-of-type:has-text(\"Submit\")"
    ];

    private static readonly string[] DismissButton =
    [
        "button[aria-label=\"Dismiss\"]",
        "button.artdeco-modal__dismiss",
        ".artdeco-modal__dismiss"
    ];

    private static readonly string[] DiscardButton =
    [
        "button[data-test-dialog-primary-btn]",
        "button[data-control-name=\"discard_application_confirm_btn\"]"
    ];

    public LinkedInApplicator(ILogger<LinkedInApplicator> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _behavior = new HumanBehavior();
        _scopeFactory = scopeFactory;
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

                if (hasUnansweredRequired)
                {
                    _logger.LogInformation("[{Title}] ⏭️ Skipping — configure answers in Questions tab", job.Title);
                    await CloseModalAsync(page);
                    return new ApplyResult(false, "Pending questions need configuration", true);
                }

                // File upload
                var fileFields = fields.Where(f => f.Type == FormFieldType.File).ToList();
                if (fileFields.Count > 0)
                {
                    var resumePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "AutoApplicator", "resume.pdf");
                    if (File.Exists(resumePath))
                        await UploadResumeAsync(page, resumePath);
                }

                await _behavior.DelayAsync(500, 1000);

                // Check if submit step (no fields + submit button)
                if (fields.Count == 0 && await IsSubmitStepAsync(page))
                {
                    _logger.LogInformation("[{Title}] Submit button found! Submitting...", job.Title);
                    await ClickSubmitAsync(page);
                    await _behavior.DelayAsync(3000, 4000);

                    if (await CheckSubmissionSuccessAsync(page))
                    {
                        _logger.LogInformation("[{Title}] ✅ APPLICATION SUBMITTED SUCCESSFULLY!", job.Title);
                        return new ApplyResult(true, "Application submitted", false, _answersUsed);
                    }
                    _logger.LogWarning("[{Title}] ❌ Submit failed — no confirmation", job.Title);
                    return new ApplyResult(false, "No confirmation detected");
                }

                // Advance to next step
                var result = await AdvanceStepAsync(page);
                switch (result)
                {
                    case StepResult.Submit:
                        _logger.LogInformation("[{Title}] 📤 On submit step", job.Title);
                        await ClickSubmitAsync(page);
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

    private async Task<bool> OpenEasyApplyModalAsync(IPage page)
    {
        var alreadyOpen = await WaitForAnySelectorAsync(page, ModalContainer, 1000);
        if (alreadyOpen is not null) return true;

        var selector = await WaitForAnySelectorAsync(page, EasyApplyButton, 3000);
        if (selector is null) return false;

        await _behavior.HumanClickAsync(page, selector);
        await _behavior.DelayAsync(1000, 2000);

        var modalOpen = await WaitForAnySelectorAsync(page, ModalContainer, 3000);
        return modalOpen is not null;
    }

    private async Task<(List<FormField> Fields, string StepTitle)> ExtractFormFieldsAsync(IPage page)
    {
        var fields = new List<FormField>();
        await _behavior.DelayAsync(1000, 1500);

        var formRoot = page.Locator(".jobs-easy-apply-modal, .artdeco-modal, [data-test-modal-id=\"easy-apply-modal\"]").First;
        var modalExists = await formRoot.IsVisibleAsync();
        if (!modalExists) return ([], "");

        // Extract step title from modal header
        var stepTitle = "";
        string[] stepSelectors =
        [
            ".jobs-easy-apply-modal__content h3.t-16",
            ".ph5 h3.t-16.t-bold",
            ".artdeco-modal__content h3"
        ];
        foreach (var sel in stepSelectors)
        {
            try
            {
                var title = await page.Locator(sel).First.InnerTextAsync(new() { Timeout = 500 });
                if (!string.IsNullOrWhiteSpace(title))
                {
                    stepTitle = title.Trim();
                    _logger.LogDebug("Step title extracted via '{Selector}': \"{Title}\"", sel, stepTitle);
                    break;
                }
            }
            catch { }
        }

        // Text inputs
        var textInputs = formRoot.Locator("input[type=\"text\"], input:not([type]), input.artdeco-text-input--input, [data-test-single-line-text-form-component] input");
        var textCount = await textInputs.CountAsync();
        for (var i = 0; i < textCount; i++)
        {
            var input = textInputs.Nth(i);
            var label = await ExtractLabelAsync(page, input, "text");
            if (string.IsNullOrEmpty(label)) continue;
            var required = await input.GetAttributeAsync("required") is not null;
            var value = await input.InputValueAsync();
            var id = await input.GetAttributeAsync("id") ?? "";
            fields.Add(new FormField(FormFieldType.Text, label, id, required, value));
        }

        // Select dropdowns
        var selects = formRoot.Locator("select, [data-test-text-entity-list-form-component] select");
        var selectCount = await selects.CountAsync();
        for (var i = 0; i < selectCount; i++)
        {
            var sel = selects.Nth(i);
            var label = await ExtractLabelAsync(page, sel, "select");
            if (string.IsNullOrEmpty(label)) continue;
            var required = await sel.GetAttributeAsync("required") is not null;
            var value = await sel.InputValueAsync();
            var id = await sel.GetAttributeAsync("id") ?? "";
            var options = await sel.EvaluateAsync<string[]>(@"(el) => {
                try {
                    return Array.from(el.options)
                        .map(o => o.textContent?.trim() || o.value?.trim() || '')
                        .filter(v => {
                            if (!v) return false;
                            const lower = v.toLowerCase();
                            return !lower.includes('selecionar') && !lower.includes('select')
                                && !lower.includes('opção') && !lower.includes('opcao');
                        });
                } catch { return []; }
            }");
            fields.Add(new FormField(FormFieldType.Select, label, id, required, value, [.. options]));
        }

        // Textareas
        var textareas = formRoot.Locator("textarea");
        var taCount = await textareas.CountAsync();
        for (var i = 0; i < taCount; i++)
        {
            var ta = textareas.Nth(i);
            var label = await ExtractLabelAsync(page, ta, "textarea");
            if (string.IsNullOrEmpty(label)) continue;
            var required = await ta.GetAttributeAsync("required") is not null;
            var value = await ta.InputValueAsync();
            var id = await ta.GetAttributeAsync("id") ?? "";
            fields.Add(new FormField(FormFieldType.Textarea, label, id, required, value));
        }

        // File input detection
        var fileBtn = page.Locator("input[type=\"file\"], .jobs-document-upload__upload-button").First;
        var hasFile = await fileBtn.IsVisibleAsync();
        if (hasFile)
            fields.Add(new FormField(FormFieldType.File, "Resume", "", true));

        return (fields, stepTitle);
    }

    private async Task<string> ExtractLabelAsync(IPage page, ILocator element, string type)
    {
        // Primary strategy: use element.evaluate() to find label via closest container
        try
        {
            var labelText = await element.EvaluateAsync<string>(@"(el) => {
                try {
                    // Strategy 1: Find parent form-element container and look for label
                    const formElement = el.closest('[data-test-form-element], .fb-dash-form-element, .artdeco-text-input, [data-test-text-entity-list-form-component]');
                    if (formElement) {
                        // For selects, try title element first
                        const title = formElement.querySelector('[data-test-text-entity-list-form-title]');
                        if (title) {
                            const span = title.querySelector('span:not(.visually-hidden)');
                            if (span && span.textContent.trim()) return span.textContent.trim();
                            return title.textContent.trim();
                        }
                        const lbl = formElement.querySelector('label');
                        if (lbl) {
                            const span = lbl.querySelector('span:not(.visually-hidden)');
                            if (span && span.textContent.trim()) return span.textContent.trim();
                            const clone = lbl.cloneNode(true);
                            clone.querySelectorAll('.visually-hidden, [aria-hidden=""true""]').forEach(s => s.remove());
                            return (clone.textContent || '').trim();
                        }
                    }

                    // Strategy 2: label by 'for' attribute
                    if (el.id) {
                        const byFor = document.querySelector('label[for=""' + el.id + '""]');
                        if (byFor) {
                            const span = byFor.querySelector('span:not(.visually-hidden)');
                            if (span && span.textContent.trim()) return span.textContent.trim();
                            const clone = byFor.cloneNode(true);
                            clone.querySelectorAll('.visually-hidden, [aria-hidden=""true""]').forEach(s => s.remove());
                            return (clone.textContent || '').trim();
                        }
                    }
                    return '';
                } catch { return ''; }
            }");
            if (!string.IsNullOrEmpty(labelText)) return labelText;
        }
        catch { }

        // Fallback: aria-label
        try
        {
            var ariaLabel = await element.GetAttributeAsync("aria-label");
            if (!string.IsNullOrEmpty(ariaLabel)) return ariaLabel;
        }
        catch { }

        // Fallback: placeholder
        try
        {
            var placeholder = await element.GetAttributeAsync("placeholder");
            if (!string.IsNullOrEmpty(placeholder)) return placeholder;
        }
        catch { }

        return "";
    }

    private async Task<bool> FillFieldAsync(IPage page, FormField field, JobListing job, string stepTitle)
    {
        _logger.LogInformation("[{Title}] Step '{StepTitle}': Filling field \"{Field}\"", job.Title, stepTitle, field.Label);

        if (field.Type == FormFieldType.File) return true;

        using var scope = _scopeFactory.CreateScope();
        var questionRepo = scope.ServiceProvider.GetRequiredService<IQuestionRepository>();

        var existing = await questionRepo.FindByTextAsync(field.Label);
        var isExisting = existing is not null;

        // Map field type
        var questionFieldType = field.Type switch
        {
            FormFieldType.Text => QuestionFieldType.Textarea,
            FormFieldType.Textarea => QuestionFieldType.Textarea,
            FormFieldType.Select => QuestionFieldType.Select,
            FormFieldType.Radio => QuestionFieldType.Radio,
            FormFieldType.Checkbox => QuestionFieldType.Checkbox,
            _ => QuestionFieldType.Textarea
        };

        // ALWAYS upsert the question (save/update) with latest options, before any fill logic
        if (isExisting && existing is not null)
        {
            existing.Options = field.Options ?? [];
            existing.FieldType = questionFieldType;
            existing.Group = stepTitle;
            existing.JobTitle = job.Title;
            existing.Company = job.Company;
            existing.UpdatedAt = DateTime.UtcNow;
            await questionRepo.UpdateAsync(existing, default);
        }
        else
        {
            var newQuestion = new CollectedQuestion
            {
                Id = Guid.NewGuid(),
                QuestionText = field.Label,
                FieldType = questionFieldType,
                Options = field.Options ?? [],
                Group = stepTitle,
                Platform = PlatformType.LinkedIn,
                JobTitle = job.Title,
                Company = job.Company,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await questionRepo.AddAsync(newQuestion, default);
        }

        // If existing question has a saved answer, use it
        if (isExisting && existing is not null && !string.IsNullOrEmpty(existing.Answer))
        {
            _logger.LogInformation("[Questions] Usando resposta salva: \"{Label}\" = \"{Answer}\"", field.Label, existing.Answer);
            _answersUsed[field.Label] = existing.Answer;
            await FillFieldValueAsync(page, field, existing.Answer);
            return true;
        }

        // If existing question but no answer configured
        if (isExisting)
        {
            _logger.LogInformation("[Questions] Sem resposta configurada, pulando: \"{Label}\"", field.Label);
            return false;
        }

        // New question: if field is already pre-filled by LinkedIn (and not a select), skip it
        if (!string.IsNullOrEmpty(field.CurrentValue) && field.Type != FormFieldType.Select)
        {
            _logger.LogInformation("[Questions] Campo já preenchido pelo LinkedIn: \"{Label}\"", field.Label);
            return true;
        }

        // No saved answer available, check if required
        if (field.Required) return false;
        return true;
    }

    private async Task FillFieldValueAsync(IPage page, FormField field, string answer)
    {
        var selector = string.IsNullOrEmpty(field.ElementId) ? "" : $"#{field.ElementId}";

        switch (field.Type)
        {
            case FormFieldType.Text:
            case FormFieldType.Textarea:
                if (!string.IsNullOrEmpty(selector))
                {
                    await page.Locator(selector).ClickAsync();
                    await _behavior.DelayAsync(100, 300);
                    await page.Locator(selector).FillAsync(answer);
                }
                break;

            case FormFieldType.Select:
                if (!string.IsNullOrEmpty(selector))
                {
                    var bestOption = FindBestOption(answer, field.Options ?? []);
                    if (bestOption is not null)
                        await page.Locator(selector).SelectOptionAsync(new[] { bestOption });
                    else
                        await page.Locator(selector).SelectOptionAsync(new[] { answer });
                }
                break;

            case FormFieldType.Radio:
                if (!string.IsNullOrEmpty(selector))
                {
                    var radios = page.Locator(selector);
                    var count = await radios.CountAsync();
                    for (var i = 0; i < count; i++)
                    {
                        var radio = radios.Nth(i);
                        var label = await radio.EvaluateAsync<string>("(el) => el.closest('label')?.textContent?.trim() || ''");
                        if (label.Contains(answer, StringComparison.OrdinalIgnoreCase))
                        {
                            await radio.CheckAsync();
                            break;
                        }
                    }
                }
                break;

            case FormFieldType.Checkbox:
                if (answer.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                    answer.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    answer == "1")
                {
                    if (!string.IsNullOrEmpty(selector))
                        await page.Locator(selector).CheckAsync();
                }
                break;
        }
    }

    private static string? FindBestOption(string answer, List<string>? options)
    {
        if (options is null || options.Count == 0) return null;

        var lower = answer.ToLowerInvariant();

        var exact = options.FirstOrDefault(o => o.Equals(answer, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var contains = options.FirstOrDefault(o =>
            o.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
            lower.Contains(o, StringComparison.OrdinalIgnoreCase));
        if (contains is not null) return contains;

        var firstWord = lower.Split(' ')[0];
        return options.FirstOrDefault(o => o.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase));
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
        var nextSel = await WaitForAnySelectorAsync(page, NextButton, 2000);
        if (nextSel is not null)
        {
            await _behavior.HumanClickAsync(page, nextSel);
            await _behavior.DelayAsync(1500, 2500);

            if (await GetValidationErrorAsync(page) is not null) return StepResult.Error;
            return StepResult.Next;
        }

        var reviewSel = await WaitForAnySelectorAsync(page, ReviewButton, 1500);
        if (reviewSel is not null)
        {
            await _behavior.HumanClickAsync(page, reviewSel);
            await _behavior.DelayAsync(1500, 2500);
            return StepResult.Review;
        }

        var submitSel = await WaitForAnySelectorAsync(page, SubmitButton, 1500);
        if (submitSel is not null)
            return StepResult.Submit;

        return StepResult.Error;
    }

    private async Task<bool> IsSubmitStepAsync(IPage page)
    {
        var modalSel = await WaitForAnySelectorAsync(page, ModalContainer, 500);
        if (modalSel is null) return false;
        var submitSel = await WaitForAnySelectorAsync(page, SubmitButton, 1000);
        return submitSel is not null;
    }

    private async Task ClickSubmitAsync(IPage page)
    {
        var submitSel = await WaitForAnySelectorAsync(page, SubmitButton, 3000);
        if (submitSel is null)
        {
            _logger.LogWarning("Submit button not found");
            return;
        }

        try { await _behavior.HumanClickAsync(page, submitSel); }
        catch
        {
            try { await page.Locator(submitSel).ClickAsync(new() { Force = true }); }
            catch
            {
                try { await page.EvaluateAsync(@"(sel) => document.querySelector(sel)?.click()", submitSel); }
                catch { }
            }
        }
    }

    private async Task<bool> CheckSubmissionSuccessAsync(IPage page)
    {
        var successPhrases = new[]
        {
            "application was sent", "applied successfully", "your application",
            "application submitted", "candidatura enviada", "inscrição concluída",
            "candidatura concluída", "enviada com sucesso", "success"
        };

        await _behavior.DelayAsync(2000, 3000);

        // Check modal containers for success text
        var containers = new[] { ".jobs-easy-apply-modal", ".artdeco-modal", ".jpac-modal" };
        foreach (var container in containers)
        {
            try
            {
                var text = await page.Locator(container).First.InnerTextAsync(new() { Timeout = 1000 });
                if (successPhrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("[Success] Confirmation text found in: {Container}", container);
                    await DismissSuccessModalAsync(page);
                    return true;
                }
            }
            catch { }
        }

        // Check body text
        try
        {
            var bodyText = await page.Locator("body").InnerTextAsync(new() { Timeout = 1000 });
            if (successPhrases.Any(p => bodyText.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation("[Success] Text found in body");
                return true;
            }
        }
        catch { }

        // Check if modal is gone (job page visible again)
        try
        {
            var modalOpen = await WaitForAnySelectorAsync(page, ModalContainer, 2000);
            if (modalOpen is null)
            {
                _logger.LogInformation("[Success] Modal closed, job page visible");
                return true;
            }
        }
        catch { }

        return false;
    }

    private async Task DismissSuccessModalAsync(IPage page)
    {
        var doneSelectors = new[]
        {
            "button[aria-label*=\"Concluir\"]",
            "button[aria-label*=\"Done\"]",
            "button[aria-label*=\"Dismiss\"]",
            "button.artdeco-modal__dismiss"
        };

        var doneSel = await WaitForAnySelectorAsync(page, doneSelectors, 2000);
        if (doneSel is not null)
        {
            await _behavior.HumanClickAsync(page, doneSel);
            await _behavior.DelayAsync(500, 1000);
        }
    }

    private async Task<string?> GetValidationErrorAsync(IPage page)
    {
        var errorSelectors = new[]
        {
            ".artdeco-inline-feedback--error",
            ".fb-form-element__error-text",
            "[data-test-form-element-error-text]"
        };

        foreach (var sel in errorSelectors)
        {
            try
            {
                var el = page.Locator(sel).First;
                if (await el.IsVisibleAsync())
                    return await el.InnerTextAsync();
            }
            catch { }
        }
        return null;
    }

    private async Task CloseModalAsync(IPage page)
    {
        try
        {
            var dismissSel = await WaitForAnySelectorAsync(page, DismissButton, 2000);
            if (dismissSel is not null)
            {
                await _behavior.HumanClickAsync(page, dismissSel);
                await _behavior.DelayAsync(500, 1000);

                var discardSel = await WaitForAnySelectorAsync(page, DiscardButton, 2000);
                if (discardSel is not null)
                    await _behavior.HumanClickAsync(page, discardSel);
            }
        }
        catch { }
    }

    private async Task<string?> WaitForAnySelectorAsync(IPage page, string[] selectors, int timeoutMs)
    {
        foreach (var sel in selectors)
        {
            try
            {
                var locator = page.Locator(sel).First;
                await locator.WaitForAsync(new() { Timeout = timeoutMs });
                if (await locator.IsVisibleAsync())
                    return sel;
            }
            catch { }
        }
        return null;
    }
}

public sealed record FormField(FormFieldType Type, string Label, string ElementId, bool Required, string? CurrentValue = null, List<string>? Options = null);

public enum FormFieldType { Text, Textarea, Select, Radio, Checkbox, File }

public enum StepResult { Next, Review, Submit, Error }

public sealed record ApplyResult(
    bool Success,
    string? ErrorMessage = null,
    bool NeedsManualIntervention = false,
    Dictionary<string, string>? AnswersUsed = null);
