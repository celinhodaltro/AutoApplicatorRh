using AutoApplicator.Domain.Enums;
using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Abstractions;

public interface ISuccessDetector
{
    PlatformType Platform { get; }
    Task<bool> DetectAsync(IPage page);
}
