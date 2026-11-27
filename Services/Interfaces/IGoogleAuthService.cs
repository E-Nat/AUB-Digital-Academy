using Microsoft.AspNetCore.Identity;

namespace AUBDigitalAcademy.Services.Interfaces;

public enum GoogleSecurityAction
{
    Link = 1,
    Unlink = 2
}

public enum GoogleStepUpDispatchStatus
{
    Sent,
    CooldownActive,
    UserUnavailable,
    LockedOut,
    TelegramNotVerified,
    AlreadyLinked,
    NotLinked,
    LocalPasswordRequired,
    RateLimited,
    DeliveryFailed
}

public enum GoogleStepUpVerifyStatus
{
    Verified,
    UserUnavailable,
    LockedOut,
    TelegramNotVerified,
    PreconditionFailed,
    NoActiveChallenge,
    Expired,
    LockedTooManyAttempts,
    InvalidCode,
    AlreadyUsed
}

public enum GoogleLinkStatus
{
    Linked,
    InvalidNonce,
    InvalidUser,
    LockedOut,
    TelegramNotVerified,
    AlreadyHasGoogle,
    GoogleAlreadyLinkedToAnotherUser,
    UserUnavailable,
    PersistenceError
}

public enum GoogleUnlinkStatus
{
    Unlinked,
    UserUnavailable,
    LockedOut,
    TelegramNotVerified,
    NotLinked,
    LocalPasswordRequired,
    NoActiveChallenge,
    Expired,
    LockedTooManyAttempts,
    InvalidCode,
    AlreadyUsed
}

public sealed record GoogleSecurityStatusResult(
    bool UserFound,
    bool IsActive,
    bool IsLockedOut,
    string FullName,
    string? UniversityId,
    string? Email,
    string? RoleSummary,
    bool IsTelegramVerified,
    bool HasLinkedGoogle,
    bool HasUsableLocalPassword);

public sealed record GoogleStepUpDispatchResult(
    GoogleStepUpDispatchStatus Status,
    int RetryAfterSeconds = 0);

public sealed record GoogleStepUpVerifyResult(
    GoogleStepUpVerifyStatus Status);

public sealed record GoogleLinkCompleteResult(
    GoogleLinkStatus Status);

public sealed record GoogleUnlinkResult(
    GoogleUnlinkStatus Status);

public interface IGoogleAuthService
{
    Task<GoogleSecurityStatusResult> GetSecurityStatusAsync(string userId);

    Task<GoogleStepUpDispatchResult> RequestStepUpOtpAsync(
        string userId,
        GoogleSecurityAction action,
        string? clientIp = null);

    Task<GoogleStepUpVerifyResult> VerifyStepUpOtpAsync(
        string userId,
        GoogleSecurityAction action,
        string code);

    Task<GoogleLinkCompleteResult> CompleteGoogleLinkAsync(
        string authenticatedUserId,
        ExternalLoginInfo? externalLoginInfo,
        string? expectedNonce);

    Task<GoogleUnlinkResult> CompleteGoogleUnlinkAsync(
        string authenticatedUserId,
        string verificationCode);
}
