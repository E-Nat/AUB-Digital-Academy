using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AUBDigitalAcademy.Services.Implementations;

public class GoogleAuthService : IGoogleAuthService
{
    private const string GoogleProviderName = "Google";
    private const string LinkCodePrefix = "GLK:";
    private const string UnlinkCodePrefix = "GUL:";
    private const int OtpValidityMinutes = 10;
    private const int ResendCooldownSeconds = 60;
    private const int StepUpIpLimitPerWindow = 10;
    private static readonly TimeSpan StepUpIpWindow = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> StepUpIpWindows = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLinkLocks = new();

    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ITelegramService _telegramService;
    private readonly IAuthService _authService;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        UserManager<User> userManager,
        ApplicationDbContext context,
        ITelegramService telegramService,
        IAuthService authService,
        ILogger<GoogleAuthService> logger)
    {
        _userManager = userManager;
        _context = context;
        _telegramService = telegramService;
        _authService = authService;
        _logger = logger;
    }

    public async Task<GoogleSecurityStatusResult> GetSecurityStatusAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new GoogleSecurityStatusResult(false, false, false, string.Empty, null, null, null, false, false, false);
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return new GoogleSecurityStatusResult(false, false, false, string.Empty, null, null, null, false, false, false);
        }

        var isLockedOut = await _userManager.IsLockedOutAsync(user);
        var logins = await _userManager.GetLoginsAsync(user);
        var hasLinkedGoogle = logins.Any(l => string.Equals(l.LoginProvider, GoogleProviderName, StringComparison.OrdinalIgnoreCase));
        var hasUsableLocalPassword = await HasUsableLocalPasswordAsync(user);
        var roles = await _userManager.GetRolesAsync(user);

        return new GoogleSecurityStatusResult(
            UserFound: true,
            IsActive: user.IsActive,
            IsLockedOut: isLockedOut,
            FullName: user.FullName,
            UniversityId: user.UniversityId,
            Email: user.Email,
            RoleSummary: roles.Count > 0 ? string.Join(", ", roles) : "User",
            IsTelegramVerified: user.IsTelegramVerified && user.TelegramChatId.HasValue && user.TelegramChatId.Value > 0,
            HasLinkedGoogle: hasLinkedGoogle,
            HasUsableLocalPassword: hasUsableLocalPassword);
    }

    public async Task<GoogleStepUpDispatchResult> RequestStepUpOtpAsync(
        string userId,
        GoogleSecurityAction action,
        string? clientIp = null)
    {
        if (!TryConsumeStepUpIpQuota(clientIp))
        {
            return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.RateLimited);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.UserUnavailable);
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.UserUnavailable);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.LockedOut);
        }

        if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
        {
            return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.TelegramNotVerified);
        }

        var logins = await _userManager.GetLoginsAsync(user);
        var hasLinkedGoogle = logins.Any(l => string.Equals(l.LoginProvider, GoogleProviderName, StringComparison.OrdinalIgnoreCase));

        if (action == GoogleSecurityAction.Link && hasLinkedGoogle)
        {
            return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.AlreadyLinked);
        }

        if (action == GoogleSecurityAction.Unlink)
        {
            if (!hasLinkedGoogle)
            {
                return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.NotLinked);
            }

            if (!await HasUsableLocalPasswordAsync(user))
            {
                return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.LocalPasswordRequired);
            }
        }

        var prefix = GetPrefix(action);
        var now = DateTime.UtcNow;

        var latestRequest = await _context.TelegramVerificationRequests
            .AsNoTracking()
            .Where(r => r.UserId == user.Id && r.Code.StartsWith(prefix))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestRequest != null && !latestRequest.IsUsed && latestRequest.ExpiresAt > now)
        {
            var elapsed = (now - latestRequest.CreatedAt).TotalSeconds;
            if (elapsed < ResendCooldownSeconds)
            {
                var remaining = Math.Max(1, (int)Math.Ceiling(ResendCooldownSeconds - elapsed));
                return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.CooldownActive, remaining);
            }
        }

        await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed && r.Code.StartsWith(prefix))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsUsed, true)
                .SetProperty(r => r.UsedAt, now));

        var rawCode = GenerateSixDigitCode();
        var request = new TelegramVerificationRequest
        {
            UserId = user.Id,
            UniversityId = user.UniversityId ?? user.Email ?? user.UserName ?? user.Id,
            Code = $"{prefix}{rawCode}",
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(OtpValidityMinutes),
            IsUsed = false,
            AttemptsCount = 0,
            MaxAttempts = 5
        };

        _context.TelegramVerificationRequests.Add(request);
        await _context.SaveChangesAsync();

        var operationLabel = action == GoogleSecurityAction.Link
            ? "Google Account Linking"
            : "Google Account Unlinking";
        var chatId = user.TelegramChatId.Value.ToString(CultureInfo.InvariantCulture);

        var sent = await _telegramService.SendGoogleSecurityOtpAsync(
            user.UniversityId ?? user.Email ?? "N/A",
            user.FullName,
            rawCode,
            operationLabel,
            chatId);

        if (!sent)
        {
            // Fail closed: never allow an undelivered OTP challenge to remain active.
            await _context.TelegramVerificationRequests
                .Where(r => r.Id == request.Id && !r.IsUsed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.IsUsed, true)
                    .SetProperty(r => r.UsedAt, DateTime.UtcNow));
            return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.DeliveryFailed);
        }

        return new GoogleStepUpDispatchResult(GoogleStepUpDispatchStatus.Sent);
    }

    public async Task<GoogleStepUpVerifyResult> VerifyStepUpOtpAsync(
        string userId,
        GoogleSecurityAction action,
        string code)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new GoogleStepUpVerifyResult(GoogleStepUpVerifyStatus.UserUnavailable);
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            return new GoogleStepUpVerifyResult(GoogleStepUpVerifyStatus.UserUnavailable);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return new GoogleStepUpVerifyResult(GoogleStepUpVerifyStatus.LockedOut);
        }

        if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
        {
            return new GoogleStepUpVerifyResult(GoogleStepUpVerifyStatus.TelegramNotVerified);
        }

        var logins = await _userManager.GetLoginsAsync(user);
        var hasLinkedGoogle = logins.Any(l => string.Equals(l.LoginProvider, GoogleProviderName, StringComparison.OrdinalIgnoreCase));

        if (action == GoogleSecurityAction.Link && hasLinkedGoogle)
        {
            return new GoogleStepUpVerifyResult(GoogleStepUpVerifyStatus.PreconditionFailed);
        }

        if (action == GoogleSecurityAction.Unlink && (!hasLinkedGoogle || !await HasUsableLocalPasswordAsync(user)))
        {
            return new GoogleStepUpVerifyResult(GoogleStepUpVerifyStatus.PreconditionFailed);
        }

        var status = await ValidateAndConsumeStepUpOtpAsync(user.Id, GetPrefix(action), code);
        return new GoogleStepUpVerifyResult(status);
    }

    public async Task<GoogleLinkCompleteResult> CompleteGoogleLinkAsync(
        string authenticatedUserId,
        ExternalLoginInfo? externalLoginInfo,
        string? expectedNonce)
    {
        if (string.IsNullOrWhiteSpace(authenticatedUserId))
        {
            return new GoogleLinkCompleteResult(GoogleLinkStatus.UserUnavailable);
        }

        if (externalLoginInfo == null ||
            !string.Equals(externalLoginInfo.LoginProvider, GoogleProviderName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(externalLoginInfo.ProviderKey))
        {
            return new GoogleLinkCompleteResult(GoogleLinkStatus.InvalidNonce);
        }

        if (string.IsNullOrWhiteSpace(expectedNonce) ||
            !_authService.TryConsumeExternalLoginNonce(externalLoginInfo, "link", expectedNonce))
        {
            _logger.LogWarning("Rejected Google account link due to missing, invalid, cross-purpose, expired, or replayed nonce.");
            return new GoogleLinkCompleteResult(GoogleLinkStatus.InvalidNonce);
        }

        var user = await _userManager.FindByIdAsync(authenticatedUserId);
        if (user == null || !user.IsActive)
        {
            return new GoogleLinkCompleteResult(GoogleLinkStatus.UserUnavailable);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return new GoogleLinkCompleteResult(GoogleLinkStatus.LockedOut);
        }

        if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
        {
            return new GoogleLinkCompleteResult(GoogleLinkStatus.TelegramNotVerified);
        }

        var userLock = UserLinkLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync();
        try
        {
            await using var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync()
                : null;

            try
            {
                // Never auto-link by email; enforce strict ProviderKey uniqueness and one Google login per user.
                var existingOwner = await _userManager.FindByLoginAsync(externalLoginInfo.LoginProvider, externalLoginInfo.ProviderKey);
                if (existingOwner != null)
                {
                    if (transaction != null)
                    {
                        await transaction.RollbackAsync();
                    }

                    return existingOwner.Id == user.Id
                        ? new GoogleLinkCompleteResult(GoogleLinkStatus.AlreadyHasGoogle)
                        : new GoogleLinkCompleteResult(GoogleLinkStatus.GoogleAlreadyLinkedToAnotherUser);
                }

                var existingLogins = await _userManager.GetLoginsAsync(user);
                if (existingLogins.Any(l => string.Equals(l.LoginProvider, GoogleProviderName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (transaction != null)
                    {
                        await transaction.RollbackAsync();
                    }

                    return new GoogleLinkCompleteResult(GoogleLinkStatus.AlreadyHasGoogle);
                }

                // Persist only provider & provider key — never store OAuth tokens or external email claims.
                var loginToAdd = new UserLoginInfo(GoogleProviderName, externalLoginInfo.ProviderKey, GoogleProviderName);
                var addResult = await _userManager.AddLoginAsync(user, loginToAdd);
                if (!addResult.Succeeded)
                {
                    if (transaction != null)
                    {
                        await transaction.RollbackAsync();
                    }

                    var raceOwner = await _userManager.FindByLoginAsync(GoogleProviderName, externalLoginInfo.ProviderKey);
                    if (raceOwner != null)
                    {
                        return raceOwner.Id == user.Id
                            ? new GoogleLinkCompleteResult(GoogleLinkStatus.AlreadyHasGoogle)
                            : new GoogleLinkCompleteResult(GoogleLinkStatus.GoogleAlreadyLinkedToAnotherUser);
                    }

                    _logger.LogWarning("Failed to persist Google external login for authenticated user.");
                    return new GoogleLinkCompleteResult(GoogleLinkStatus.PersistenceError);
                }

                await _userManager.UpdateSecurityStampAsync(user);
                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }

                await _authService.InvalidatePendingChallengesAsync(user.Id);
                _logger.LogInformation("Authenticated user completed Google account linking.");
                return new GoogleLinkCompleteResult(GoogleLinkStatus.Linked);
            }
            catch (Exception ex) when (ex is DbUpdateException or SqlException ||
                                       (ex is InvalidOperationException && ex.InnerException is DbUpdateException or SqlException))
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }

                var raceOwner = await _userManager.FindByLoginAsync(GoogleProviderName, externalLoginInfo.ProviderKey);
                if (raceOwner != null)
                {
                    return raceOwner.Id == user.Id
                        ? new GoogleLinkCompleteResult(GoogleLinkStatus.AlreadyHasGoogle)
                        : new GoogleLinkCompleteResult(GoogleLinkStatus.GoogleAlreadyLinkedToAnotherUser);
                }

                _logger.LogWarning("Concurrency or persistence conflict prevented Google account linking.");
                return new GoogleLinkCompleteResult(GoogleLinkStatus.PersistenceError);
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<GoogleUnlinkResult> CompleteGoogleUnlinkAsync(
        string authenticatedUserId,
        string verificationCode)
    {
        if (string.IsNullOrWhiteSpace(authenticatedUserId))
        {
            return new GoogleUnlinkResult(GoogleUnlinkStatus.UserUnavailable);
        }

        var userLock = UserLinkLocks.GetOrAdd(authenticatedUserId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync();
        try
        {
            var user = await _userManager.FindByIdAsync(authenticatedUserId);
            if (user == null || !user.IsActive)
            {
                return new GoogleUnlinkResult(GoogleUnlinkStatus.UserUnavailable);
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                return new GoogleUnlinkResult(GoogleUnlinkStatus.LockedOut);
            }

            if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
            {
                return new GoogleUnlinkResult(GoogleUnlinkStatus.TelegramNotVerified);
            }

            var logins = await _userManager.GetLoginsAsync(user);
            var googleLogins = logins
                .Where(l => string.Equals(l.LoginProvider, GoogleProviderName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (googleLogins.Count == 0)
            {
                return new GoogleUnlinkResult(GoogleUnlinkStatus.NotLinked);
            }

            if (!await HasUsableLocalPasswordAsync(user))
            {
                return new GoogleUnlinkResult(GoogleUnlinkStatus.LocalPasswordRequired);
            }

            var otpStatus = await ValidateAndConsumeStepUpOtpAsync(user.Id, UnlinkCodePrefix, verificationCode);
            if (otpStatus != GoogleStepUpVerifyStatus.Verified)
            {
                return new GoogleUnlinkResult(MapStepUpErrorToUnlinkStatus(otpStatus));
            }

            foreach (var googleLogin in googleLogins)
            {
                var removeResult = await _userManager.RemoveLoginAsync(user, googleLogin.LoginProvider, googleLogin.ProviderKey);
                if (!removeResult.Succeeded)
                {
                    _logger.LogWarning("Failed to remove Google login association for authenticated user.");
                    return new GoogleUnlinkResult(GoogleUnlinkStatus.NotLinked);
                }
            }

            await _userManager.UpdateSecurityStampAsync(user);
            await _authService.InvalidatePendingChallengesAsync(user.Id);
            _logger.LogInformation("Authenticated user unlinked Google account.");
            return new GoogleUnlinkResult(GoogleUnlinkStatus.Unlinked);
        }
        finally
        {
            userLock.Release();
        }
    }

    private async Task<bool> HasUsableLocalPasswordAsync(User user)
    {
        return await _userManager.HasPasswordAsync(user) && !string.IsNullOrWhiteSpace(user.PasswordHash);
    }

    private async Task<GoogleStepUpVerifyStatus> ValidateAndConsumeStepUpOtpAsync(
        string userId,
        string prefix,
        string code)
    {
        var normalizedCode = (code ?? string.Empty).Trim();
        var request = await _context.TelegramVerificationRequests
            .AsNoTracking()
            .Where(r => r.UserId == userId && !r.IsUsed && r.Code.StartsWith(prefix))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (request == null)
        {
            return GoogleStepUpVerifyStatus.NoActiveChallenge;
        }

        if (request.IsUsed)
        {
            return GoogleStepUpVerifyStatus.AlreadyUsed;
        }

        var now = DateTime.UtcNow;
        if (now > request.ExpiresAt)
        {
            await _context.TelegramVerificationRequests
                .Where(r => r.Id == request.Id && !r.IsUsed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.IsUsed, true)
                    .SetProperty(r => r.UsedAt, now));
            return GoogleStepUpVerifyStatus.Expired;
        }

        if (request.AttemptsCount >= request.MaxAttempts)
        {
            await _context.TelegramVerificationRequests
                .Where(r => r.Id == request.Id && !r.IsUsed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.IsUsed, true)
                    .SetProperty(r => r.UsedAt, now));
            return GoogleStepUpVerifyStatus.LockedTooManyAttempts;
        }

        var expectedRawCode = request.Code.StartsWith(prefix, StringComparison.Ordinal)
            ? request.Code.Substring(prefix.Length)
            : string.Empty;

        var providedBytes = Encoding.UTF8.GetBytes(normalizedCode);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedRawCode);
        var isMatch = expectedRawCode.Length == 6 &&
                      providedBytes.Length == expectedBytes.Length &&
                      CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);

        if (!isMatch)
        {
            var nextCount = request.AttemptsCount + 1;
            var isLocked = nextCount >= request.MaxAttempts;
            await _context.TelegramVerificationRequests
                .Where(r => r.Id == request.Id && !r.IsUsed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.AttemptsCount, nextCount)
                    .SetProperty(r => r.IsUsed, isLocked)
                    .SetProperty(r => r.UsedAt, isLocked ? now : (DateTime?)null));

            return isLocked
                ? GoogleStepUpVerifyStatus.LockedTooManyAttempts
                : GoogleStepUpVerifyStatus.InvalidCode;
        }

        var consumedRows = await _context.TelegramVerificationRequests
            .Where(r => r.Id == request.Id && !r.IsUsed && r.ExpiresAt > now && r.AttemptsCount < r.MaxAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.IsUsed, true)
                .SetProperty(r => r.UsedAt, now));

        if (consumedRows != 1)
        {
            return GoogleStepUpVerifyStatus.AlreadyUsed;
        }

        await _context.TelegramVerificationRequests
            .Where(r => r.UserId == userId && !r.IsUsed && r.Code.StartsWith(prefix) && r.Id != request.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.IsUsed, true)
                .SetProperty(r => r.UsedAt, now));

        return GoogleStepUpVerifyStatus.Verified;
    }

    private static GoogleUnlinkStatus MapStepUpErrorToUnlinkStatus(GoogleStepUpVerifyStatus status) => status switch
    {
        GoogleStepUpVerifyStatus.NoActiveChallenge => GoogleUnlinkStatus.NoActiveChallenge,
        GoogleStepUpVerifyStatus.Expired => GoogleUnlinkStatus.Expired,
        GoogleStepUpVerifyStatus.LockedTooManyAttempts => GoogleUnlinkStatus.LockedTooManyAttempts,
        GoogleStepUpVerifyStatus.InvalidCode => GoogleUnlinkStatus.InvalidCode,
        GoogleStepUpVerifyStatus.AlreadyUsed => GoogleUnlinkStatus.AlreadyUsed,
        _ => GoogleUnlinkStatus.InvalidCode
    };

    private static string GetPrefix(GoogleSecurityAction action) =>
        action == GoogleSecurityAction.Link ? LinkCodePrefix : UnlinkCodePrefix;

    private static string GenerateSixDigitCode()
    {
        return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static bool TryConsumeStepUpIpQuota(string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var entry = StepUpIpWindows.AddOrUpdate(
            clientIp,
            _ => (1, now),
            (_, current) =>
            {
                if (now - current.WindowStart >= StepUpIpWindow)
                {
                    return (1, now);
                }

                return (current.Count + 1, current.WindowStart);
            });

        return entry.Count <= StepUpIpLimitPerWindow;
    }
}
