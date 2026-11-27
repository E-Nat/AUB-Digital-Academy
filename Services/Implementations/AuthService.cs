using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AUBDigitalAcademy.Services.Implementations;

public class AuthService : IAuthService
{
    private const string TwoFactorPurpose = "AUBDigitalAcademy.ApiTelegram2FA.v1";
    private const string PasswordResetCodePrefix = "RST:";
    private const string GoogleLinkCodePrefix = "GLK:";
    private const string GoogleUnlinkCodePrefix = "GUL:";
    private const int OtpValidityMinutes = 10;
    private const int DefaultJwtExpirationMinutes = 60;
    private const int MinimumJwtSecretBytes = 32;
    private const int PasswordResetIpLimitPerWindow = 10;
    private static readonly TimeSpan PasswordResetIpWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ExternalNonceLifetime = TimeSpan.FromMinutes(30);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> ConsumedExternalNonces = new();
    private static readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> PasswordResetIpWindows = new();

    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ITelegramService _telegramService;
    private readonly ITimeLimitedDataProtector _twoFactorProtector;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<User> userManager,
        ApplicationDbContext context,
        ITelegramService telegramService,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _context = context;
        _telegramService = telegramService;
        _twoFactorProtector = dataProtectionProvider
            .CreateProtector(TwoFactorPurpose)
            .ToTimeLimitedDataProtector();
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<User?> FindByLoginIdAsync(string loginId)
    {
        if (string.IsNullOrWhiteSpace(loginId))
        {
            return null;
        }

        var trimmed = loginId.Trim();
        return await _userManager.FindByEmailAsync(trimmed)
               ?? await _userManager.Users.FirstOrDefaultAsync(u => u.UniversityId == trimmed)
               ?? await _userManager.FindByNameAsync(trimmed);
    }

    public async Task<CredentialCheckResult> AuthenticateCredentialsAsync(string loginId, string password)
    {
        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrEmpty(password))
        {
            return new CredentialCheckResult(CredentialCheckStatus.InvalidCredentials);
        }

        var user = await FindByLoginIdAsync(loginId);
        if (user == null)
        {
            return new CredentialCheckResult(CredentialCheckStatus.InvalidCredentials);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return new CredentialCheckResult(CredentialCheckStatus.LockedOut, user);
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, password);
        if (!passwordValid)
        {
            await _userManager.AccessFailedAsync(user);
            if (await _userManager.IsLockedOutAsync(user))
            {
                return new CredentialCheckResult(CredentialCheckStatus.LockedOut, user);
            }

            return new CredentialCheckResult(CredentialCheckStatus.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            return new CredentialCheckResult(CredentialCheckStatus.InactiveAccount, user);
        }

        if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue)
        {
            return new CredentialCheckResult(CredentialCheckStatus.TelegramNotLinked, user);
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return new CredentialCheckResult(CredentialCheckStatus.Success, user, roles);
    }

    public async Task<WebTwoFactorDispatchResult> EnsureWebVerificationCodeDispatchedAsync(User user)
    {
        if (!user.IsActive || await _userManager.IsLockedOutAsync(user) || !user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
        {
            return new WebTwoFactorDispatchResult(TwoFactorDispatchStatus.DeliveryFailed);
        }

        var now = DateTime.UtcNow;
        var activeRequest = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed && !r.Code.StartsWith(PasswordResetCodePrefix) && !r.Code.StartsWith(GoogleLinkCodePrefix) && !r.Code.StartsWith(GoogleUnlinkCodePrefix) && r.ExpiresAt > now && r.AttemptsCount < r.MaxAttempts)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (activeRequest != null)
        {
            return new WebTwoFactorDispatchResult(TwoFactorDispatchStatus.Sent, activeRequest);
        }

        return await CreateAndSendWebOtpAsync(user);
    }

    public async Task<WebTwoFactorDispatchResult> ResendWebVerificationCodeAsync(User user, int cooldownSeconds = 60)
    {
        if (!user.IsActive || await _userManager.IsLockedOutAsync(user) || !user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
        {
            return new WebTwoFactorDispatchResult(TwoFactorDispatchStatus.DeliveryFailed);
        }

        var now = DateTime.UtcNow;
        var latestRequest = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.Code.StartsWith(PasswordResetCodePrefix) && !r.Code.StartsWith(GoogleLinkCodePrefix) && !r.Code.StartsWith(GoogleUnlinkCodePrefix))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestRequest != null && (now - latestRequest.CreatedAt).TotalSeconds < cooldownSeconds)
        {
            int waitSeconds = Math.Max(1, cooldownSeconds - (int)(now - latestRequest.CreatedAt).TotalSeconds);
            return new WebTwoFactorDispatchResult(TwoFactorDispatchStatus.RateLimited, RetryAfterSeconds: waitSeconds);
        }

        return await CreateAndSendWebOtpAsync(user);
    }

    public async Task<WebTwoFactorDispatchResult> DispatchWebTwoFactorChallengeAsync(User user)
    {
        if (!user.IsActive || await _userManager.IsLockedOutAsync(user) || !user.IsTelegramVerified || !user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
        {
            return new WebTwoFactorDispatchResult(TwoFactorDispatchStatus.DeliveryFailed);
        }

        return await CreateAndSendWebOtpAsync(user);
    }

    public async Task InvalidatePendingChallengesAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var now = DateTime.UtcNow;
        await _context.TelegramVerificationRequests
            .Where(r => r.UserId == userId && !r.IsUsed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.IsUsed, true)
                .SetProperty(r => r.UsedAt, now));

        await _context.TelegramLinkTokens
            .Where(t => t.UserId == userId && !t.IsUsed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.IsUsed, true)
                .SetProperty(t => t.UsedAt, now));
    }

    private async Task<WebTwoFactorDispatchResult> CreateAndSendWebOtpAsync(User user)
    {
        var now = DateTime.UtcNow;
        await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed && !r.Code.StartsWith(PasswordResetCodePrefix) && !r.Code.StartsWith(GoogleLinkCodePrefix) && !r.Code.StartsWith(GoogleUnlinkCodePrefix))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.IsUsed, true)
                .SetProperty(r => r.UsedAt, now));

        var code = GenerateSixDigitCode();
        var request = new TelegramVerificationRequest
        {
            UserId = user.Id,
            UniversityId = user.UniversityId ?? user.Email ?? user.UserName ?? user.Id,
            Code = code,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(OtpValidityMinutes),
            IsUsed = false,
            AttemptsCount = 0,
            MaxAttempts = 5
        };

        _context.TelegramVerificationRequests.Add(request);
        await _context.SaveChangesAsync();

        var sent = await _telegramService.SendVerificationCodeAsync(
            request.UniversityId,
            user.FullName,
            code,
            user.TelegramChatId!.Value.ToString());

        if (!sent)
        {
            request.IsUsed = true;
            request.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return new WebTwoFactorDispatchResult(TwoFactorDispatchStatus.DeliveryFailed);
        }

        return new WebTwoFactorDispatchResult(TwoFactorDispatchStatus.Sent, request);
    }

    public async Task<ExternalLoginCheckResult> InitiateExternalTwoFactorLoginAsync(ExternalLoginInfo? info)
    {
        if (info == null ||
            !string.Equals(info.LoginProvider, "Google", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(info.ProviderKey))
        {
            return new ExternalLoginCheckResult(ExternalLoginStatus.InvalidExternalLogin);
        }

        if (!TryConsumeExternalLoginNonce(info, "login"))
        {
            _logger.LogWarning("Rejected invalid, expired, or replayed external login attempt for provider {LoginProvider}.", info.LoginProvider);
            return new ExternalLoginCheckResult(ExternalLoginStatus.Replayed);
        }

        // Resolve local account strictly through trusted Identity external login association (AspNetUserLogins).
        // Never auto-create users or silently link by unverified email match.
        var user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user == null)
        {
            _logger.LogInformation("External login {LoginProvider} did not match any associated local user.", info.LoginProvider);
            return new ExternalLoginCheckResult(ExternalLoginStatus.NotAssociated);
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("External login blocked for inactive user {UserId}.", user.Id);
            return new ExternalLoginCheckResult(ExternalLoginStatus.InactiveAccount, user);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning("External login blocked for locked-out user {UserId}.", user.Id);
            return new ExternalLoginCheckResult(ExternalLoginStatus.LockedOut, user);
        }

        if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue)
        {
            _logger.LogInformation("External login blocked for user {UserId}: Telegram 2FA is not linked.", user.Id);
            return new ExternalLoginCheckResult(ExternalLoginStatus.TelegramNotLinked, user);
        }

        var dispatch = await DispatchWebTwoFactorChallengeAsync(user);
        if (dispatch.Status != TwoFactorDispatchStatus.Sent || dispatch.Request == null)
        {
            _logger.LogWarning("External login OTP delivery failed for user {UserId}.", user.Id);
            return new ExternalLoginCheckResult(ExternalLoginStatus.OtpDeliveryFailed, user);
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return new ExternalLoginCheckResult(
            ExternalLoginStatus.TwoFactorPending,
            user,
            roles,
            dispatch.Request);
    }

    /// <summary>
    /// Validates and atomically consumes a single-use external OAuth nonce bound to its intended purpose
    /// ("login" or "link"). Rejects missing, malformed, expired, cross-purpose, mismatched, or replayed nonces.
    /// </summary>
    public bool TryConsumeExternalLoginNonce(ExternalLoginInfo? info, string expectedPurpose, string? expectedNonce = null)
    {
        if (info == null || string.IsNullOrWhiteSpace(info.LoginProvider) || string.IsNullOrWhiteSpace(expectedPurpose))
        {
            return false;
        }

        var normalizedPurpose = expectedPurpose.Trim().ToLowerInvariant();
        if (normalizedPurpose != "login" && normalizedPurpose != "link")
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ConsumedExternalNonces)
        {
            if (entry.Value <= now)
            {
                ConsumedExternalNonces.TryRemove(entry.Key, out _);
            }
        }

        if (info.AuthenticationProperties?.Items == null ||
            !info.AuthenticationProperties.Items.TryGetValue("AubExternalNonce", out var nonce) ||
            string.IsNullOrWhiteSpace(nonce))
        {
            return false;
        }

        nonce = nonce.Trim();

        if (info.AuthenticationProperties.ExpiresUtc is { } explicitExpiry && explicitExpiry <= now)
        {
            return false;
        }

        if (!IsValidExternalNonceFormat(nonce, normalizedPurpose))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedNonce))
        {
            var providedBytes = Encoding.UTF8.GetBytes(nonce);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedNonce.Trim());
            if (providedBytes.Length != expectedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
            {
                return false;
            }
        }

        var expiresAt = info.AuthenticationProperties.ExpiresUtc ?? now.Add(ExternalNonceLifetime);
        var rawReplayKey = $"raw:{info.LoginProvider}:{nonce}";
        return ConsumedExternalNonces.TryAdd(rawReplayKey, expiresAt);
    }

    private static bool IsValidExternalNonceFormat(string nonce, string expectedPurpose)
    {
        const string linkPrefix = "link:";
        const string loginPrefix = "login:";

        if (expectedPurpose == "link")
        {
            if (!nonce.StartsWith(linkPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var suffix = nonce.Substring(linkPrefix.Length);
            return suffix.Length >= 16 && suffix.All(char.IsAsciiLetterOrDigit);
        }

        // Purpose == "login": reject link/reset/step-up prefixes; allow "login:<token>" or legacy 16+ alnum nonces
        if (nonce.StartsWith(linkPrefix, StringComparison.Ordinal) ||
            nonce.StartsWith("rst:", StringComparison.OrdinalIgnoreCase) ||
            nonce.StartsWith("glk:", StringComparison.OrdinalIgnoreCase) ||
            nonce.StartsWith("gul:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var loginToken = nonce.StartsWith(loginPrefix, StringComparison.Ordinal)
            ? nonce.Substring(loginPrefix.Length)
            : nonce;

        return loginToken.Length >= 16 && loginToken.All(char.IsAsciiLetterOrDigit);
    }

    public async Task<ApiTwoFactorChallengeResult> CreateApiTwoFactorChallengeAsync(User user)
    {
        if (!HasValidJwtConfiguration())
        {
            return new ApiTwoFactorChallengeResult(TwoFactorDispatchStatus.InvalidConfiguration);
        }

        if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue)
        {
            return new ApiTwoFactorChallengeResult(TwoFactorDispatchStatus.DeliveryFailed);
        }

        var now = DateTime.UtcNow;
        await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed && !r.Code.StartsWith(PasswordResetCodePrefix) && !r.Code.StartsWith(GoogleLinkCodePrefix) && !r.Code.StartsWith(GoogleUnlinkCodePrefix))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.IsUsed, true)
                .SetProperty(r => r.UsedAt, now));

        var code = GenerateSixDigitCode();
        var request = new TelegramVerificationRequest
        {
            UserId = user.Id,
            UniversityId = user.UniversityId ?? user.Email ?? user.UserName ?? user.Id,
            Code = code,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(OtpValidityMinutes),
            IsUsed = false,
            AttemptsCount = 0,
            MaxAttempts = 5
        };

        _context.TelegramVerificationRequests.Add(request);
        await _context.SaveChangesAsync();

        var sent = await _telegramService.SendVerificationCodeAsync(
            request.UniversityId,
            user.FullName,
            code,
            user.TelegramChatId.Value.ToString());

        if (!sent)
        {
            request.IsUsed = true;
            request.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return new ApiTwoFactorChallengeResult(TwoFactorDispatchStatus.DeliveryFailed);
        }

        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var payload = $"{request.Id}|{user.Id}|{nonce}";
        var verificationId = _twoFactorProtector.Protect(payload, TimeSpan.FromMinutes(OtpValidityMinutes));

        return new ApiTwoFactorChallengeResult(
            TwoFactorDispatchStatus.Sent,
            verificationId,
            request.ExpiresAt,
            OtpValidityMinutes * 60);
    }

    public async Task<TwoFactorVerifyResult> VerifyApiTwoFactorAsync(string verificationId, string code)
    {
        if (string.IsNullOrWhiteSpace(verificationId) || string.IsNullOrWhiteSpace(code))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.InvalidToken);
        }

        string payload;
        try
        {
            payload = _twoFactorProtector.Unprotect(verificationId.Trim(), out _);
        }
        catch (CryptographicException ex)
        {
            if (ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
            {
                return new TwoFactorVerifyResult(TwoFactorVerifyStatus.Expired);
            }

            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.InvalidToken);
        }

        var parts = payload.Split('|');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var requestId) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.InvalidToken);
        }

        var expectedUserId = parts[1];
        var request = await _context.TelegramVerificationRequests
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null ||
            !string.Equals(request.UserId, expectedUserId, StringComparison.Ordinal) ||
            request.Code.StartsWith(PasswordResetCodePrefix, StringComparison.Ordinal) ||
            request.Code.StartsWith(GoogleLinkCodePrefix, StringComparison.Ordinal) ||
            request.Code.StartsWith(GoogleUnlinkCodePrefix, StringComparison.Ordinal))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.InvalidToken);
        }

        var user = await _userManager.FindByIdAsync(expectedUserId);
        if (user == null || !user.IsActive)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.UserUnavailable);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.LockedOut, user);
        }

        if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.TelegramNotLinked, user);
        }

        return await ValidateAndConsumeOtpRequestAsync(request, user, code);
    }

    public async Task<TwoFactorVerifyResult> VerifyWebTwoFactorAsync(string userId, string code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.InvalidToken);
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.UserUnavailable);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.LockedOut, user);
        }

        if (!user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.TelegramNotLinked, user);
        }

        var request = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed && !r.Code.StartsWith(PasswordResetCodePrefix) && !r.Code.StartsWith(GoogleLinkCodePrefix) && !r.Code.StartsWith(GoogleUnlinkCodePrefix))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (request == null)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.AlreadyUsed, user);
        }

        return await ValidateAndConsumeOtpRequestAsync(request, user, code);
    }

    public async Task<PasswordResetInitResult> InitiatePasswordResetAsync(string loginId, string? clientIp = null)
    {
        if (!TryConsumePasswordResetIpQuota(clientIp))
        {
            return new PasswordResetInitResult(PasswordResetInitStatus.RateLimited);
        }

        if (string.IsNullOrWhiteSpace(loginId))
        {
            return new PasswordResetInitResult(PasswordResetInitStatus.Accepted, IsEligible: false);
        }

        var user = await FindByLoginIdAsync(loginId.Trim());
        if (user == null ||
            !user.IsActive ||
            await _userManager.IsLockedOutAsync(user) ||
            !user.IsTelegramVerified ||
            !user.TelegramChatId.HasValue ||
            user.TelegramChatId.Value <= 0)
        {
            return new PasswordResetInitResult(PasswordResetInitStatus.Accepted, IsEligible: false);
        }

        var now = DateTime.UtcNow;
        var latestResetReq = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && r.Code.StartsWith(PasswordResetCodePrefix))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        // Enforce 60-second per-account cooldown across all login identifiers (Email, UniversityId, UserName)
        if (latestResetReq != null && (now - latestResetReq.CreatedAt).TotalSeconds < 60)
        {
            if (!latestResetReq.IsUsed &&
                latestResetReq.ExpiresAt > now &&
                latestResetReq.AttemptsCount < latestResetReq.MaxAttempts)
            {
                return new PasswordResetInitResult(
                    PasswordResetInitStatus.Accepted,
                    IsEligible: true,
                    UserId: user.Id,
                    RequestId: latestResetReq.Id,
                    CreatedAtUtc: latestResetReq.CreatedAt);
            }

            return new PasswordResetInitResult(PasswordResetInitStatus.Accepted, IsEligible: false);
        }

        return await CreateAndSendPasswordResetOtpAsync(user);
    }

    public async Task<PasswordResetInitResult> ResendPasswordResetCodeAsync(
        string userId,
        bool isEligible,
        DateTimeOffset sessionIssuedAtUtc,
        string? clientIp = null,
        int cooldownSeconds = 60)
    {
        if (!TryConsumePasswordResetIpQuota(clientIp))
        {
            return new PasswordResetInitResult(PasswordResetInitStatus.RateLimited);
        }

        if ((DateTimeOffset.UtcNow - sessionIssuedAtUtc).TotalSeconds < cooldownSeconds)
        {
            return new PasswordResetInitResult(PasswordResetInitStatus.RateLimited);
        }

        if (!isEligible || string.IsNullOrWhiteSpace(userId))
        {
            return new PasswordResetInitResult(PasswordResetInitStatus.Accepted, IsEligible: false);
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null ||
            !user.IsActive ||
            await _userManager.IsLockedOutAsync(user) ||
            !user.IsTelegramVerified ||
            !user.TelegramChatId.HasValue ||
            user.TelegramChatId.Value <= 0)
        {
            return new PasswordResetInitResult(PasswordResetInitStatus.Accepted, IsEligible: false);
        }

        var now = DateTime.UtcNow;
        var latestResetReq = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && r.Code.StartsWith(PasswordResetCodePrefix))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestResetReq != null && (now - latestResetReq.CreatedAt).TotalSeconds < cooldownSeconds)
        {
            return new PasswordResetInitResult(PasswordResetInitStatus.RateLimited);
        }

        return await CreateAndSendPasswordResetOtpAsync(user);
    }

    public async Task<PasswordResetCompleteResult> CompletePasswordResetAsync(
        string userId,
        int requestId,
        bool isEligible,
        string code,
        string newPassword)
    {
        if (!isEligible ||
            requestId <= 0 ||
            string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrEmpty(newPassword))
        {
            return new PasswordResetCompleteResult(PasswordResetCompleteStatus.InvalidOrExpiredCode);
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null ||
            !user.IsActive ||
            await _userManager.IsLockedOutAsync(user) ||
            !user.IsTelegramVerified ||
            !user.TelegramChatId.HasValue ||
            user.TelegramChatId.Value <= 0)
        {
            return new PasswordResetCompleteResult(PasswordResetCompleteStatus.InvalidOrExpiredCode);
        }

        var request = await _context.TelegramVerificationRequests
            .FirstOrDefaultAsync(r => r.Id == requestId && r.UserId == user.Id);

        if (request == null ||
            !request.Code.StartsWith(PasswordResetCodePrefix, StringComparison.Ordinal) ||
            request.IsUsed)
        {
            return new PasswordResetCompleteResult(PasswordResetCompleteStatus.InvalidOrExpiredCode);
        }

        var now = DateTime.UtcNow;
        if (now > request.ExpiresAt)
        {
            await _context.TelegramVerificationRequests
                .Where(r => r.Id == request.Id && !r.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.IsUsed, true)
                    .SetProperty(r => r.UsedAt, now));
            return new PasswordResetCompleteResult(PasswordResetCompleteStatus.InvalidOrExpiredCode);
        }

        if (request.AttemptsCount >= request.MaxAttempts)
        {
            await _context.TelegramVerificationRequests
                .Where(r => r.Id == request.Id && !r.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.IsUsed, true)
                    .SetProperty(r => r.UsedAt, now));
            return new PasswordResetCompleteResult(PasswordResetCompleteStatus.TooManyAttempts);
        }

        var candidatePrefixed = $"{PasswordResetCodePrefix}{code.Trim()}";
        var inputHash = SHA256.HashData(Encoding.UTF8.GetBytes(candidatePrefixed));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Code));

        if (!CryptographicOperations.FixedTimeEquals(inputHash, expectedHash))
        {
            var nextAttempts = request.AttemptsCount + 1;
            var exhausted = nextAttempts >= request.MaxAttempts;
            await _context.TelegramVerificationRequests
                .Where(r => r.Id == request.Id && !r.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.AttemptsCount, nextAttempts)
                    .SetProperty(r => r.IsUsed, exhausted)
                    .SetProperty(r => r.UsedAt, exhausted ? (DateTime?)now : null));

            return new PasswordResetCompleteResult(
                exhausted ? PasswordResetCompleteStatus.TooManyAttempts : PasswordResetCompleteStatus.InvalidOrExpiredCode);
        }

        // Validate new password against Identity password validators before consuming the OTP
        var policyErrors = new List<string>();
        foreach (var validator in _userManager.PasswordValidators)
        {
            var valRes = await validator.ValidateAsync(_userManager, user, newPassword);
            if (!valRes.Succeeded)
            {
                policyErrors.AddRange(valRes.Errors.Select(e => e.Description));
            }
        }

        if (policyErrors.Count > 0)
        {
            return new PasswordResetCompleteResult(PasswordResetCompleteStatus.PasswordPolicyFailed, policyErrors);
        }

        // Atomically consume the single-use OTP challenge before performing the Identity reset
        var consumedRows = await _context.TelegramVerificationRequests
            .Where(r => r.Id == request.Id && !r.IsUsed && r.ExpiresAt > now && r.AttemptsCount < r.MaxAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.IsUsed, true)
                .SetProperty(r => r.UsedAt, now));

        if (consumedRows != 1)
        {
            return new PasswordResetCompleteResult(PasswordResetCompleteStatus.InvalidOrExpiredCode);
        }

        // Generate fresh Identity reset token strictly server-side after OTP verification
        var identityToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, identityToken, newPassword);
        if (!resetResult.Succeeded)
        {
            return new PasswordResetCompleteResult(
                PasswordResetCompleteStatus.PasswordPolicyFailed,
                resetResult.Errors.Select(e => e.Description).ToList());
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        await _userManager.ResetAccessFailedCountAsync(user);

        // Invalidate all remaining pending OTPs and Telegram link tokens for this user
        await InvalidatePendingChallengesAsync(user.Id);

        _logger.LogInformation("Password reset completed for user {UserId}; SecurityStamp rotated and pending challenges invalidated.", user.Id);
        return new PasswordResetCompleteResult(PasswordResetCompleteStatus.Succeeded);
    }

    private async Task<PasswordResetInitResult> CreateAndSendPasswordResetOtpAsync(User user)
    {
        var now = DateTime.UtcNow;
        await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed && r.Code.StartsWith(PasswordResetCodePrefix))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.IsUsed, true)
                .SetProperty(r => r.UsedAt, now));

        var code = GenerateSixDigitCode();
        var request = new TelegramVerificationRequest
        {
            UserId = user.Id,
            UniversityId = user.UniversityId ?? user.Email ?? user.UserName ?? user.Id,
            Code = $"{PasswordResetCodePrefix}{code}",
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(OtpValidityMinutes),
            IsUsed = false,
            AttemptsCount = 0,
            MaxAttempts = 5
        };

        _context.TelegramVerificationRequests.Add(request);
        await _context.SaveChangesAsync();

        var sent = await _telegramService.SendPasswordResetCodeAsync(
            request.UniversityId,
            user.FullName,
            code,
            user.TelegramChatId!.Value.ToString());

        if (!sent)
        {
            request.IsUsed = true;
            request.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return new PasswordResetInitResult(PasswordResetInitStatus.DeliveryFailed, IsEligible: false);
        }

        return new PasswordResetInitResult(
            PasswordResetInitStatus.Accepted,
            IsEligible: true,
            UserId: user.Id,
            RequestId: request.Id,
            CreatedAtUtc: request.CreatedAt);
    }

    private static bool TryConsumePasswordResetIpQuota(string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in PasswordResetIpWindows)
        {
            if (now - entry.Value.WindowStart >= PasswordResetIpWindow)
            {
                PasswordResetIpWindows.TryRemove(entry.Key, out _);
            }
        }

        var key = clientIp.Trim();
        var updated = PasswordResetIpWindows.AddOrUpdate(
            key,
            _ => (1, now),
            (_, existing) =>
            {
                if (now - existing.WindowStart >= PasswordResetIpWindow)
                {
                    return (1, now);
                }
                return (existing.Count + 1, existing.WindowStart);
            });

        return updated.Count <= PasswordResetIpLimitPerWindow;
    }

    public bool HasValidJwtConfiguration()
    {
        return GetJwtSigningKeyBytes() != null;
    }

    public JwtTokenResult? GenerateJwtToken(User user, string role)
    {
        var keyBytes = GetJwtSigningKeyBytes();
        if (keyBytes == null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var expirationMinutes = GetJwtExpirationMinutes();
        var expiresAt = now.AddMinutes(expirationMinutes);

        var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));

        var payloadObj = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["sstamp"] = ComputeSecurityStampDigest(user.SecurityStamp),
            ["email"] = user.Email ?? string.Empty,
            ["role"] = role.ToUpperInvariant(),
            ["name"] = user.FullName,
            ["university_id"] = user.UniversityId ?? string.Empty,
            ["iss"] = GetJwtIssuer(),
            ["aud"] = GetJwtAudience(),
            ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            ["nbf"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            ["exp"] = new DateTimeOffset(expiresAt).ToUnixTimeSeconds()
        };

        var payloadJson = JsonSerializer.Serialize(payloadObj);
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signingInput = $"{header}.{payload}";
        using var hmac = new HMACSHA256(keyBytes);
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        var signature = Base64UrlEncode(signatureBytes);

        return new JwtTokenResult($"{signingInput}.{signature}", expiresAt);
    }

    public Dictionary<string, string>? ValidateJwtToken(string token)
    {
        var keyBytes = GetJwtSigningKeyBytes();
        if (keyBytes == null || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            using var headerDoc = JsonDocument.Parse(headerJson);
            if (!headerDoc.RootElement.TryGetProperty("alg", out var algProp) ||
                !string.Equals(algProp.GetString(), "HS256", StringComparison.Ordinal))
            {
                return null;
            }

            var signingInput = $"{parts[0]}.{parts[1]}";
            using var hmac = new HMACSHA256(keyBytes);
            var expectedSig = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
            var providedSig = Base64UrlDecode(parts[2]);

            if (providedSig.Length != expectedSig.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedSig, providedSig))
            {
                return null;
            }

            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            const long clockSkewSeconds = 30;

            if (!root.TryGetProperty("iss", out var issProp) ||
                !string.Equals(issProp.GetString(), GetJwtIssuer(), StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("aud", out var audProp) ||
                !string.Equals(audProp.GetString(), GetJwtAudience(), StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("exp", out var expProp) ||
                expProp.GetInt64() <= nowUnix - clockSkewSeconds)
            {
                return null;
            }

            if (root.TryGetProperty("nbf", out var nbfProp) &&
                nbfProp.GetInt64() > nowUnix + clockSkewSeconds)
            {
                return null;
            }

            if (!root.TryGetProperty("sub", out var subProp) ||
                string.IsNullOrWhiteSpace(subProp.GetString()))
            {
                return null;
            }

            if (!root.TryGetProperty("sstamp", out var stampProp) ||
                string.IsNullOrWhiteSpace(stampProp.GetString()))
            {
                return null;
            }

            var claims = new Dictionary<string, string>();
            foreach (var prop in root.EnumerateObject())
            {
                claims[prop.Name] = prop.Value.ToString();
            }

            return claims;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(User User, IReadOnlyList<string> Roles)?> ValidateApiBearerTokenAsync(string token)
    {
        var claims = ValidateJwtToken(token);
        if (claims == null ||
            !claims.TryGetValue("sub", out var userId) ||
            !claims.TryGetValue("sstamp", out var tokenStampDigest) ||
            string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(tokenStampDigest))
        {
            return null;
        }

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || !user.IsActive || (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow))
        {
            return null;
        }

        var expectedDigest = ComputeSecurityStampDigest(user.SecurityStamp);
        var providedBytes = Encoding.UTF8.GetBytes(tokenStampDigest);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedDigest);
        if (providedBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            return null;
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return (user, roles);
    }

    private static string ComputeSecurityStampDigest(string? securityStamp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"AUB.SecurityStamp.v1:{securityStamp ?? string.Empty}"));
        return Convert.ToHexString(bytes);
    }

    private async Task<TwoFactorVerifyResult> ValidateAndConsumeOtpRequestAsync(
        TelegramVerificationRequest request,
        User user,
        string code)
    {
        if (request.IsUsed)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.AlreadyUsed, user);
        }

        if (DateTime.UtcNow > request.ExpiresAt)
        {
            request.IsUsed = true;
            request.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.Expired, user);
        }

        if (request.AttemptsCount >= request.MaxAttempts)
        {
            request.IsUsed = true;
            request.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.TooManyAttempts, user);
        }

        var normalizedInput = code.Trim();
        var inputHash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedInput));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Code));

        if (!CryptographicOperations.FixedTimeEquals(inputHash, expectedHash))
        {
            request.AttemptsCount++;
            if (request.AttemptsCount >= request.MaxAttempts)
            {
                request.IsUsed = true;
                request.UsedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return new TwoFactorVerifyResult(TwoFactorVerifyStatus.TooManyAttempts, user);
            }

            await _context.SaveChangesAsync();
            return new TwoFactorVerifyResult(
                TwoFactorVerifyStatus.InvalidCode,
                user,
                RemainingAttempts: request.MaxAttempts - request.AttemptsCount);
        }

        var now = DateTime.UtcNow;
        var consumedRows = await _context.TelegramVerificationRequests
            .Where(r => r.Id == request.Id && !r.IsUsed && r.ExpiresAt > now && r.AttemptsCount < r.MaxAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.IsUsed, true)
                .SetProperty(r => r.UsedAt, now));

        if (consumedRows != 1)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.AlreadyUsed, user);
        }

        request.IsUsed = true;
        request.UsedAt = now;

        await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed && !r.Code.StartsWith(PasswordResetCodePrefix) && !r.Code.StartsWith(GoogleLinkCodePrefix) && !r.Code.StartsWith(GoogleUnlinkCodePrefix) && r.Id != request.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.IsUsed, true)
                .SetProperty(r => r.UsedAt, now));

        await _userManager.ResetAccessFailedCountAsync(user);

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return new TwoFactorVerifyResult(TwoFactorVerifyStatus.Verified, user, roles);
    }

    private byte[]? GetJwtSigningKeyBytes()
    {
        var secret = _configuration["Jwt:Secret"]
            ?? _configuration["Jwt:Key"]
            ?? _configuration["JWT_SECRET"];

        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("JWT signing key is not configured.");
            return null;
        }

        var keyBytes = Encoding.UTF8.GetBytes(secret.Trim());
        if (keyBytes.Length < MinimumJwtSecretBytes)
        {
            _logger.LogWarning("Configured JWT signing key does not meet minimum length requirements.");
            return null;
        }

        return keyBytes;
    }

    private string GetJwtIssuer()
    {
        var issuer = _configuration["Jwt:Issuer"];
        return string.IsNullOrWhiteSpace(issuer) ? "AUBDigitalAcademy" : issuer.Trim();
    }

    private string GetJwtAudience()
    {
        var audience = _configuration["Jwt:Audience"];
        return string.IsNullOrWhiteSpace(audience) ? "AUBDigitalAcademy.Api" : audience.Trim();
    }

    private int GetJwtExpirationMinutes()
    {
        if (int.TryParse(_configuration["Jwt:ExpirationMinutes"], out var minutes) && minutes > 0)
        {
            return minutes;
        }

        return DefaultJwtExpirationMinutes;
    }

    private static string GenerateSixDigitCode()
    {
        return RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        return Convert.FromBase64String(base64);
    }
}
