using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Gupy.FieldFillers;

public sealed class GupyTextareaFieldFiller : IFieldFiller
{
    private readonly ILogger<GupyTextareaFieldFiller> _logger;

    public GupyTextareaFieldFiller(ILogger<GupyTextareaFieldFiller> logger)
    {
        _logger = logger;
    }

    public FormFieldType FieldType => FormFieldType.Textarea;

    public async Task<List<FormField>> ExtractAsync(IPage page, ILocator formRoot)
    {
        var fields = new List<FormField>();

        try
        {
            var textareas = formRoot.Locator("textarea");
            var count = await textareas.CountAsync();

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var ta = textareas.Nth(i);
                    var id = await ta.GetAttributeAsync("id") ?? "";

                    string label = "";
                    if (!string.IsNullOrEmpty(id))
                    {
                        label = await page.EvaluateAsync<string>(@"(lid) => {
                            const lbl = document.querySelector(`label[for=""${lid}""]`);
                            return lbl ? lbl.textContent?.trim() || '' : '';
                        }", id);
                    }

                    if (string.IsNullOrEmpty(label)) continue;

                    var required = await ta.GetAttributeAsync("aria-required") == "true";
                    var value = await ta.InputValueAsync();

                    fields.Add(new FormField(FormFieldType.Textarea, label, id, required, value));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GupyTextareaFieldFiller.ExtractAsync: failed to extract textarea at index {Index}", i);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GupyTextareaFieldFiller.ExtractAsync failed");
        }

        return fields;
    }

    public async Task FillAsync(IPage page, FormField field, string answer)
    {
        try
        {
            if (!string.IsNullOrEmpty(field.ElementId))
            {
                var selector = $"#{CssSelectorHelper.EscapeCssId(field.ElementId)}";
                await page.Locator(selector).FillAsync(answer);
                _logger.LogInformation("GupyTextareaFieldFiller: Filled '{Label}' with '{Answer}'", field.Label, answer);
            }
            else
            {
                _logger.LogWarning("GupyTextareaFieldFiller: ElementId is empty for field '{Label}', cannot fill", field.Label);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GupyTextareaFieldFiller.FillAsync failed for field '{Label}'", field.Label);
            throw;
        }
    }

    public Task<bool> CanHandleAsync(ILocator element) => Task.FromResult(false);
}
