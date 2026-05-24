using Microsoft.Playwright;

namespace AutoApplicator.Infrastructure.Automation.Common;

public static class SelectorHelper
{
    public static async Task<string> GetFirstVisibleTextAsync(IElementHandle parent, string[] selectors)
    {
        foreach (var sel in selectors)
        {
            try
            {
                var el = await parent.QuerySelectorAsync(sel);
                if (el is null) continue;
                var text = (await el.InnerTextAsync())?.Trim();
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch { /* try next */ }
        }
        return string.Empty;
    }

    public static async Task<string> QuickTextAsync(IPage page, string[] selectors)
    {
        foreach (var sel in selectors)
        {
            try
            {
                var text = await page.Locator(sel).First.InnerTextAsync(new() { Timeout = 1500 });
                if (!string.IsNullOrEmpty(text?.Trim())) return text.Trim();
            }
            catch { /* try next */ }
        }
        return string.Empty;
    }

    public static async Task<string?> ExtractLabelAsync(IPage page, ILocator element)
    {
        // Primary strategy: use element.evaluate() to find label via closest container
        try
        {
            var labelText = await element.EvaluateAsync<string>(@"(el) => {
                try {
                    // Strategy 1: Find parent form-element container and look for label
                    const formElement = el.closest('[data-test-form-element], .fb-dash-form-element, .artdeco-text-input, [data-test-text-entity-list-form-component]');
                    if (formElement) {
                        // For selects, try title element first
                        const title = formElement.querySelector('[data-test-text-entity-list-form-title]');
                        if (title) {
                            const span = title.querySelector('span:not(.visually-hidden)');
                            if (span && span.textContent.trim()) return span.textContent.trim();
                            return title.textContent.trim();
                        }
                        const lbl = formElement.querySelector('label');
                        if (lbl) {
                            const span = lbl.querySelector('span:not(.visually-hidden)');
                            if (span && span.textContent.trim()) return span.textContent.trim();
                            const clone = lbl.cloneNode(true);
                            clone.querySelectorAll('.visually-hidden, [aria-hidden=""true""]').forEach(s => s.remove());
                            return (clone.textContent || '').trim();
                        }
                    }

                    // Strategy 2: label by 'for' attribute
                    if (el.id) {
                        const byFor = document.querySelector('label[for=""' + el.id + '""]');
                        if (byFor) {
                            const span = byFor.querySelector('span:not(.visually-hidden)');
                            if (span && span.textContent.trim()) return span.textContent.trim();
                            const clone = byFor.cloneNode(true);
                            clone.querySelectorAll('.visually-hidden, [aria-hidden=""true""]').forEach(s => s.remove());
                            return (clone.textContent || '').trim();
                        }
                    }
                    return '';
                } catch { return ''; }
            }");
            if (!string.IsNullOrEmpty(labelText)) return labelText;
        }
        catch { /* try next fallback */ }

        // Fallback: aria-label
        try
        {
            var ariaLabel = await element.GetAttributeAsync("aria-label");
            if (!string.IsNullOrEmpty(ariaLabel)) return ariaLabel;
        }
        catch { /* try next fallback */ }

        // Fallback: placeholder
        try
        {
            var placeholder = await element.GetAttributeAsync("placeholder");
            if (!string.IsNullOrEmpty(placeholder)) return placeholder;
        }
        catch { /* try next fallback */ }

        return null;
    }

    public static async Task<string?> GetValidationErrorAsync(IPage page)
    {
        var errorSelectors = new[]
        {
            ".artdeco-inline-feedback--error",
            ".fb-form-element__error-text",
            "[data-test-form-element-error-text]"
        };

        foreach (var sel in errorSelectors)
        {
            try
            {
                var el = page.Locator(sel).First;
                if (await el.IsVisibleAsync())
                    return await el.InnerTextAsync();
            }
            catch { /* try next selector */ }
        }
        return null;
    }
}
