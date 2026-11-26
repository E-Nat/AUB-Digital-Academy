namespace AUBDigitalAcademy.Services.Interfaces;

/// <summary>
/// Provides centralized, allowlist-based HTML sanitization and URL protocol validation
/// to prevent Stored Cross-Site Scripting (XSS) across lesson content, course media,
/// books/resources, and external links.
/// </summary>
public interface IHtmlSanitizationService
{
    /// <summary>
    /// Sanitizes lesson HTML content using an explicit allowlist of safe educational tags,
    /// attributes, and URL schemes. Returns null when input is null, whitespace, or
    /// reduced to empty after stripping disallowed markup.
    /// </summary>
    string? SanitizeLessonHtml(string? rawHtml);

    /// <summary>
    /// Validates and normalizes a resource/media URL (allows http://, https://,
    /// single-slash app-relative paths, or safe relative asset paths).
    /// Returns null if the URL is empty or uses a dangerous scheme (e.g. javascript:, data:, vbscript:).
    /// </summary>
    string? SanitizeResourceUrl(string? url);

    /// <summary>
    /// Returns true if the provided URL is a valid, safe resource/media URL.
    /// </summary>
    bool IsSafeResourceUrl(string? url);

    /// <summary>
    /// Validates and normalizes an external HTTP/HTTPS URL (strictly requires http:// or https://).
    /// Returns null if empty or invalid.
    /// </summary>
    string? SanitizeHttpUrl(string? url);

    /// <summary>
    /// Returns true if the provided URL is a valid absolute http:// or https:// URL.
    /// </summary>
    bool IsSafeHttpUrl(string? url);
}
