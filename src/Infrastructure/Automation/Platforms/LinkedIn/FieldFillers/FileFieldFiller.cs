using AutoApplicator.Infrastructure.Automation.Abstractions;
using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn.FieldFillers;

public sealed class FileFieldFiller : IFieldFiller
{
    private readonly ILogger<FileFieldFiller> _logger;

    public FileFieldFiller(ILogger<FileFieldFiller> logger)
    {
        _logger = logger;
    }

    public FormFieldType FieldType => FormFieldType.File;

    public async Task<bool> CanHandleAsync(ILocator element)
    {
        try
        {
            var tagName = await element.EvaluateAsync<string>("el => el.tagName.toLowerCase()");
            if (tagName == "input")
            {
                var type = await element.GetAttributeAsync("type");
                if (type == "file") return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<FormField>> ExtractAsync(IPage page, ILocator formRoot)
    {
        var fields = new List<FormField>();
        try
        {
            var fileInput = formRoot.Locator("input[type=\"file\"]").First;
            if (await fileInput.IsVisibleAsync())
            {
                fields.Add(new FormField(FormFieldType.File, "Resume", "", true));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FileFieldFiller.ExtractAsync failed");
        }
        return fields;
    }

    public async Task FillAsync(IPage page, FormField field, string answer)
    {
        var resumePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoApplicator", "resume.pdf");

        if (!File.Exists(resumePath))
        {
            _logger.LogWarning("Resume not found at: {Path}", resumePath);
            return;
        }

        try
        {
            var fileInput = page.Locator("input[type=\"file\"]").First;
            await fileInput.SetInputFilesAsync(resumePath);
            _logger.LogInformation("Uploaded resume: {Path}", resumePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File upload failed");
            throw;
        }
    }
}
