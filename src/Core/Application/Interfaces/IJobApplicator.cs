using AutoApplicator.Domain.Entities;
using AutoApplicator.Domain.Enums;
using AutoApplicator.Domain.Models;

namespace AutoApplicator.Application.Interfaces;

public interface IJobApplicator
{
    PlatformType Platform { get; }
    Task<ApplyResult> ApplyAsync(IBrowserPage page, JobListing job);
}
