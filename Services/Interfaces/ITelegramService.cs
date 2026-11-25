using AUBDigitalAcademy.Models;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface ITelegramService
{
    /// <summary>
    /// Dispatches a single-use verification code to the configured Telegram destination.
    /// Preserves the existing HTTP delivery mechanism without exposing codes or credentials.
    /// </summary>
    Task<bool> SendVerificationCodeAsync(string universityId, string studentName, string verificationCode, string? customChatId = null);

    /// <summary>
    /// Tests connectivity with the Telegram Bot API using the configured bot token.
    /// Returns true if the bot token is valid and Telegram API responds with HTTP 200 (getMe).
    /// Never logs or exposes the token.
    /// </summary>
    Task<(bool Success, string? BotUsername, string? ErrorMessage)> TestBotConnectivityAsync();

    /// <summary>
    /// Sends a text message to a specified Telegram chat.
    /// </summary>
    Task<bool> SendMessageAsync(long chatId, string text, string parseMode = "HTML", CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches pending updates from Telegram using long polling.
    /// </summary>
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset = null, int timeoutSeconds = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a single-use, cryptographically secure deep-link token for account linking.
    /// </summary>
    Task<(string LinkUrl, string PlainToken, DateTime ExpiresAt)> GenerateLinkTokenAsync(string userId, string? clientIp = null);

    /// <summary>
    /// Validates a single-use deep-link token and binds the Telegram Chat ID to the user.
    /// </summary>
    Task<(bool Success, string Message, User? User)> ProcessLinkTokenAsync(string plainToken, long chatId, string? telegramUsername);

    /// <summary>
    /// Checks whether the user has a verified linked Telegram chat ID.
    /// </summary>
    Task<bool> IsAccountLinkedAsync(string userId);
}
