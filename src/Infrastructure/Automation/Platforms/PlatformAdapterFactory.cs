using AutoApplicator.Domain.Enums;
using AutoApplicator.Infrastructure.Automation.Platforms.Gupy;
using AutoApplicator.Infrastructure.Automation.Platforms.Indeed;
using AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoApplicator.Infrastructure.Automation.Platforms;

public sealed class PlatformAdapterFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;

    public PlatformAdapterFactory(ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
    {
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
    }

    public IPlatformAdapter Create(PlatformType platform)
    {
        return platform switch
        {
            PlatformType.LinkedIn => _serviceProvider.GetRequiredService<LinkedInAdapter>(),
            PlatformType.Indeed => new IndeedAdapter(_loggerFactory.CreateLogger<IndeedAdapter>()),
            PlatformType.Gupy => new GupyAdapter(_loggerFactory.CreateLogger<GupyAdapter>()),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, $"No adapter for platform {platform}")
        };
    }
}
