using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Common;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.FieldFillers;

public sealed class RadioFieldFiller : IFieldFiller
{
    private readonly ILogger<RadioFieldFiller> _logger;

    public RadioFieldFiller(ILogger<RadioFieldFiller> logger)
    {
        _logger = logger;
    }

    public FormFieldType FieldType => FormFieldType.Radio;

    public async Task<bool> CanHandleAsync(ILocator element)
    {
        try
        {
            var tagName = await element.EvaluateAsync<string>("el => el.tagName.toLowerCase()");

            if (tagName == "input")
            {
                var type = await element.GetAttributeAsync("type");
                if (type == "radio") return true;
            }

            var role = await element.GetAttributeAsync("role");
            if (role == "radio") return true;

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RadioFieldFiller.CanHandleAsync failed");
            return false;
        }
    }

    public async Task<List<FormField>> ExtractAsync(IPage page, ILocator formRoot)
    {
        var fields = new List<FormField>();

        try
        {
            // Find all radio groups within the formRoot
            // A radio group is typically contained in a fieldset or a container with multiple radio inputs
            var radioGroups = formRoot.Locator("fieldset, div[role=\"radiogroup\"], .fb-dash-form-element:has(input[type=\"radio\"])");
            var groupCount = await radioGroups.CountAsync();

            for (var g = 0; g < groupCount; g++)
            {
                var group = radioGroups.Nth(g);
                try
                {
                    // Extract group label
                    var label = await ExtractRadioGroupLabelAsync(page, group);
                    if (string.IsNullOrEmpty(label)) continue;

                    // Make sure this group actually contains radio buttons
                    var radios = group.Locator("input[type=\"radio\"], [role=\"radio\"]");
                    var radioCount = await radios.CountAsync();
                    if (radioCount == 0) continue;

                    var options = new List<string>();
                    var firstId = string.Empty;
                    var required = false;

                    for (var r = 0; r < radioCount; r++)
                    {
                        var radio = radios.Nth(r);
                        try
                        {
                            var radioLabel = await radio.EvaluateAsync<string>(@"(el) => {
                                try {
                                    // Closest label
                                    const lbl = el.closest('label');
                                    if (lbl) {
                                        const span = lbl.querySelector('span:not(.visually-hidden)');
                                        if (span && span.textContent.trim()) return span.textContent.trim();
                                        return (lbl.textContent || '').trim();
                                    }
                                    // Check aria-label
                                    if (el.getAttribute('aria-label')) return el.getAttribute('aria-label');
                                    // Check parent span or div text
                                    const parent = el.parentElement;
                                    if (parent) return (parent.textContent || '').trim();
                                    return '';
                                } catch { return ''; }
                            }");

                            if (!string.IsNullOrEmpty(radioLabel))
                                options.Add(radioLabel);

                            if (string.IsNullOrEmpty(firstId))
                            {
                                firstId = await radio.GetAttributeAsync("id") ?? string.Empty;
                            }

                            if (!required)
                            {
                                required = await radio.GetAttributeAsync("required") is not null;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "RadioFieldFiller.ExtractAsync: failed to extract radio option at index {Index} in group {Group}", r, g);
                        }
                    }

                    if (options.Count > 0)
                    {
                        fields.Add(new FormField(FormFieldType.Radio, label, firstId, required, null, options));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RadioFieldFiller.ExtractAsync: failed to process radio group at index {Index}", g);
                }
            }

            // Also handle standalone radio buttons (not inside a fieldset)
            var standaloneRadios = formRoot.Locator("input[type=\"radio\"]:not(fieldset input[type=\"radio\"]):not(div[role=\"radiogroup\"] input[type=\"radio\"])");
            var standaloneCount = await standaloneRadios.CountAsync();
            if (standaloneCount > 0)
            {
                var standaloneOptions = new List<string>();
                var firstId = string.Empty;
                var required = false;

                for (var r = 0; r < standaloneCount; r++)
                {
                    var radio = standaloneRadios.Nth(r);
                    try
                    {
                        var radioLabel = await SelectorHelper.ExtractLabelAsync(page, radio);
                        if (!string.IsNullOrEmpty(radioLabel))
                            standaloneOptions.Add(radioLabel);

                        if (string.IsNullOrEmpty(firstId))
                            firstId = await radio.GetAttributeAsync("id") ?? string.Empty;

                        if (!required)
                            required = await radio.GetAttributeAsync("required") is not null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "RadioFieldFiller.ExtractAsync: failed to extract standalone radio at index {Index}", r);
                    }
                }

                if (standaloneOptions.Count > 0)
                {
                    fields.Add(new FormField(FormFieldType.Radio, string.Join(" / ", standaloneOptions), firstId, required, null, standaloneOptions));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RadioFieldFiller.ExtractAsync failed");
        }

        return fields;
    }

    public async Task FillAsync(IPage page, FormField field, string answer)
    {
        try
        {
            // Try to find the radio whose label contains the answer
            var radioFound = false;

            // Strategy 1: Search by container label
            if (!string.IsNullOrEmpty(field.ElementId))
            {
                var radio = page.Locator($"#{CssSelectorHelper.EscapeCssId(field.ElementId)}");
                if (await radio.CountAsync() > 0)
                {
                    var label = await radio.EvaluateAsync<string>("(el) => el.closest('label')?.textContent?.trim() || ''");
                    if (label.Contains(answer, StringComparison.OrdinalIgnoreCase))
                    {
                        await radio.CheckAsync();
                        radioFound = true;
                    }
                }
            }

            // Strategy 2: Search all radios with matching label
            if (!radioFound)
            {
                var allRadios = page.Locator("input[type=\"radio\"], [role=\"radio\"]");
                var count = await allRadios.CountAsync();
                for (var i = 0; i < count; i++)
                {
                    var radio = allRadios.Nth(i);
                    try
                    {
                        var label = await radio.EvaluateAsync<string>(@"(el) => {
                            try {
                                const lbl = el.closest('label');
                                if (lbl) {
                                    const span = lbl.querySelector('span:not(.visually-hidden)');
                                    if (span && span.textContent.trim()) return span.textContent.trim();
                                    return (lbl.textContent || '').trim();
                                }
                                if (el.getAttribute('aria-label')) return el.getAttribute('aria-label');
                                const parent = el.parentElement;
                                if (parent) return (parent.textContent || '').trim();
                                return '';
                            } catch { return ''; }
                        }");

                        if (label.Contains(answer, StringComparison.OrdinalIgnoreCase))
                        {
                            await radio.CheckAsync();
                            radioFound = true;
                            _logger.LogInformation("RadioFieldFiller: Selected radio '{Label}' for answer '{Answer}'", label, answer);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "RadioFieldFiller.FillAsync: failed to check radio at index {Index}", i);
                    }
                }
            }

            if (!radioFound)
            {
                _logger.LogWarning("RadioFieldFiller: Could not find radio option matching '{Answer}' for field '{Label}'", answer, field.Label);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RadioFieldFiller.FillAsync failed for field '{Label}'", field.Label);
            throw;
        }
    }

    private static async Task<string?> ExtractRadioGroupLabelAsync(IPage page, ILocator group)
    {
        try
        {
            // Try legend inside fieldset
            var legendText = await group.EvaluateAsync<string?>(@"(el) => {
                try {
                    const legend = el.querySelector('legend');
                    if (legend) {
                        const span = legend.querySelector('span:not(.visually-hidden)');
                        if (span && span.textContent.trim()) return span.textContent.trim();
                        return (legend.textContent || '').trim();
                    }
                    return null;
                } catch { return null; }
            }");

            if (!string.IsNullOrEmpty(legendText)) return legendText;

            // Try aria-label or aria-labelledby
            var ariaLabel = await group.GetAttributeAsync("aria-label");
            if (!string.IsNullOrEmpty(ariaLabel)) return ariaLabel;

            // Try first label in the group
            var firstLabel = await group.EvaluateAsync<string?>(@"(el) => {
                try {
                    const label = el.querySelector('label');
                    if (label) {
                        const span = label.querySelector('span:not(.visually-hidden)');
                        if (span && span.textContent.trim()) return span.textContent.trim();
                        return (label.textContent || '').trim();
                    }
                    return null;
                } catch { return null; }
            }");

            return firstLabel;
        }
        catch
        {
            return null;
        }
    }
}
