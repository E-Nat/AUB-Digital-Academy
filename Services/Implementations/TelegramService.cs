using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AUBDigitalAcademy.Services.Implementations;

public class TelegramService : ITelegramService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramService> _logger;
    private readonly IHostEnvironment? _hostEnvironment;

    public TelegramService(
        HttpClient httpClient,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramService> logger,
        IHostEnvironment? hostEnvironment = null)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
    }

    private static string? FirstNonWhiteSpace(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Safely resolves the Telegram Bot token without logging or exposing the actual value.
    /// Prioritizes 'TelegramBot:Token' (.NET User Secrets), with fallback to 'Telegram:BotToken' or environment variable.
    /// </summary>
    private string? GetBotToken()
    {
        return TelegramLogSanitizer.ResolveBotToken(_configuration);
    }

    public async Task<(bool Success, string? BotUsername, string? ErrorMessage)> TestBotConnectivityAsync()
    {
        var botToken = GetBotToken();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogWarning("Telegram Bot token is not configured in 'TelegramBot:Token' or 'Telegram:BotToken'.");
            return (false, null, "Bot token was not found in 'TelegramBot:Token', 'Telegram:BotToken', or environment variables.");
        }

        try
        {
            var url = $"https://api.telegram.org/bot{botToken}/getMe";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean() &&
                    root.TryGetProperty("result", out var resultProp))
                {
                    string? username = resultProp.TryGetProperty("username", out var u) ? u.GetString() : null;
                    string? firstName = resultProp.TryGetProperty("first_name", out var f) ? f.GetString() : null;

                    _logger.LogInformation("Telegram Bot connectivity verified. Connected to @{BotUsername} ({FirstName}).", username, firstName);
                    return (true, username, null);
                }
            }

            _logger.LogWarning("Telegram Bot API {Operation} returned non-success HTTP status {StatusCode}.", "getMe", (int)response.StatusCode);
            return (false, null, $"Telegram API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})");
        }
        catch (Exception ex)
        {
            var safeEx = TelegramLogSanitizer.SanitizeException(ex, botToken);
            _logger.LogError(
                safeEx,
                "Exception occurred while testing Telegram Bot API connectivity (Operation={Operation}, StatusCode={StatusCode}).",
                "getMe",
                (ex as HttpRequestException)?.StatusCode is { } sc ? (int?)sc : null);
            return (false, null, safeEx.Message);
        }
    }

    public async Task<bool> SendVerificationCodeAsync(string universityId, string studentName, string verificationCode, string? customChatId = null)
    {
        // 1. Resolve Bot Token safely
        var botToken = GetBotToken();

        // 2. Resolve Chat ID
        var chatId = FirstNonWhiteSpace(
            customChatId,
            _configuration["Telegram:ChatId"],
            Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID"));

        // 3. Optional Message Thread ID (e.g. forum topic)
        var threadId = FirstNonWhiteSpace(
            _configuration["Telegram:MessageThreadId"],
            Environment.GetEnvironmentVariable("TELEGRAM_MESSAGE_THREAD_ID"));

        // Format message
        var message = $"🎓 <b>AUB Digital Academy — Student Verification</b>\n\n" +
                      $"👤 Student: <b>{WebUtility.HtmlEncode(studentName)}</b>\n" +
                      $"🆔 Student ID: <code>{WebUtility.HtmlEncode(universityId)}</code>\n\n" +
                      $"🔑 Verification Code: <b><code>{verificationCode}</code></b>\n\n" +
                      $"⏳ <i>This code expires in 10 minutes and is valid for a single use.</i>\n" +
                      $"⚠️ <i>Never share your verification code with anyone.</i>";

        // Dev / Simulation fallback if credentials are not configured in local environment
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            if (_hostEnvironment == null || _hostEnvironment.IsDevelopment())
            {
                _logger.LogInformation("Telegram service: Simulation mode active for Student ID {UniversityId} ({StudentName}). OTP value suppressed from logs. (Configure 'Telegram:ChatId' or link account via /link in @AUBLinkBot to receive real messages).", universityId, studentName);
            }
            else
            {
                _logger.LogWarning("Telegram service: Credentials or ChatId not configured in {EnvironmentName} environment for Student ID {UniversityId}. Real Telegram delivery skipped; OTP value suppressed from logs.", _hostEnvironment.EnvironmentName, universityId);
            }
            return true;
        }

        try
        {
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var formValues = new Dictionary<string, string>
            {
                { "chat_id", chatId },
                { "text", message },
                { "parse_mode", "HTML" }
            };

            // message_thread_id is ONLY valid for forum supergroups (IDs starting with '-100')
            // It must NEVER be sent to private user chats (positive IDs)
            if (!string.IsNullOrWhiteSpace(threadId) && string.IsNullOrEmpty(customChatId) && chatId.StartsWith("-"))
            {
                formValues.Add("message_thread_id", threadId);
            }

            using var content = new FormUrlEncodedContent(formValues);
            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telegram verification code delivered successfully for Student ID {UniversityId} to ChatId {ChatId}.", universityId, chatId);
                return true;
            }

            var errorDetails = await response.Content.ReadAsStringAsync();
            var safeErrorDetails = TelegramLogSanitizer.SanitizeMessage(errorDetails, botToken);
            _logger.LogWarning(
                "Telegram API {Operation} returned non-success HTTP status {StatusCode} for Student ID {UniversityId}: {ErrorDetails}",
                "sendMessage",
                (int)response.StatusCode,
                universityId,
                safeErrorDetails);
            return false;
        }
        catch (Exception ex)
        {
            var safeEx = TelegramLogSanitizer.SanitizeException(ex, botToken);
            _logger.LogError(
                safeEx,
                "Failed to deliver Telegram verification code for Student ID {UniversityId} (Operation={Operation}, StatusCode={StatusCode}).",
                universityId,
                "sendMessage",
                (ex as HttpRequestException)?.StatusCode is { } sc ? (int?)sc : null);
            return false;
        }
    }

    public async Task<bool> SendMessageAsync(long chatId, string text, string parseMode = "HTML", CancellationToken cancellationToken = default)
    {
        var botToken = GetBotToken();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogWarning("Telegram Bot token is not configured. Cannot send message to chat {ChatId}.", chatId);
            return false;
        }

        try
        {
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var formValues = new Dictionary<string, string>
            {
                { "chat_id", chatId.ToString() },
                { "text", text },
                { "parse_mode", parseMode }
            };

            using var content = new FormUrlEncodedContent(formValues);
            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telegram message sent successfully to chat {ChatId}.", chatId);
                return true;
            }

            var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var safeErrBody = TelegramLogSanitizer.SanitizeMessage(errBody, botToken);
            _logger.LogWarning(
                "Telegram {Operation} returned HTTP {StatusCode} for chat {ChatId}: {ErrorBody}",
                "sendMessage",
                (int)response.StatusCode,
                chatId,
                safeErrBody);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var safeEx = TelegramLogSanitizer.SanitizeException(ex, botToken);
            _logger.LogError(
                safeEx,
                "Exception while sending Telegram message to chat {ChatId} (Operation={Operation}, StatusCode={StatusCode}).",
                chatId,
                "sendMessage",
                (ex as HttpRequestException)?.StatusCode is { } sc ? (int?)sc : null);
            return false;
        }
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset = null, int timeoutSeconds = 20, CancellationToken cancellationToken = default)
    {
        var botToken = GetBotToken();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogWarning("Telegram Bot token is not configured. Cannot poll for updates.");
            return Array.Empty<TelegramUpdate>();
        }

        try
        {
            var url = $"https://api.telegram.org/bot{botToken}/getUpdates?timeout={timeoutSeconds}";
            if (offset.HasValue)
            {
                url += $"&offset={offset.Value}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var safeErrBody = TelegramLogSanitizer.SanitizeMessage(errBody, botToken);
                _logger.LogWarning(
                    "Telegram {Operation} returned HTTP {StatusCode}: {ErrorBody}",
                    "getUpdates",
                    (int)response.StatusCode,
                    safeErrBody);
                return Array.Empty<TelegramUpdate>();
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean() &&
                root.TryGetProperty("result", out var resultProp) &&
                resultProp.ValueKind == JsonValueKind.Array)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var updates = JsonSerializer.Deserialize<List<TelegramUpdate>>(resultProp.GetRawText(), options);
                return updates ?? (IReadOnlyList<TelegramUpdate>)Array.Empty<TelegramUpdate>();
            }

            return Array.Empty<TelegramUpdate>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var safeEx = TelegramLogSanitizer.SanitizeException(ex, botToken);
            _logger.LogError(
                safeEx,
                "Exception while polling Telegram updates (Operation={Operation}, StatusCode={StatusCode}).",
                "getUpdates",
                (ex as HttpRequestException)?.StatusCode is { } sc ? (int?)sc : null);
            return Array.Empty<TelegramUpdate>();
        }
    }

    public async Task<(string LinkUrl, string PlainToken, DateTime ExpiresAt)> GenerateLinkTokenAsync(string userId, string? clientIp = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Invalidate prior unused tokens for this user
        var existingTokens = await context.TelegramLinkTokens
            .Where(t => t.UserId == userId && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var t in existingTokens)
        {
            t.IsUsed = true;
            t.UsedAt = DateTime.UtcNow;
        }

        // Generate 32-byte cryptographic random token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var plainToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken));
        var tokenHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var expiresAt = DateTime.UtcNow.AddMinutes(15);
        var linkToken = new TelegramLinkToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            IsUsed = false,
            CreatedByIp = clientIp
        };

        context.TelegramLinkTokens.Add(linkToken);
        await context.SaveChangesAsync();

        var botUsername = _configuration["Telegram:BotUsername"] ?? "AUBLinkBot";
        var linkUrl = $"https://t.me/{botUsername}?start=link_{plainToken}";
        return (linkUrl, plainToken, expiresAt);
    }

    public async Task<(bool Success, string Message, User? User)> ProcessLinkTokenAsync(string plainToken, long chatId, string? telegramUsername)
    {
        if (string.IsNullOrWhiteSpace(plainToken))
        {
            return (false, "Missing or invalid token.", null);
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken.Trim()));
        var tokenHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var linkToken = await context.TelegramLinkTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (linkToken == null || linkToken.IsUsed || DateTime.UtcNow > linkToken.ExpiresAt)
        {
            return (false, "This link token is invalid, expired, or has already been used. Please generate a fresh link in the AUB Portal.", null);
        }

        var user = linkToken.User;
        if (user == null)
        {
            return (false, "Associated university user account was not found.", null);
        }

        // Enforce uniqueness: Check if this Telegram Chat ID is already bound to another active user
        var isChatTakenByOther = await context.Users
            .AnyAsync(u => u.TelegramChatId == chatId && u.Id != user.Id);

        if (isChatTakenByOther)
        {
            return (false, "This Telegram account is already linked to another university account. Contact IT Support if you believe this is an error.", null);
        }

        // Mark token used
        linkToken.IsUsed = true;
        linkToken.UsedAt = DateTime.UtcNow;

        // Update user profile
        user.TelegramChatId = chatId;
        user.TelegramUsername = telegramUsername;
        user.IsTelegramVerified = true;
        user.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        _logger.LogInformation("Telegram account linked successfully: Student {UniversityId} ({UserId}) bound to ChatId {ChatId} (@{TelegramUsername}).",
            user.UniversityId, user.Id, chatId, telegramUsername ?? "none");

        return (true, $"Account successfully linked to {user.FullName} ({user.UniversityId ?? user.UserName}).", user);
    }

    public async Task<bool> IsAccountLinkedAsync(string userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Users.AnyAsync(u => u.Id == userId && u.TelegramChatId.HasValue && u.IsTelegramVerified);
    }
}
