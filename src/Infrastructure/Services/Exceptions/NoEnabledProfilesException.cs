namespace AutoApplicator.Infrastructure.Services.Exceptions;

public sealed class NoEnabledProfilesException : AutomationException
{
    public NoEnabledProfilesException()
        : base("No enabled profiles found. Create and enable a profile first.", "Go to Profiles", "/profiles")
    {
    }
}
