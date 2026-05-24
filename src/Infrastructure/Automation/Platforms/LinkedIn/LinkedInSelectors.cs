namespace AutoApplicator.Infrastructure.Automation.Platforms.LinkedIn;

public static class LinkedInSelectors
{
    // Auth
    public static readonly string[] AuthSelectors =
    [
        ".global-nav__me-photo",
        "img.global-nav__me-photo",
        ".global-nav__primary-link-me-menu-trigger",
        ".feed-identity-module__actor-meta"
    ];

    // List containers
    public static readonly string[] ListSelectors =
    [
        ".jobs-search-results-list",
        ".scaffold-layout__list",
        ".jobs-search__results-list"
    ];

    // Navigate / card check
    public static readonly string[] NavigateCardSelectors =
    [
        ".job-card-container",
        ".jobs-search-results__list-item",
        "li[data-occludable-job-id]",
        ".scaffold-layout__list-item"
    ];

    // Job Cards
    public static readonly string[] JobCardSelectors =
    [
        ".job-card-container",
        ".jobs-search-results__list-item",
        "li[data-occludable-job-id]",
        ".scaffold-layout__list-item"
    ];

    public static readonly string[] CardTitleSelectors =
    [
        ".job-card-list__title",
        "a.job-card-container__link",
        ".artdeco-entity-lockup__title a",
        "a[class*=\"job-card-list__title\"]"
    ];

    public static readonly string[] CardCompanySelectors =
    [
        ".job-card-container__primary-description",
        ".artdeco-entity-lockup__subtitle",
        ".job-card-container__company-name"
    ];

    public static readonly string[] CardLocationSelectors =
    [
        ".job-card-container__metadata-item",
        ".artdeco-entity-lockup__caption",
        ".job-card-container__metadata-wrapper li"
    ];

    // Detail
    public static readonly string[] DetailDescriptionSelectors =
    [
        "#job-details",
        ".jobs-description__content",
        ".jobs-description-content__text",
        ".jobs-box__html-content",
        "article[class*=\"jobs-description\"]",
        ".jobs-description",
        "div[class*=\"description__text\"]",
        ".job-details-about-the-job-module"
    ];

    public static readonly string[] DetailSalarySelectors =
    [
        ".job-details-jobs-unified-top-card__job-insight span",
        "[class*=\"salary\"]",
        ".compensation__salary"
    ];

    public static readonly string[] DetailPostedDateSelectors =
    [
        ".job-details-jobs-unified-top-card__primary-description-container span",
        "time",
        ".jobs-unified-top-card__posted-date"
    ];

    // Apply
    public static readonly string[] EasyApplyButton =
    [
        "button.jobs-apply-button",
        "a.jobs-apply-button",
        "button[aria-label*=\"Easy Apply\"]",
        "a[aria-label*=\"Easy Apply\"]",
        "button[aria-label*=\"Candidatura\"]",
        "a[aria-label*=\"Candidatura\"]",
        "button[aria-label*=\"candidatura\"]",
        "a[aria-label*=\"candidatura\"]",
        "a[href*=\"apply/?openSDUIApplyFlow\"]",
        ".jobs-apply-button--top-card button",
        ".jobs-apply-button--top-card a",
        "button.jobs-s-apply",
        "a.jobs-s-apply"
    ];

    public static readonly string[] ModalContainer =
    [
        ".jobs-easy-apply-modal",
        "[data-test-modal-id=\"easy-apply-modal\"]",
        ".artdeco-modal--layer-default"
    ];

    public static readonly string[] NextButton =
    [
        "button[aria-label=\"Continue to next step\"]",
        "button[aria-label=\"Avançar para próxima etapa\"]",
        "button[aria-label*=\"Avançar\"]",
        "button[aria-label*=\"Continuar\"]",
        "button[aria-label*=\"Próxima\"]",
        "button:has-text(\"Avançar\")",
        "button:has-text(\"Continuar\")",
        "button[data-easy-apply-next-button]",
        "button[data-easy-apply-next-button]:not([disabled])",
        ".artdeco-modal footer button.artdeco-button--primary:not([disabled])"
    ];

    public static readonly string[] ReviewButton =
    [
        "button[aria-label=\"Review your application\"]",
        "button[aria-label=\"Revise sua candidatura\"]",
        "button[data-easy-apply-review-button]",
        "button[data-live-test-easy-apply-review-button]",
        ".artdeco-modal footer button:has-text(\"Revisar\")",
        ".artdeco-modal footer button:has-text(\"Review\")",
        ".artdeco-modal footer button:has-text(\"Rever\")"
    ];

    public static readonly string[] SubmitButton =
    [
        "button[aria-label=\"Submit application\"]",
        "button[aria-label=\"Enviar candidatura\"]",
        "button[data-easy-apply-submit-button]",
        "button[data-live-test-easy-apply-submit-button]",
        ".artdeco-modal footer button.artdeco-button--primary:last-of-type:has-text(\"Enviar\")",
        ".artdeco-modal footer button.artdeco-button--primary:last-of-type:has-text(\"Submit\")"
    ];

    public static readonly string[] DismissButton =
    [
        "button[aria-label=\"Dismiss\"]",
        "button.artdeco-modal__dismiss",
        ".artdeco-modal__dismiss"
    ];

    public static readonly string[] DiscardButton =
    [
        "button[data-test-dialog-primary-btn]",
        "button[data-control-name=\"discard_application_confirm_btn\"]"
    ];

    // Pagination
    public static readonly string[] PaginationSelectors =
    [
        ".artdeco-pagination__button--next",
        "button[aria-label=\"Next\"]",
        "[class*=\"pagination\"] button:last-child"
    ];

    // Step Title
    public static readonly string[] StepTitleSelectors =
    [
        ".jobs-easy-apply-modal__content h3.t-16",
        ".ph5 h3.t-16.t-bold",
        ".artdeco-modal__content h3"
    ];

    // Error/Validation
    public static readonly string[] ErrorSelectors =
    [
        ".artdeco-inline-feedback--error",
        ".fb-form-element__error-text",
        "[data-test-form-element-error-text]"
    ];

    // Success
    public static readonly string[] SuccessPhrases =
    [
        "application was sent", "applied successfully", "your application",
        "application submitted", "candidatura enviada", "inscrição concluída",
        "candidatura concluída", "enviada com sucesso", "success"
    ];

    public static readonly string[] DoneSelectors =
    [
        "button[aria-label*=\"Concluir\"]",
        "button[aria-label*=\"Done\"]",
        "button[aria-label*=\"Dismiss\"]",
        "button.artdeco-modal__dismiss"
    ];

    // Maps
    public static readonly Dictionary<string, string> DatePostedMap = new()
    {
        ["Past 24 Hours"] = "r86400",
        ["Past Week"] = "r604800",
        ["Past Month"] = "r2592000"
    };

    public static readonly Dictionary<string, string> JobTypeMap = new()
    {
        ["Full-time"] = "F",
        ["Contract"] = "C",
        ["Freelance"] = "T",
        ["Part-time"] = "P"
    };

    public static readonly Dictionary<string, string> ExperienceMap = new()
    {
        ["Internship"] = "1",
        ["Entry"] = "2",
        ["Associate"] = "3",
        ["Mid-Senior"] = "4",
        ["Director"] = "5",
        ["Executive"] = "6"
    };
}
