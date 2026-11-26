using AUBDigitalAcademy.Models;
using Microsoft.AspNetCore.Identity;

namespace AUBDigitalAcademy.Services.Interfaces;

public enum CredentialCheckStatus
{
    Success,
    InvalidCredentials,
    LockedOut,
    InactiveAccount,
    TelegramNotLinked
}

public sealed record CredentialCheckResult(
    CredentialCheckStatus Status,
    User? User = null,
    IReadOnlyList<string>? Roles = null);

public enum TwoFactorDispatchStatus
{
    Sent,
    InvalidConfiguration,
    DeliveryFailed,
    RateLimited
}

public sealed record ApiTwoFactorChallengeResult(
    TwoFactorDispatchStatus Status,
    string? VerificationId = null,
    DateTime? ExpiresAt = null,
    int ExpiresInSeconds = 0);

public sealed record WebTwoFactorDispatchResult(
    TwoFactorDispatchStatus Status,
    TelegramVerificationRequest? Request = null,
    int? RetryAfterSeconds = null);

public enum ExternalLoginStatus
{
    TwoFactorPending,
    InvalidExternalLogin,
    Replayed,
    NotAssociated,
    InactiveAccount,
    LockedOut,
    TelegramNotLinked,
    OtpDeliveryFailed
}

public sealed record ExternalLoginCheckResult(
    ExternalLoginStatus Status,
    User? User = null,
    IReadOnlyList<string>? Roles = null,
    TelegramVerificationRequest? VerificationRequest = null);

public enum TwoFactorVerifyStatus
{
    Verified,
    InvalidToken,
    Expired,
    AlreadyUsed,
    TooManyAttempts,
    InvalidCode,
    UserUnavailable,
    LockedOut,
    TelegramNotLinked
}

public sealed record TwoFactorVerifyResult(
    TwoFactorVerifyStatus Status,
    User? User = null,
    IReadOnlyList<string>? Roles = null,
    int? RemainingAttempts = null);

public sealed record JwtTokenResult(
    string Token,
    DateTime ExpiresAt);

public interface IAuthService
{
    Task<User?> FindByLoginIdAsync(string loginId);
    Task<CredentialCheckResult> AuthenticateCredentialsAsync(string loginId, string password);
    Task<WebTwoFactorDispatchResult> EnsureWebVerificationCodeDispatchedAsync(User user);
    Task<WebTwoFactorDispatchResult> ResendWebVerificationCodeAsync(User user, int cooldownSeconds = 60);
    Task<WebTwoFactorDispatchResult> DispatchWebTwoFactorChallengeAsync(User user);
    Task InvalidatePendingChallengesAsync(string userId);
    Task<ExternalLoginCheckResult> InitiateExternalTwoFactorLoginAsync(ExternalLoginInfo? info);
    Task<ApiTwoFactorChallengeResult> CreateApiTwoFactorChallengeAsync(User user);
    Task<TwoFactorVerifyResult> VerifyApiTwoFactorAsync(string verificationId, string code);
    Task<TwoFactorVerifyResult> VerifyWebTwoFactorAsync(string userId, string code);
    bool HasValidJwtConfiguration();
    JwtTokenResult? GenerateJwtToken(User user, string role);
    Dictionary<string, string>? ValidateJwtToken(string token);
}
