using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AUBDigitalAcademy.Services.Implementations;

public class TelegramPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramPollingService> _logger;

    public TelegramPollingService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TelegramPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botToken = TelegramLogSanitizer.ResolveBotToken(_configuration);

        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogWarning("TelegramPollingService: Telegram Bot token is not configured. Polling background service is disabled.");
            return;
        }

        if (_configuration.GetValue<bool>("Telegram:DisablePolling", false))
        {
            _logger.LogInformation("TelegramPollingService: Telegram polling is disabled via configuration ('Telegram:DisablePolling' = true).");
            return;
        }

        _logger.LogInformation("TelegramPollingService: Starting Telegram Bot long polling background service...");

        long? offset = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var telegramService = scope.ServiceProvider.GetRequiredService<ITelegramService>();

                var updates = await telegramService.GetUpdatesAsync(offset, timeoutSeconds: 20, stoppingToken);

                foreach (var update in updates)
                {
                    offset = update.UpdateId + 1;
                    await HandleUpdateAsync(telegramService, update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException ex)
            {
                var safeMessage = TelegramLogSanitizer.SanitizeMessage(ex.Message, botToken);
                _logger.LogWarning(
                    "TelegramPollingService: Network connectivity issue while polling Telegram API (Operation={Operation}, StatusCode={StatusCode}): {Message}. Retrying in 5 seconds...",
                    "getUpdates",
                    ex.StatusCode.HasValue ? (int?)ex.StatusCode.Value : null,
                    safeMessage);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                var safeEx = TelegramLogSanitizer.SanitizeException(ex, botToken);
                _logger.LogError(
                    safeEx,
                    "TelegramPollingService: Unexpected exception in polling loop (Operation={Operation}). Retrying in 5 seconds...",
                    "getUpdates");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("TelegramPollingService: Telegram Bot long polling background service stopped.");
    }

    private async Task HandleUpdateAsync(ITelegramService telegramService, TelegramUpdate update, CancellationToken stoppingToken)
    {
        if (update.Message == null || update.Message.Chat == null)
        {
            return;
        }

        var message = update.Message;
        var chatId = message.Chat.Id;
        var text = message.Text?.Trim();
        var fromUser = message.From;
        var senderName = fromUser?.FirstName ?? "Student";

        _logger.LogInformation(
            "TelegramPollingService: Received message from ChatId {ChatId} (@{Username}): {CommandSummary}",
            chatId,
            fromUser?.Username ?? "none",
            TelegramLogSanitizer.SanitizeTelegramCommandForLog(text));

        if (string.IsNullOrWhiteSpace(text))
        {
            await telegramService.SendMessageAsync(
                chatId,
                $"👋 Hello <b>{WebUtility.HtmlEncode(senderName)}</b>!\n\nI received your media/attachment. Send <b>/help</b> to see available commands.",
                cancellationToken: stoppingToken);
            return;
        }

        // Parse command
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";

        // Remove bot mention suffix if present (e.g. /start@AUBLinkBot -> /start)
        if (command.Contains('@'))
        {
            command = command.Split('@')[0];
        }

        switch (command)
        {
            case "/start":
                // Check if this is a deep link: /start link_<token>
                if (parts.Length > 1 && parts[1].StartsWith("link_", StringComparison.OrdinalIgnoreCase))
                {
                    var token = parts[1].Substring(5).Trim();
                    var (linkSuccess, linkMsg, linkedUser) = await telegramService.ProcessLinkTokenAsync(token, chatId, fromUser?.Username);
                    if (linkSuccess)
                    {
                        var successReply =
                            $"✅ <b>Account Linked Successfully!</b>\n\n" +
                            $"👤 Name: <b>{WebUtility.HtmlEncode(linkedUser?.FullName ?? "")}</b>\n" +
                            $"🆔 ID: <code>{WebUtility.HtmlEncode(linkedUser?.UniversityId ?? linkedUser?.UserName ?? "")}</code>\n" +
                            $"📱 Telegram Chat ID: <code>{chatId}</code>\n\n" +
                            $"Your Telegram profile is now securely paired with your AUB Digital Academy account. Single-use login verification codes will be delivered here.";
                        await telegramService.SendMessageAsync(chatId, successReply, cancellationToken: stoppingToken);
                    }
                    else
                    {
                        var failReply =
                            $"❌ <b>Account Linking Failed:</b>\n\n{WebUtility.HtmlEncode(linkMsg)}\n\n" +
                            $"Please log in to the AUB Portal to generate a fresh linking link.";
                        await telegramService.SendMessageAsync(chatId, failReply, cancellationToken: stoppingToken);
                    }
                    break;
                }

                var welcomeMessage =
                    $"🎓 <b>Welcome to AUB Digital Academy Bot!</b>\n\n" +
                    $"Hello <b>{WebUtility.HtmlEncode(senderName)}</b>! I am the official Telegram assistant for <b>ACLEDA University of Business Digital Academy</b>.\n\n" +
                    $"💡 <b>What can this bot do?</b>\n" +
                    $"• Deliver instant verification codes for student & faculty logins\n" +
                    $"• Link your Telegram profile securely with your academic portal\n" +
                    $"• Provide academic updates and notification alerts\n\n" +
                    $"📌 <b>Available Commands:</b>\n" +
                    $"• <b>/start</b> — Show this welcome guide\n" +
                    $"• <b>/link &lt;Token&gt;</b> — Link your Telegram account using a secure portal token\n" +
                    $"• <b>/status</b> — Check your Telegram Chat ID and linking status\n" +
                    $"• <b>/help</b> — Display instructions and commands\n" +
                    $"• <b>/about</b> — Information about AUB Digital Academy\n\n" +
                    $"<i>To link your student account, sign in on the AUB web portal and click 'Link Telegram Account'.</i>";

                await telegramService.SendMessageAsync(chatId, welcomeMessage, cancellationToken: stoppingToken);
                break;

            case "/help":
                var helpMessage =
                    $"ℹ️ <b>AUB Digital Academy Bot — Help Guide</b>\n\n" +
                    $"Here are the commands you can use:\n\n" +
                    $"• <b>/start</b> — Welcome message and introduction\n" +
                    $"• <b>/status</b> — View your Chat ID (<code>{chatId}</code>) and connection status\n" +
                    $"• <b>/about</b> — Learn about ACLEDA University of Business\n" +
                    $"• <b>/help</b> — Show this help message\n\n" +
                    $"🔐 <b>Account Verification & 2FA:</b>\n" +
                    $"When signing in to the AUB portal with Telegram 2FA, your single-use code will be delivered here automatically.\n\n" +
                    $"Need further assistance? Contact IT Support at <a href=\"mailto:it.support@aub.edu.kh\">it.support@aub.edu.kh</a>.";

                await telegramService.SendMessageAsync(chatId, helpMessage, cancellationToken: stoppingToken);
                break;

            case "/status":
                var usernameDisplay = !string.IsNullOrWhiteSpace(fromUser?.Username)
                    ? $"@{fromUser.Username}"
                    : "<i>(No username set)</i>";

                var statusMessage =
                    $"📋 <b>Your Telegram Connection Status</b>\n\n" +
                    $"• <b>Chat ID:</b> <code>{chatId}</code>\n" +
                    $"• <b>Username:</b> {usernameDisplay}\n" +
                    $"• <b>Name:</b> {WebUtility.HtmlEncode(fromUser?.FirstName ?? "")} {WebUtility.HtmlEncode(fromUser?.LastName ?? "")}\n" +
                    $"• <b>Bot Status:</b> 🟢 Active &amp; Operational\n\n" +
                    $"<i>Keep this Chat ID handy if requested during account linking.</i>";

                await telegramService.SendMessageAsync(chatId, statusMessage, cancellationToken: stoppingToken);
                break;

            case "/link":
                if (parts.Length < 2)
                {
                    await telegramService.SendMessageAsync(chatId,
                        "⚠️ <b>Usage:</b> <code>/link &lt;Token&gt;</code>\n\n" +
                        "For security, account linking requires a single-use token generated from your authenticated session on the AUB Portal.\n" +
                        "Sign in to the web portal and click <b>'Link Telegram Account'</b> to open your personalized linking link.",
                        cancellationToken: stoppingToken);
                    break;
                }

                var rawToken = parts[1].Trim();
                if (rawToken.StartsWith("link_", StringComparison.OrdinalIgnoreCase))
                {
                    rawToken = rawToken.Substring(5).Trim();
                }

                var (tokenSuccess, tokenMsg, userObj) = await telegramService.ProcessLinkTokenAsync(rawToken, chatId, fromUser?.Username);
                if (tokenSuccess)
                {
                    var successReply =
                        $"✅ <b>Account Linked Successfully!</b>\n\n" +
                        $"👤 Name: <b>{WebUtility.HtmlEncode(userObj?.FullName ?? "")}</b>\n" +
                        $"🆔 ID: <code>{WebUtility.HtmlEncode(userObj?.UniversityId ?? userObj?.UserName ?? "")}</code>\n" +
                        $"📱 Telegram Chat ID: <code>{chatId}</code>\n\n" +
                        $"Your Telegram profile is now securely paired with your AUB Digital Academy account. Single-use login verification codes will be delivered here.";
                    await telegramService.SendMessageAsync(chatId, successReply, cancellationToken: stoppingToken);
                }
                else
                {
                    var failReply =
                        $"❌ <b>Account Linking Failed:</b>\n\n{WebUtility.HtmlEncode(tokenMsg)}\n\n" +
                        $"Please log in to the AUB Portal to generate a fresh linking link.";
                    await telegramService.SendMessageAsync(chatId, failReply, cancellationToken: stoppingToken);
                }
                break;

            case "/about":
                var aboutMessage =
                    $"🏛️ <b>About AUB Digital Academy</b>\n\n" +
                    $"ACLEDA University of Business (AUB) Digital Academy is an institutional learning platform empowering students in Cambodia with premier digital education.\n\n" +
                    $"🌐 <b>Website:</b> https://aub.edu.kh\n" +
                    $"📞 <b>Support:</b> +855 (0) 23 999 000\n" +
                    $"📍 <b>Location:</b> Phnom Penh, Cambodia";

                await telegramService.SendMessageAsync(chatId, aboutMessage, cancellationToken: stoppingToken);
                break;

            default:
                var fallbackReply =
                    $"👋 Hello <b>{WebUtility.HtmlEncode(senderName)}</b>!\n\n" +
                    $"I received: <i>{WebUtility.HtmlEncode(text.Length > 50 ? text.Substring(0, 50) + "..." : text)}</i>\n\n" +
                    $"Use <b>/help</b> to see all supported commands, or <b>/status</b> to check your Chat ID.";

                await telegramService.SendMessageAsync(chatId, fallbackReply, cancellationToken: stoppingToken);
                break;
        }
    }
}
