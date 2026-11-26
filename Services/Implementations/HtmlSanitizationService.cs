using System.Net;
using AngleSharp.Dom;
using Ganss.Xss;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

/// <summary>
/// Centralized HTML and URL sanitization service backed by Ganss.Xss.HtmlSanitizer
/// with an explicit allowlist policy tailored for educational rich content.
/// </summary>
public sealed class HtmlSanitizationService : IHtmlSanitizationService
{
    private static readonly string[] AllowedLessonTags =
    [
        // Structural & text blocks
        "p", "div", "span", "br", "hr", "blockquote", "pre", "code",
        // Headings
        "h1", "h2", "h3", "h4", "h5", "h6",
        // Inline educational formatting
        "b", "strong", "i", "em", "u", "s", "del", "ins", "mark", "small",
        "sub", "sup", "kbd", "samp", "var", "abbr", "cite", "q",
        // Lists
        "ul", "ol", "li", "dl", "dt", "dd",
        // Tables
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "colgroup", "col",
        // Safe links & images
        "a", "img", "figure", "figcaption"
    ];

    private static readonly string[] AllowedLessonAttributes =
    [
        // Global safe attributes (no id/name to prevent DOM clobbering, no style, no on*)
        "class", "title", "dir", "lang", "aria-label",
        // Links
        "href", "target", "rel",
        // Images
        "src", "alt", "width", "height", "loading",
        // Tables
        "colspan", "rowspan", "scope", "headers",
        // Lists / Quotes
        "start", "type", "reversed", "cite"
    ];

    private readonly ThreadLocal<HtmlSanitizer> _lessonSanitizer = new(CreateLessonSanitizer);

    private static HtmlSanitizer CreateLessonSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in AllowedLessonTags)
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        foreach (var attr in AllowedLessonAttributes)
        {
            sanitizer.AllowedAttributes.Add(attr);
        }

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");

        sanitizer.UriAttributes.Clear();
        sanitizer.UriAttributes.Add("href");
        sanitizer.UriAttributes.Add("src");
        sanitizer.UriAttributes.Add("cite");

        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();
        sanitizer.AllowDataAttributes = false;

        sanitizer.PostProcessNode += (_, args) =>
        {
            if (args.Node is not IElement element)
            {
                return;
            }

            var tagName = element.TagName;

            if (string.Equals(tagName, "A", StringComparison.OrdinalIgnoreCase))
            {
                var href = element.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href) && !IsSafeAnchorHref(href))
                {
                    element.RemoveAttribute("href");
                }

                var target = element.GetAttribute("target");
                if (!string.IsNullOrWhiteSpace(target))
                {
                    if (string.Equals(target, "_blank", StringComparison.OrdinalIgnoreCase))
                    {
                        element.SetAttribute("target", "_blank");
                        element.SetAttribute("rel", "noopener noreferrer");
                    }
                    else
                    {
                        element.RemoveAttribute("target");
                    }
                }
            }
            else if (string.Equals(tagName, "IMG", StringComparison.OrdinalIgnoreCase))
            {
                var src = element.GetAttribute("src");
                var safeSrc = SanitizeResourceUrlCore(src);
                if (safeSrc == null)
                {
                    element.RemoveAttribute("src");
                }
                else
                {
                    element.SetAttribute("src", safeSrc);
                }
            }
            else if (element.HasAttribute("cite"))
            {
                var cite = element.GetAttribute("cite");
                if (SanitizeHttpUrlCore(cite) == null)
                {
                    element.RemoveAttribute("cite");
                }
            }
        };

        return sanitizer;
    }

    public string? SanitizeLessonHtml(string? rawHtml)
    {
        if (string.IsNullOrWhiteSpace(rawHtml))
        {
            return null;
        }

        var sanitizer = _lessonSanitizer.Value!;
        var sanitized = sanitizer.Sanitize(rawHtml);

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return null;
        }

        return sanitized.Trim();
    }

    public string? SanitizeResourceUrl(string? url) => SanitizeResourceUrlCore(url);

    public bool IsSafeResourceUrl(string? url) => SanitizeResourceUrlCore(url) != null;

    public string? SanitizeHttpUrl(string? url) => SanitizeHttpUrlCore(url);

    public bool IsSafeHttpUrl(string? url) => SanitizeHttpUrlCore(url) != null;

    private static bool IsSafeAnchorHref(string href)
    {
        var trimmed = href.Trim();
        if (string.IsNullOrEmpty(trimmed) || ContainsControlCharacters(trimmed))
        {
            return false;
        }

        if (trimmed.StartsWith('#'))
        {
            return true;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absUri))
        {
            return absUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                   || absUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                   || absUri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
        }

        return SanitizeResourceUrlCore(trimmed) != null;
    }

    private static string? SanitizeHttpUrlCore(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();
        if (ContainsControlCharacters(trimmed))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(trimmed);
        if (ContainsControlCharacters(decoded) || decoded.Contains('\\'))
        {
            return null;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return trimmed;
        }

        return null;
    }

    private static string? SanitizeResourceUrlCore(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();
        if (ContainsControlCharacters(trimmed) || trimmed.Contains('\\'))
        {
            return null;
        }

        // Decode entities to catch obfuscated schemes like java&#x09;script:
        var decoded = WebUtility.HtmlDecode(trimmed).Trim();
        if (ContainsControlCharacters(decoded) || decoded.Contains('\\'))
        {
            return null;
        }

        // 1. Absolute HTTP/HTTPS URL
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absUri))
        {
            if (absUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                absUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
            return null;
        }

        // Reject protocol-relative URLs (//evil.com) or scheme-like prefixes
        if (decoded.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        var firstSlash = decoded.IndexOf('/');
        var firstColon = decoded.IndexOf(':');
        if (firstColon >= 0 && (firstSlash == -1 || firstColon < firstSlash))
        {
            return null;
        }

        // 2. Single-slash app-relative path (e.g. /images/course_webdev.jpg)
        if (trimmed.StartsWith('/'))
        {
            if (trimmed.Length > 1 && trimmed[1] == '/')
            {
                return null;
            }
            return trimmed;
        }

        // 3. Safe relative asset path without scheme (e.g. assets/images/course_webdev.jpg)
        if (!trimmed.Contains("..", StringComparison.Ordinal) &&
            !trimmed.StartsWith('#'))
        {
            return trimmed;
        }

        return null;
    }

    private static bool ContainsControlCharacters(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                return true;
            }
        }
        return false;
    }
}
