using AUBDigitalAcademy.Models;

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
    DeliveryFailed
}

public sealed record ApiTwoFactorChallengeResult(
    TwoFactorDispatchStatus Status,
    string? VerificationId = null,
    DateTime? ExpiresAt = null,
    int ExpiresInSeconds = 0);

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
    Task<TelegramVerificationRequest> EnsureWebVerificationCodeDispatchedAsync(User user);
    Task<ApiTwoFactorChallengeResult> CreateApiTwoFactorChallengeAsync(User user);
    Task<TwoFactorVerifyResult> VerifyApiTwoFactorAsync(string verificationId, string code);
    Task<TwoFactorVerifyResult> VerifyWebTwoFactorAsync(string userId, string code);
    bool HasValidJwtConfiguration();
    JwtTokenResult? GenerateJwtToken(User user, string role);
    Dictionary<string, string>? ValidateJwtToken(string token);
}
