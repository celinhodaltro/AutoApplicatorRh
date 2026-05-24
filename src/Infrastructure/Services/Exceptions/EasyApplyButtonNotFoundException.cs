namespace AutoApplicator.Infrastructure.Services.Exceptions;

public sealed class EasyApplyButtonNotFoundException : AutomationException
{
    public EasyApplyButtonNotFoundException(string jobTitle)
        : base($"Could not find Easy Apply button for '{jobTitle}'.")
    {
    }
}
