using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.FieldFillers;

public sealed class CheckboxFieldFiller : IFieldFiller
{
    private readonly ILogger<CheckboxFieldFiller> _logger;

    public CheckboxFieldFiller(ILogger<CheckboxFieldFiller> logger)
    {
        _logger = logger;
    }

    public FormFieldType FieldType => FormFieldType.Checkbox;

    public async Task<bool> CanHandleAsync(ILocator element)
    {
        try
        {
            var tagName = await element.EvaluateAsync<string>("el => el.tagName.toLowerCase()");
            if (tagName == "input")
            {
                var type = await element.GetAttributeAsync("type");
                if (type == "checkbox") return true;
            }

            var role = await element.GetAttributeAsync("role");
            if (role == "checkbox") return true;

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CheckboxFieldFiller.CanHandleAsync failed");
            return false;
        }
    }

    public async Task<List<FormField>> ExtractAsync(IPage page, ILocator formRoot)
    {
        var fields = new List<FormField>();

        try
        {
            var checkboxes = formRoot.Locator("input[type=\"checkbox\"], [role=\"checkbox\"]");
            var count = await checkboxes.CountAsync();

            for (var i = 0; i < count; i++)
            {
                var cb = checkboxes.Nth(i);
                try
                {
                    var label = await SelectorHelper.ExtractLabelAsync(page, cb);
                    if (string.IsNullOrEmpty(label)) continue;

                    var required = await cb.GetAttributeAsync("required") is not null;
                    var isChecked = await cb.IsCheckedAsync();
                    var id = await cb.GetAttributeAsync("id") ?? string.Empty;

                    fields.Add(new FormField(FormFieldType.Checkbox, label, id, required, isChecked ? "Yes" : null));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CheckboxFieldFiller.ExtractAsync: failed at index {Index}", i);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckboxFieldFiller.ExtractAsync failed");
        }

        return fields;
    }

    public async Task FillAsync(IPage page, FormField field, string answer)
    {
        try
        {
            if (answer.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                answer == "1")
            {
                if (!string.IsNullOrEmpty(field.ElementId))
                {
                    await page.Locator($"#{field.ElementId}").CheckAsync();
                    _logger.LogInformation("CheckboxFieldFiller: Checked '{Label}'", field.Label);
                }
                else
                {
                    // Try to find the checkbox by label text
                    var checkboxes = page.Locator("input[type=\"checkbox\"], [role=\"checkbox\"]");
                    var count = await checkboxes.CountAsync();
                    for (var i = 0; i < count; i++)
                    {
                        var cb = checkboxes.Nth(i);
                        var label = await SelectorHelper.ExtractLabelAsync(page, cb);
                        if (!string.IsNullOrEmpty(label) && label.Contains(field.Label, StringComparison.OrdinalIgnoreCase))
                        {
                            await cb.CheckAsync();
                            _logger.LogInformation("CheckboxFieldFiller: Checked '{Label}' (found by label text)", field.Label);
                            return;
                        }
                    }
                }
            }
            else
            {
                _logger.LogInformation("CheckboxFieldFiller: Skipping '{Label}' (answer: '{Answer}')", field.Label, answer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckboxFieldFiller.FillAsync failed for field '{Label}'", field.Label);
            throw;
        }
    }
}
