using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace AUBDigitalAcademy.Services.Implementations;

/// <summary>
/// Redacts Telegram Bot tokens, full Bot API URLs, and sensitive auth payloads
/// before diagnostic details are written to application logs or surfaced in errors.
/// </summary>
public static partial class TelegramLogSanitizer
{
    public const string RedactedPlaceholder = "[REDACTED]";
    private const int DefaultMaxMessageLength = 500;

    [GeneratedRegex(@"(https?://api\.telegram\.org/)bot[^/\s?#&""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TelegramApiUrlBotSegmentRegex();

    [GeneratedRegex(@"\bbot\d+:[A-Za-z0-9_-]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BotPrefixTokenRegex();

    [GeneratedRegex(@"\b\d{6,}:[A-Za-z0-9_-]{20,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex TelegramTokenFormatRegex();

    [GeneratedRegex(@"(Bearer\s+)[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerJwtRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9\-_]{10,}\.[A-Za-z0-9\-_]{10,}\.[A-Za-z0-9\-_]{10,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex RawJwtRegex();

    [GeneratedRegex(@"((?:^|[?&\s|;,])(?:token|password|secret|otp|code|verificationId)=)[^&\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveQueryParamRegex();

    public static string? ResolveBotToken(IConfiguration? configuration)
    {
        string?[] candidates =
        {
            configuration?["TelegramBot:Token"],
            configuration?["Telegram:BotToken"],
            Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string Sanitize(string? input, string? knownBotToken = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var result = input;

        if (!string.IsNullOrWhiteSpace(knownBotToken))
        {
            result = result.Replace(knownBotToken, RedactedPlaceholder, StringComparison.Ordinal);

            var escapedToken = Uri.EscapeDataString(knownBotToken);
            if (!string.Equals(escapedToken, knownBotToken, StringComparison.Ordinal))
            {
                result = result.Replace(escapedToken, RedactedPlaceholder, StringComparison.Ordinal);
            }
        }

        result = TelegramApiUrlBotSegmentRegex().Replace(result, $"$1bot{RedactedPlaceholder}");
        result = BotPrefixTokenRegex().Replace(result, $"bot{RedactedPlaceholder}");
        result = TelegramTokenFormatRegex().Replace(result, RedactedPlaceholder);
        result = BearerJwtRegex().Replace(result, $"$1{RedactedPlaceholder}");
        result = RawJwtRegex().Replace(result, "[REDACTED_JWT]");
        result = SensitiveQueryParamRegex().Replace(result, $"$1{RedactedPlaceholder}");

        return result;
    }

    public static string SanitizeMessage(string? message, string? knownBotToken = null, int maxLength = DefaultMaxMessageLength)
    {
        var sanitized = Sanitize(message, knownBotToken);
        if (maxLength > 0 && sanitized.Length > maxLength)
        {
            return sanitized[..maxLength] + "...";
        }

        return sanitized;
    }

    public static string SanitizeUri(Uri? uri, string? knownBotToken = null)
    {
        if (uri == null)
        {
            return "[unknown-uri]";
        }

        return Sanitize(uri.OriginalString, knownBotToken);
    }

    public static string GetOperationName(Uri? uri)
    {
        if (uri == null)
        {
            return "unknown";
        }

        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString.Split('?', 2)[0];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return "unknown";
        }

        return segments[^1];
    }

    public static string SanitizeTelegramCommandForLog(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "[media/non-text]";
        }

        var trimmed = text.Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        if (command.Contains('@'))
        {
            command = command.Split('@', 2)[0];
        }

        return command switch
        {
            "/start" when parts.Length > 1 && parts[1].StartsWith("link_", StringComparison.OrdinalIgnoreCase)
                => $"/start link_{RedactedPlaceholder}",
            "/start" => "/start",
            "/link" when parts.Length > 1 => $"/link {RedactedPlaceholder}",
            "/link" => "/link",
            "/help" => "/help",
            "/status" => "/status",
            "/about" => "/about",
            _ => $"[text message redacted, length={trimmed.Length}]"
        };
    }

    public static RedactedTelegramException SanitizeException(Exception exception, string? knownBotToken = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var messageBuilder = new StringBuilder();
        var current = exception;
        var depth = 0;

        while (current != null && depth < 10)
        {
            if (depth > 0)
            {
                messageBuilder.Append(" ---> ");
            }

            messageBuilder.Append(current.GetType().Name);
            var safePart = Sanitize(current.Message, knownBotToken);
            if (!string.IsNullOrWhiteSpace(safePart))
            {
                messageBuilder.Append(": ").Append(safePart);
            }

            current = current.InnerException;
            depth++;
        }

        var sanitizedMessage = messageBuilder.ToString();
        var sanitizedToString = Sanitize(exception.ToString(), knownBotToken);
        var sanitizedStackTrace = Sanitize(exception.StackTrace, knownBotToken);

        return new RedactedTelegramException(
            sanitizedMessage,
            sanitizedToString,
            sanitizedStackTrace,
            exception.GetType().FullName ?? exception.GetType().Name);
    }
}

/// <summary>
/// Exception wrapper carrying sanitized diagnostic details (type, message, inner messages, and stack trace)
/// without retaining an unredacted InnerException chain that logger providers could emit verbatim.
/// </summary>
public sealed class RedactedTelegramException : Exception
{
    private readonly string _sanitizedToString;
    private readonly string _sanitizedStackTrace;

    public RedactedTelegramException(
        string sanitizedMessage,
        string sanitizedToString,
        string sanitizedStackTrace,
        string originalExceptionType)
        : base(sanitizedMessage)
    {
        _sanitizedToString = sanitizedToString;
        _sanitizedStackTrace = sanitizedStackTrace;
        OriginalExceptionType = originalExceptionType;
    }

    public string OriginalExceptionType { get; }

    public override string? StackTrace => string.IsNullOrEmpty(_sanitizedStackTrace) ? base.StackTrace : _sanitizedStackTrace;

    public override string ToString() => _sanitizedToString;
}
