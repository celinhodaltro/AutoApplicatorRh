namespace AutoApplicator.Infrastructure.Services.Exceptions;

public sealed class QuestionsNeededException : AutomationException
{
    public QuestionsNeededException(string jobTitle)
        : base($"'{jobTitle}' needs answers configured. Go to Questions tab.", "Go to Questions", "/questions")
    {
    }
}
