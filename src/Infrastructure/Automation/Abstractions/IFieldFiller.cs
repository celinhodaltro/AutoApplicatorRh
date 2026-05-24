using AutoApplicator.Infrastructure.Automation.Models;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Abstractions;

public interface IFieldFiller
{
    FormFieldType FieldType { get; }
    Task<bool> CanHandleAsync(ILocator element);
    Task<List<FormField>> ExtractAsync(IPage page, ILocator formRoot);
    Task FillAsync(IPage page, FormField field, string answer);
}
