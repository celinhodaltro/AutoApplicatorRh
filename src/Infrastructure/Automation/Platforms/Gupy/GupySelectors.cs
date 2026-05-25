namespace AutoApplicator.Infrastructure.Automation.Platforms.Gupy;

public static class GupySelectors
{
    // Search URL builder
    public static string BuildSearchUrl(string keywords, int page = 1) =>
        $"https://portal.gupy.io/job-search/term={Uri.EscapeDataString(keywords)}?page={page}";

    // Job cards on search results
    public const string JobCardSelector = "a[aria-label^=\"Ir para vaga\"]";
    public const string CardTitleSelector = "h3";
    public const string CardDetailsSelector = "[data-testid=\"listing-details\"]";
    public const string CardLocationSelector = "[data-testid=\"job-location\"]";

    // Detail page - Apply button
    public const string ApplyButton = "a[data-testid=\"job-cta-link\"]";
    public const string JobTitleSelector = "h1";

    // Login detection
    public const string LoginPageIndicator = "/candidates/signin";

    // Form fields
    public const string SubmitButton = "button[name=\"saveAndContinueButton\"]";
    public const string ContinueButton = "button:has-text(\"Continuar\")";
    public const string ResponderAgoraButton = "button:has-text(\"Responder agora\")";
    public const string RadioGroup = "fieldset[aria-labelledby]";
    public const string QuestionText = "legend";
    public const string TypeaheadField = "[role=\"combobox\"]";
    public const string TextInputField = "input[type=\"text\"]";
    public const string RadioInputField = "input[type=\"radio\"]";

    // Post-apply modal
    public const string PostApplyModal = "div[role=\"dialog\"]";
    public const string FinalizeButton = "button:has-text(\"Finalizar candidatura\")";
    public const string FinalizeButtonById = "button#dialog-give-up-personalization-step";
    public const string PersonalizeButton = "button:has-text(\"Personalizar candidatura\")";
    public const string CloseModalButton = "button:has-text(\"Fechar\")";

    // Success confirmation
    public const string ApplicationSuccessTitle = "h1:has-text(\"Candidatura finalizada\")";

    // Pagination
    public const string NextPageButton = "a[aria-label=\"Próxima página\"]";
}
