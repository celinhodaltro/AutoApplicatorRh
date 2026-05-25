using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.FieldFillers;

public sealed class SelectFieldFiller : IFieldFiller
{
    private readonly ILogger<SelectFieldFiller> _logger;

    public SelectFieldFiller(ILogger<SelectFieldFiller> logger)
    {
        _logger = logger;
    }

    public FormFieldType FieldType => FormFieldType.Select;

    public async Task<bool> CanHandleAsync(ILocator element)
    {
        try
        {
            var tagName = await element.EvaluateAsync<string>("el => el.tagName.toLowerCase()");
            if (tagName == "select") return true;

            var hasAttr = await element.EvaluateAsync<bool?>("el => el.matches('[data-test-text-entity-list-form-component] select')");
            return hasAttr == true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SelectFieldFiller.CanHandleAsync failed");
            return false;
        }
    }

    public async Task<List<FormField>> ExtractAsync(IPage page, ILocator formRoot)
    {
        var fields = new List<FormField>();

        try
        {
            var selects = formRoot.Locator("select, [data-test-text-entity-list-form-component] select");
            var count = await selects.CountAsync();

            for (var i = 0; i < count; i++)
            {
                var sel = selects.Nth(i);
                try
                {
                    var label = await SelectorHelper.ExtractLabelAsync(page, sel);
                    if (string.IsNullOrEmpty(label)) continue;

                    var required = await sel.GetAttributeAsync("required") is not null;
                    var value = await sel.InputValueAsync();
                    var id = await sel.GetAttributeAsync("id") ?? string.Empty;

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
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SelectFieldFiller.ExtractAsync: failed to extract select at index {Index}", i);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SelectFieldFiller.ExtractAsync failed");
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

            var bestOption = FindBestOption(answer, field.Options ?? []);
            var valueToSelect = bestOption ?? answer;

            await page.Locator(selector).SelectOptionAsync(new[] { valueToSelect });

            _logger.LogInformation("SelectFieldFiller: Filled '{Label}' with '{Answer}' (option: '{Option}')",
                field.Label, answer, valueToSelect);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SelectFieldFiller.FillAsync failed for field '{Label}'", field.Label);
            throw;
        }
    }

    private static string? FindBestOption(string answer, List<string> options)
    {
        if (options.Count == 0) return null;

        var lowerAnswer = answer.ToLowerInvariant();

        // Exact match
        var exact = options.FirstOrDefault(o => o.Equals(answer, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Contains
        var contains = options.FirstOrDefault(o =>
            o.Contains(lowerAnswer, StringComparison.OrdinalIgnoreCase) ||
            lowerAnswer.Contains(o, StringComparison.OrdinalIgnoreCase));
        if (contains is not null) return contains;

        // StartsWith first word
        var firstWord = lowerAnswer.Split(' ')[0];
        return options.FirstOrDefault(o => o.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase));
    }
}
