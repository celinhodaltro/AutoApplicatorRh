using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.FieldFillers;

public sealed class TextFieldFiller : IFieldFiller
{
    private readonly ILogger<TextFieldFiller> _logger;

    public TextFieldFiller(ILogger<TextFieldFiller> logger)
    {
        _logger = logger;
    }

    public FormFieldType FieldType => FormFieldType.Text;

    public async Task<bool> CanHandleAsync(ILocator element)
    {
        try
        {
            var tagName = await element.EvaluateAsync<string>("el => el.tagName.toLowerCase()");
            if (tagName == "textarea") return true;

            var type = await element.GetAttributeAsync("type");
            if (tagName == "input" && (type is null || type == "text")) return true;

            var hasClass = await element.EvaluateAsync<bool?>("el => el.matches('.artdeco-text-input--input, [data-test-single-line-text-form-component] input')");
            return hasClass == true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TextFieldFiller.CanHandleAsync failed");
            return false;
        }
    }

    public async Task<List<FormField>> ExtractAsync(IPage page, ILocator formRoot)
    {
        var fields = new List<FormField>();

        try
        {
            var textInputs = formRoot.Locator("input[type=\"text\"], input:not([type]), input.artdeco-text-input--input, [data-test-single-line-text-form-component] input, textarea");
            var count = await textInputs.CountAsync();

            for (var i = 0; i < count; i++)
            {
                var input = textInputs.Nth(i);
                try
                {
                    var label = await SelectorHelper.ExtractLabelAsync(page, input);
                    if (string.IsNullOrEmpty(label)) continue;

                    var tagName = await input.EvaluateAsync<string>("el => el.tagName.toLowerCase()");
                    var type = tagName == "textarea"
                        ? FormFieldType.Textarea
                        : FormFieldType.Text;

                    var required = await input.GetAttributeAsync("required") is not null;
                    var value = await input.InputValueAsync();
                    var id = await input.GetAttributeAsync("id") ?? string.Empty;

                    fields.Add(new FormField(type, label, id, required, value));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "TextFieldFiller.ExtractAsync: failed to extract field at index {Index}", i);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TextFieldFiller.ExtractAsync failed");
        }

        return fields;
    }

    public async Task FillAsync(IPage page, FormField field, string answer)
    {
        try
        {
            var selector = string.IsNullOrEmpty(field.ElementId)
                ? throw new InvalidOperationException("ElementId is empty, cannot fill")
                : $"#{field.ElementId}";

            var locator = page.Locator(selector);
            await locator.ClickAsync();
            await Task.Delay(Random.Shared.Next(100, 300));
            await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible });
            await locator.FillAsync(answer);

            _logger.LogInformation("TextFieldFiller: Filled '{Label}' with '{Answer}'", field.Label, answer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TextFieldFiller.FillAsync failed for field '{Label}'", field.Label);
            throw;
        }
    }
}
