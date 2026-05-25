using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.FieldFillers;

public sealed class TypeaheadFieldFiller : IFieldFiller
{
    private readonly ILogger<TypeaheadFieldFiller> _logger;

    public TypeaheadFieldFiller(ILogger<TypeaheadFieldFiller> logger)
    {
        _logger = logger;
    }

    public FormFieldType FieldType => FormFieldType.Typeahead;

    public async Task<bool> CanHandleAsync(ILocator element)
    {
        try
        {
            var isTypeahead = await element.EvaluateAsync<string>(@"(el) => {
                const parent = el.closest('.search-basic-typeahead, .search-vertical-typeahead, [data-test-single-typeahead-entity-form-component]');
                return parent ? 'typeahead' : '';
            }");
            return isTypeahead == "typeahead";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TypeaheadFieldFiller.CanHandleAsync failed");
            return false;
        }
    }

    public async Task<List<FormField>> ExtractAsync(IPage page, ILocator formRoot)
    {
        var fields = new List<FormField>();

        try
        {
            var inputs = formRoot.Locator(".search-basic-typeahead input, .search-vertical-typeahead input, [data-test-single-typeahead-entity-form-component] input");
            var count = await inputs.CountAsync();

            for (var i = 0; i < count; i++)
            {
                var input = inputs.Nth(i);
                try
                {
                    var label = await SelectorHelper.ExtractLabelAsync(page, input);
                    if (string.IsNullOrEmpty(label)) continue;

                    var required = await input.GetAttributeAsync("required") is not null;
                    var value = await input.InputValueAsync();
                    var id = await input.GetAttributeAsync("id") ?? string.Empty;

                    fields.Add(new FormField(FormFieldType.Typeahead, label, id, required, value));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "TypeaheadFieldFiller.ExtractAsync: failed at index {Index}", i);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TypeaheadFieldFiller.ExtractAsync failed");
        }

        return fields;
    }

    public async Task FillAsync(IPage page, FormField field, string answer)
    {
        try
        {
            var escapedId = CssSelectorHelper.EscapeCssId(field.ElementId);
            var selector = string.IsNullOrEmpty(field.ElementId)
                ? throw new InvalidOperationException("ElementId is empty, cannot fill")
                : $"#{escapedId}";

            var locator = page.Locator(selector);

            await locator.ClickAsync();
            await Task.Delay(Random.Shared.Next(300, 500));
            await locator.FillAsync(string.Empty);
            await Task.Delay(Random.Shared.Next(200, 400));
            await locator.PressSequentiallyAsync(answer, new() { Delay = 50 });
            await Task.Delay(Random.Shared.Next(800, 1200));
            await locator.PressAsync("ArrowDown");
            await Task.Delay(Random.Shared.Next(100, 200));
            await locator.PressAsync("Enter");
            await Task.Delay(Random.Shared.Next(300, 500));
            await locator.PressAsync("Tab");

            _logger.LogInformation("TypeaheadFieldFiller: Filled '{Label}' with '{Answer}'", field.Label, answer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TypeaheadFieldFiller.FillAsync failed for field '{Label}'", field.Label);
            throw;
        }
    }
}
