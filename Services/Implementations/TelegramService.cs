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
                _logger.LogInformation("Telegram verification code delivered successfully for Student ID {UniversityId} to ChatId {MaskedChatId}.", universityId, TelegramLogSanitizer.MaskChatId(chatId));
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

    public async Task<bool> SendPasswordResetCodeAsync(string universityId, string fullName, string verificationCode, string customChatId)
    {
        if (string.IsNullOrWhiteSpace(customChatId) || !long.TryParse(customChatId.Trim(), out var parsedChatId) || parsedChatId <= 0)
        {
            _logger.LogWarning("Telegram password reset code delivery rejected: invalid or missing personal ChatId.");
            return false;
        }

        var botToken = GetBotToken();
        var maskedChatId = TelegramLogSanitizer.MaskChatId(parsedChatId);

        if (string.IsNullOrWhiteSpace(botToken))
        {
            if (_hostEnvironment == null || _hostEnvironment.IsDevelopment())
            {
                _logger.LogInformation("Telegram service: Simulation mode active for password reset (ChatId {MaskedChatId}). OTP value suppressed from logs.", maskedChatId);
            }
            else
            {
                _logger.LogWarning("Telegram service: Bot token not configured in {EnvironmentName} environment for password reset. OTP value suppressed from logs.", _hostEnvironment.EnvironmentName);
            }
            return true;
        }

        var message = $"🔐 <b>AUB Digital Academy — Password Reset Verification</b>\n\n" +
                      $"👤 Account: <b>{WebUtility.HtmlEncode(fullName)}</b>\n" +
                      $"🆔 ID: <code>{WebUtility.HtmlEncode(universityId)}</code>\n\n" +
                      $"🔑 Reset Code: <b><code>{verificationCode}</code></b>\n\n" +
                      $"⏳ <i>This password reset code expires in 10 minutes and is valid for a single use.</i>\n" +
                      $"⚠️ <i>If you did not request a password reset, ignore this message and never share this code.</i>";

        try
        {
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var formValues = new Dictionary<string, string>
            {
                { "chat_id", parsedChatId.ToString() },
                { "text", message },
                { "parse_mode", "HTML" }
            };

            using var content = new FormUrlEncodedContent(formValues);
            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telegram password reset code delivered successfully to ChatId {MaskedChatId}.", maskedChatId);
                return true;
            }

            var errorDetails = await response.Content.ReadAsStringAsync();
            var safeErrorDetails = TelegramLogSanitizer.SanitizeMessage(errorDetails, botToken);
            _logger.LogWarning(
                "Telegram API {Operation} returned non-success HTTP status {StatusCode} for password reset (ChatId {MaskedChatId}): {ErrorDetails}",
                "sendMessage",
                (int)response.StatusCode,
                maskedChatId,
                safeErrorDetails);
            return false;
        }
        catch (Exception ex)
        {
            var safeEx = TelegramLogSanitizer.SanitizeException(ex, botToken);
            _logger.LogError(
                safeEx,
                "Failed to deliver Telegram password reset code to ChatId {MaskedChatId} (Operation={Operation}, StatusCode={StatusCode}).",
                maskedChatId,
                "sendMessage",
                (ex as HttpRequestException)?.StatusCode is { } sc ? (int?)sc : null);
            return false;
        }
    }

    public async Task<bool> SendGoogleSecurityOtpAsync(string universityId, string fullName, string verificationCode, string operationLabel, string customChatId)
    {
        if (string.IsNullOrWhiteSpace(customChatId) || !long.TryParse(customChatId.Trim(), out var parsedChatId) || parsedChatId <= 0)
        {
            _logger.LogWarning("Telegram Google security OTP rejected: missing or invalid personal ChatId.");
            return false;
        }

        // Strict fail-closed requirement: never fall back to a shared/default chat or simulation logging.
        var botToken = GetBotToken();
        var maskedChatId = TelegramLogSanitizer.MaskChatId(parsedChatId);
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogWarning("Telegram Google security OTP failed closed: bot token is not configured (ChatId {MaskedChatId}).", maskedChatId);
            return false;
        }

        var message = $"🛡️ <b>AUB Digital Academy — Google Security Verification</b>\n\n" +
                      $"👤 Account: <b>{WebUtility.HtmlEncode(fullName)}</b>\n" +
                      $"🆔 ID: <code>{WebUtility.HtmlEncode(universityId)}</code>\n" +
                      $"⚙️ Action: <b>{WebUtility.HtmlEncode(operationLabel)}</b>\n\n" +
                      $"🔑 Step-Up Code: <b><code>{verificationCode}</code></b>\n\n" +
                      $"⏳ <i>This security code expires in 10 minutes and is valid for a single use.</i>\n" +
                      $"⚠️ <i>If you did not initiate this request, do not share this code and review your account security.</i>";

        try
        {
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var formValues = new Dictionary<string, string>
            {
                { "chat_id", parsedChatId.ToString() },
                { "text", message },
                { "parse_mode", "HTML" }
            };

            using var content = new FormUrlEncodedContent(formValues);
            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telegram Google security OTP delivered successfully to ChatId {MaskedChatId}.", maskedChatId);
                return true;
            }

            var errorDetails = await response.Content.ReadAsStringAsync();
            var safeErrorDetails = TelegramLogSanitizer.SanitizeMessage(errorDetails, botToken);
            _logger.LogWarning(
                "Telegram API {Operation} returned non-success HTTP status {StatusCode} for Google security OTP (ChatId {MaskedChatId}): {ErrorDetails}",
                "sendMessage",
                (int)response.StatusCode,
                maskedChatId,
                safeErrorDetails);
            return false;
        }
        catch (Exception ex)
        {
            var safeEx = TelegramLogSanitizer.SanitizeException(ex, botToken);
            _logger.LogError(
                safeEx,
                "Failed to deliver Telegram Google security OTP to ChatId {MaskedChatId} (Operation={Operation}, StatusCode={StatusCode}).",
                maskedChatId,
                "sendMessage",
                (ex as HttpRequestException)?.StatusCode is { } sc ? (int?)sc : null);
            return false;
        }
    }

    public async Task<bool> SendMessageAsync(long chatId, string text, string parseMode = "HTML", CancellationToken cancellationToken = default)
    {
        var botToken = GetBotToken();
        var maskedChatId = TelegramLogSanitizer.MaskChatId(chatId);
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogWarning("Telegram Bot token is not configured. Cannot send message to chat {MaskedChatId}.", maskedChatId);
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
                _logger.LogInformation("Telegram message sent successfully to chat {MaskedChatId}.", maskedChatId);
                return true;
            }

            var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var safeErrBody = TelegramLogSanitizer.SanitizeMessage(errBody, botToken);
            _logger.LogWarning(
                "Telegram {Operation} returned HTTP {StatusCode} for chat {MaskedChatId}: {ErrorBody}",
                "sendMessage",
                (int)response.StatusCode,
                maskedChatId,
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
                "Exception while sending Telegram message to chat {MaskedChatId} (Operation={Operation}, StatusCode={StatusCode}).",
                maskedChatId,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || !user.IsActive || (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow))
        {
            throw new InvalidOperationException("User account is not eligible for Telegram linking.");
        }

        if (user.TelegramChatId.HasValue)
        {
            throw new InvalidOperationException("User account already has a linked Telegram association.");
        }

        var now = DateTime.UtcNow;

        // Invalidate prior unused tokens for this user
        await context.TelegramLinkTokens
            .Where(t => t.UserId == userId && !t.IsUsed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.IsUsed, true)
                .SetProperty(t => t.UsedAt, now));

        // Generate 32-byte cryptographic random token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var plainToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken));
        var tokenHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var expiresAt = now.AddMinutes(15);
        var safeIp = string.IsNullOrWhiteSpace(clientIp) ? null : clientIp.Trim();
        if (safeIp != null && safeIp.Length > 50)
        {
            safeIp = safeIp[..50];
        }

        var linkToken = new TelegramLinkToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            IsUsed = false,
            CreatedByIp = safeIp
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

        if (chatId <= 0)
        {
            return (false, "Telegram linking is only permitted from a private Telegram chat.", null);
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken.Trim()));
        var tokenHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        try
        {
            await using var tx = await context.Database.BeginTransactionAsync();

            var now = DateTime.UtcNow;

            // Atomically claim the single-use token first; SQL Server X-lock serializes concurrent claimers
            var claimedRows = await context.TelegramLinkTokens
                .Where(t => t.TokenHash == tokenHash && !t.IsUsed && t.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.IsUsed, true)
                    .SetProperty(t => t.UsedAt, now));

            if (claimedRows != 1)
            {
                await tx.RollbackAsync();
                return (false, "This link token is invalid, expired, or has already been used. Please generate a fresh link in the AUB Portal.", null);
            }

            var linkToken = await context.TelegramLinkTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (linkToken == null)
            {
                await tx.RollbackAsync();
                return (false, "This link token is invalid, expired, or has already been used. Please generate a fresh link in the AUB Portal.", null);
            }

            var user = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == linkToken.UserId);

            if (user == null || !user.IsActive || (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow))
            {
                await tx.RollbackAsync();
                return (false, "Associated university user account was not found or is unavailable.", null);
            }

            // Never silently overwrite an existing TelegramChatId association
            if (user.TelegramChatId.HasValue)
            {
                await tx.RollbackAsync();
                return (false, "This university account already has a linked Telegram account. Contact IT Support if you need to change it.", null);
            }

            // Enforce uniqueness: Check if this Telegram Chat ID is already bound to another user
            var isChatTakenByOther = await context.Users
                .AnyAsync(u => u.TelegramChatId == chatId && u.Id != user.Id);

            if (isChatTakenByOther)
            {
                await tx.RollbackAsync();
                return (false, "This Telegram account is already linked to another university account. Contact IT Support if you believe this is an error.", null);
            }

            var safeUsername = string.IsNullOrWhiteSpace(telegramUsername)
                ? null
                : telegramUsername.Trim().TrimStart('@');
            if (safeUsername != null && safeUsername.Length > 100)
            {
                safeUsername = safeUsername[..100];
            }

            // Atomically bind TelegramChatId ONLY when still null; never mark IsTelegramVerified = true before OTP verification
            var userUpdatedRows = await context.Users
                .Where(u => u.Id == user.Id && u.IsActive && u.TelegramChatId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.TelegramChatId, chatId)
                    .SetProperty(u => u.TelegramUsername, safeUsername)
                    .SetProperty(u => u.UpdatedAt, now));

            if (userUpdatedRows != 1)
            {
                await tx.RollbackAsync();
                return (false, "This university account already has a linked Telegram account or is unavailable.", null);
            }

            // Invalidate any other unused tokens for this user
            await context.TelegramLinkTokens
                .Where(t => t.UserId == user.Id && !t.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.IsUsed, true)
                    .SetProperty(t => t.UsedAt, now));

            await tx.CommitAsync();

            var updatedUser = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == user.Id);

            _logger.LogInformation("Telegram chat linked for user {UserId} (ChatId {MaskedChatId}); awaiting OTP verification.",
                user.Id, TelegramLogSanitizer.MaskChatId(chatId));

            return (true, $"Account successfully linked to {user.FullName} ({user.UniversityId ?? user.UserName}). Please return to the AUB Portal to enter your verification code.", updatedUser);
        }
        catch (DbUpdateException)
        {
            return (false, "This Telegram account is already linked to another university account. Contact IT Support if you believe this is an error.", null);
        }
    }

    public async Task<bool> IsAccountLinkedAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        return await context.Users.AnyAsync(u =>
            u.Id == userId &&
            u.IsActive &&
            (!u.LockoutEnabled || !u.LockoutEnd.HasValue || u.LockoutEnd.Value <= now) &&
            u.TelegramChatId.HasValue &&
            u.TelegramChatId.Value > 0);
    }
}
