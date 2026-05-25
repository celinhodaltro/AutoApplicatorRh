using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.Gupy.FieldFillers;

public sealed class GupyRadioFieldFiller : IFieldFiller
{
    private readonly ILogger<GupyRadioFieldFiller> _logger;

    public GupyRadioFieldFiller(ILogger<GupyRadioFieldFiller> logger)
    {
        _logger = logger;
    }

    public FormFieldType FieldType => FormFieldType.Radio;

    public async Task<List<FormField>> ExtractAsync(IPage page, ILocator formRoot)
    {
        var fields = new List<FormField>();

        try
        {
            // Gupy radio groups are inside <fieldset> with <legend>
            var fieldSets = formRoot.Locator("fieldset[aria-labelledby]");
            var count = await fieldSets.CountAsync();

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var fs = fieldSets.Nth(i);
                    var legend = fs.Locator("legend").First;
                    var questionText = await legend.InnerTextAsync();
                    if (string.IsNullOrEmpty(questionText)) continue;

                    var options = new List<string>();
                    var radioLabels = fs.Locator("label span:not(.visually-hidden)");
                    var optCount = await radioLabels.CountAsync();
                    for (int j = 0; j < optCount; j++)
                        options.Add((await radioLabels.Nth(j).InnerTextAsync()).Trim());

                    fields.Add(new FormField(FormFieldType.Radio, questionText.Trim(), "", true, null, options));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GupyRadioFieldFiller.ExtractAsync: failed to extract radio group at index {Index}", i);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GupyRadioFieldFiller.ExtractAsync failed");
        }

        return fields;
    }

    public async Task FillAsync(IPage page, FormField field, string answer)
    {
        try
        {
            // Find radio by matching answer text
            var radios = page.Locator("input[type=\"radio\"]");
            var count = await radios.CountAsync();
            for (int i = 0; i < count; i++)
            {
                var radio = radios.Nth(i);
                var label = await radio.EvaluateAsync<string>("el => el.closest('label')?.textContent?.trim() || ''");
                if (label.Contains(answer, StringComparison.OrdinalIgnoreCase))
                {
                    await radio.CheckAsync();
                    _logger.LogInformation("GupyRadioFieldFiller: Selected radio '{Label}' for answer '{Answer}'", label, answer);
                    return;
                }
            }

            _logger.LogWarning("GupyRadioFieldFiller: Could not find radio option matching '{Answer}' for field '{Label}'", answer, field.Label);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GupyRadioFieldFiller.FillAsync failed for field '{Label}'", field.Label);
            throw;
        }
    }

    public Task<bool> CanHandleAsync(ILocator element) => Task.FromResult(false);
}
