using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AUBDigitalAcademy.Controllers;

[Authorize]
public class SettingsController : Controller
{
    private const string GoogleProviderName = "Google";
    private const string GoogleSecurityPendingCookieName = "AUB_GoogleSecurityPending";
    private const string GoogleSecurityPurpose = "AUBDigitalAcademy.GoogleSecurity.v1";
    private const string StageLinkOtp = "link-otp";
    private const string StageLinkOAuth = "link-oauth";
    private const string StageUnlinkOtp = "unlink-otp";
    private static readonly TimeSpan PendingStateLifetime = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> RevokedSecuritySessionNonces = new();

    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IGoogleAuthService _googleAuthService;
    private readonly ITimeLimitedDataProtector _pendingProtector;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IGoogleAuthService googleAuthService,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SettingsController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _googleAuthService = googleAuthService;
        _pendingProtector = dataProtectionProvider
            .CreateProtector(GoogleSecurityPurpose)
            .ToTimeLimitedDataProtector();
        _logger = logger;
    }

    [HttpGet("/Profile")]
    [HttpGet("/Settings")]
    [HttpGet("/Settings/Profile")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Profile()
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var status = await _googleAuthService.GetSecurityStatusAsync(userId);
        if (!status.UserFound || !status.IsActive || status.IsLockedOut)
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        var pending = ReadPendingSecurityState(userId);
        var pendingStage = pending?.Stage is StageLinkOtp or StageUnlinkOtp
            ? pending.Value.Stage
            : null;

        var isGoogleConfigured = await IsGoogleSchemeConfiguredAsync();

        var model = new ProfileSecurityViewModel
        {
            FullName = status.FullName,
            UniversityId = status.UniversityId,
            Email = status.Email,
            RoleSummary = status.RoleSummary ?? "User",
            IsTelegramVerified = status.IsTelegramVerified,
            HasLinkedGoogle = status.HasLinkedGoogle,
            HasUsableLocalPassword = status.HasUsableLocalPassword,
            IsGoogleConfigured = isGoogleConfigured,
            PendingActionStage = pendingStage,
            StatusMessage = TempData?["StatusMessage"] as string,
            ErrorMessage = TempData?["ErrorMessage"] as string
        };

        return View("~/Views/Settings/Profile.cshtml", model);
    }

    [HttpPost("/Settings/StartGoogleLink")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("TelegramStepUpPolicy")]
    public async Task<IActionResult> StartGoogleLink()
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        if (!await IsGoogleSchemeConfiguredAsync())
        {
            SetErrorMessage("Google Sign-In is not configured on this server.");
            return RedirectToAction(nameof(Profile));
        }

        var existingPending = ReadPendingSecurityState(userId);
        if (existingPending != null)
        {
            RevokeSecuritySessionNonce(existingPending.Value.SessionNonce);
            ClearPendingSecurityState();
        }

        var dispatch = await _googleAuthService.RequestStepUpOtpAsync(userId, GoogleSecurityAction.Link, GetClientIp());
        if (dispatch.Status != GoogleStepUpDispatchStatus.Sent)
        {
            SetErrorMessage(GetStepUpDispatchErrorMessage(dispatch));
            return RedirectToAction(nameof(Profile));
        }

        WritePendingSecurityState(StageLinkOtp, userId, externalNonce: "none");
        SetStatusMessage("A 6-digit Telegram verification code has been sent to confirm Google account linking.");
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost("/Settings/ResendGoogleStepUpCode")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("TelegramStepUpPolicy")]
    public async Task<IActionResult> ResendGoogleStepUpCode()
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var pending = ReadPendingSecurityState(userId);
        if (pending == null || (pending.Value.Stage != StageLinkOtp && pending.Value.Stage != StageUnlinkOtp))
        {
            SetErrorMessage("No active Telegram verification challenge found. Please start again.");
            return RedirectToAction(nameof(Profile));
        }

        var action = pending.Value.Stage == StageLinkOtp
            ? GoogleSecurityAction.Link
            : GoogleSecurityAction.Unlink;

        var dispatch = await _googleAuthService.RequestStepUpOtpAsync(userId, action, GetClientIp());
        if (dispatch.Status != GoogleStepUpDispatchStatus.Sent)
        {
            SetErrorMessage(GetStepUpDispatchErrorMessage(dispatch));
            return RedirectToAction(nameof(Profile));
        }

        RevokeSecuritySessionNonce(pending.Value.SessionNonce);
        WritePendingSecurityState(pending.Value.Stage, userId, externalNonce: "none");
        SetStatusMessage("A new 6-digit Telegram verification code has been sent.");
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost("/Settings/LinkGoogle")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("TelegramStepUpPolicy")]
    public async Task<IActionResult> LinkGoogle(string code)
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var pending = ReadPendingSecurityState(userId);
        if (!await IsGoogleSchemeConfiguredAsync())
        {
            if (pending != null)
            {
                RevokeSecuritySessionNonce(pending.Value.SessionNonce);
            }

            ClearPendingSecurityState();
            SetErrorMessage("Google Sign-In is not configured on this server.");
            return RedirectToAction(nameof(Profile));
        }

        if (pending == null || pending.Value.Stage != StageLinkOtp)
        {
            if (pending != null)
            {
                RevokeSecuritySessionNonce(pending.Value.SessionNonce);
            }

            ClearPendingSecurityState();
            SetErrorMessage("Please request a Telegram verification code before linking your Google account.");
            return RedirectToAction(nameof(Profile));
        }

        var verifyResult = await _googleAuthService.VerifyStepUpOtpAsync(userId, GoogleSecurityAction.Link, code ?? string.Empty);
        if (verifyResult.Status != GoogleStepUpVerifyStatus.Verified)
        {
            if (verifyResult.Status is GoogleStepUpVerifyStatus.Expired or GoogleStepUpVerifyStatus.LockedTooManyAttempts or GoogleStepUpVerifyStatus.NoActiveChallenge or GoogleStepUpVerifyStatus.PreconditionFailed)
            {
                RevokeSecuritySessionNonce(pending.Value.SessionNonce);
                ClearPendingSecurityState();
            }

            SetErrorMessage(GetStepUpVerifyErrorMessage(verifyResult.Status));
            return RedirectToAction(nameof(Profile));
        }

        // Rotate pending state from "link-otp" to "link-oauth" so OTP confirmation is strictly bound to one OAuth redirect.
        RevokeSecuritySessionNonce(pending.Value.SessionNonce);
        var linkNonce = $"link:{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";
        WritePendingSecurityState(StageLinkOAuth, userId, linkNonce);

        await SignOutExternalSchemeSafelyAsync();

        var redirectUrl = Url.Action(nameof(LinkGoogleCallback), "Settings");
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(GoogleProviderName, redirectUrl, userId);
        properties.Items["AubExternalNonce"] = linkNonce;
        return Challenge(properties, GoogleProviderName);
    }

    [HttpGet("/Settings/LinkGoogleCallback")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LinkGoogleCallback(string? remoteError = null)
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            await SignOutExternalSchemeSafelyAsync();
            ClearPendingSecurityState();
            return Challenge();
        }

        var pending = ReadPendingSecurityState(userId);
        try
        {
            if (!string.IsNullOrEmpty(remoteError))
            {
                _logger.LogWarning("Google account link callback received remote error/cancellation.");
                SetErrorMessage("Google account linking was cancelled or failed. Please try again.");
                return RedirectToAction(nameof(Profile));
            }

            if (pending == null || pending.Value.Stage != StageLinkOAuth || string.IsNullOrWhiteSpace(pending.Value.ExternalNonce))
            {
                SetErrorMessage("Google linking session expired or was not verified via Telegram OTP. Please try again.");
                return RedirectToAction(nameof(Profile));
            }

            // Strictly validate XsrfId against the currently authenticated user ID.
            var info = await _signInManager.GetExternalLoginInfoAsync(userId);
            if (info == null)
            {
                SetErrorMessage("Unable to verify Google account information for your session. Please try again.");
                return RedirectToAction(nameof(Profile));
            }

            var result = await _googleAuthService.CompleteGoogleLinkAsync(userId, info, pending.Value.ExternalNonce);
            if (result.Status != GoogleLinkStatus.Linked)
            {
                SetErrorMessage(GetGoogleLinkErrorMessage(result.Status));
                return RedirectToAction(nameof(Profile));
            }

            var updatedUser = await _userManager.FindByIdAsync(userId);
            if (updatedUser != null && updatedUser.IsActive)
            {
                // Keep the current browser session valid after SecurityStamp rotation while invalidating other sessions/JWTs.
                await _signInManager.RefreshSignInAsync(updatedUser);
            }

            SetStatusMessage("Your Google account has been securely linked. Google Sign-In will still require Telegram 2FA.");
            return RedirectToAction(nameof(Profile));
        }
        finally
        {
            if (pending != null)
            {
                RevokeSecuritySessionNonce(pending.Value.SessionNonce);
            }

            ClearPendingSecurityState();
            await SignOutExternalSchemeSafelyAsync();
        }
    }

    [HttpPost("/Settings/StartGoogleUnlink")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("TelegramStepUpPolicy")]
    public async Task<IActionResult> StartGoogleUnlink()
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var existingPending = ReadPendingSecurityState(userId);
        if (existingPending != null)
        {
            RevokeSecuritySessionNonce(existingPending.Value.SessionNonce);
            ClearPendingSecurityState();
        }

        var dispatch = await _googleAuthService.RequestStepUpOtpAsync(userId, GoogleSecurityAction.Unlink, GetClientIp());
        if (dispatch.Status != GoogleStepUpDispatchStatus.Sent)
        {
            SetErrorMessage(GetStepUpDispatchErrorMessage(dispatch));
            return RedirectToAction(nameof(Profile));
        }

        WritePendingSecurityState(StageUnlinkOtp, userId, externalNonce: "none");
        SetStatusMessage("A 6-digit Telegram verification code has been sent to confirm unlinking your Google account.");
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost("/Settings/UnlinkGoogle")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("TelegramStepUpPolicy")]
    public async Task<IActionResult> UnlinkGoogle(string code)
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var pending = ReadPendingSecurityState(userId);
        if (pending == null || pending.Value.Stage != StageUnlinkOtp)
        {
            ClearPendingSecurityState();
            SetErrorMessage("Please request a Telegram verification code before unlinking your Google account.");
            return RedirectToAction(nameof(Profile));
        }

        var result = await _googleAuthService.CompleteGoogleUnlinkAsync(userId, code ?? string.Empty);
        if (result.Status != GoogleUnlinkStatus.Unlinked)
        {
            if (result.Status is GoogleUnlinkStatus.Expired or GoogleUnlinkStatus.LockedTooManyAttempts or GoogleUnlinkStatus.NoActiveChallenge or GoogleUnlinkStatus.LocalPasswordRequired or GoogleUnlinkStatus.NotLinked)
            {
                RevokeSecuritySessionNonce(pending.Value.SessionNonce);
                ClearPendingSecurityState();
            }

            SetErrorMessage(GetGoogleUnlinkErrorMessage(result.Status));
            return RedirectToAction(nameof(Profile));
        }

        RevokeSecuritySessionNonce(pending.Value.SessionNonce);
        ClearPendingSecurityState();

        var updatedUser = await _userManager.FindByIdAsync(userId);
        if (updatedUser != null && updatedUser.IsActive)
        {
            await _signInManager.RefreshSignInAsync(updatedUser);
        }

        SetStatusMessage("Your Google account has been unlinked. You can sign in using your university ID/email and local password.");
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost("/Settings/CancelGoogleSecurityAction")]
    [ValidateAntiForgeryToken]
    public IActionResult CancelGoogleSecurityAction()
    {
        var userId = GetAuthenticatedUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            var pending = ReadPendingSecurityState(userId);
            if (pending != null)
            {
                RevokeSecuritySessionNonce(pending.Value.SessionNonce);
            }
        }

        ClearPendingSecurityState();
        SetStatusMessage("The pending Google security action was cancelled.");
        return RedirectToAction(nameof(Profile));
    }

    private string? GetAuthenticatedUserId()
    {
        return User == null
            ? null
            : User.FindFirstValue(ClaimTypes.NameIdentifier) ?? _userManager.GetUserId(User);
    }

    private async Task<bool> IsGoogleSchemeConfiguredAsync()
    {
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        return schemes.Any(s => string.Equals(s.Name, GoogleProviderName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SignOutExternalSchemeSafelyAsync()
    {
        if (HttpContext == null)
        {
            return;
        }

        try
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
        catch
        {
            // Ignore when no external auth handler is registered in isolated test runs.
        }
    }

    private void WritePendingSecurityState(string stage, string userId, string externalNonce)
    {
        if (HttpContext?.Response == null)
        {
            return;
        }

        var sessionNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var payload = $"{stage}|{userId}|{sessionNonce}|{externalNonce}";
        var protectedPayload = _pendingProtector.Protect(payload, PendingStateLifetime);

        HttpContext.Response.Cookies.Append(
            GoogleSecurityPendingCookieName,
            protectedPayload,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = HttpContext.Request?.IsHttps ?? false,
                Expires = DateTimeOffset.UtcNow.Add(PendingStateLifetime),
                Path = "/"
            });
    }

    private (string Stage, string UserId, string SessionNonce, string ExternalNonce)? ReadPendingSecurityState(string expectedUserId)
    {
        var cookieValue = HttpContext?.Request?.Cookies?[GoogleSecurityPendingCookieName];
        if (string.IsNullOrWhiteSpace(cookieValue))
        {
            return null;
        }

        try
        {
            var payload = _pendingProtector.Unprotect(cookieValue);
            var parts = payload.Split('|');
            if (parts.Length != 4)
            {
                return null;
            }

            var stage = parts[0];
            var userId = parts[1];
            var sessionNonce = parts[2];
            var externalNonce = parts[3];

            if (!string.Equals(userId, expectedUserId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(sessionNonce) ||
                IsSecuritySessionNonceRevoked(sessionNonce))
            {
                return null;
            }

            return (stage, userId, sessionNonce, externalNonce);
        }
        catch
        {
            return null;
        }
    }

    private void ClearPendingSecurityState()
    {
        if (HttpContext?.Response == null)
        {
            return;
        }

        HttpContext.Response.Cookies.Delete(
            GoogleSecurityPendingCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = HttpContext.Request?.IsHttps ?? false,
                Path = "/"
            });
    }

    private static void RevokeSecuritySessionNonce(string? sessionNonce)
    {
        if (string.IsNullOrWhiteSpace(sessionNonce))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in RevokedSecuritySessionNonces)
        {
            if (entry.Value <= now)
            {
                RevokedSecuritySessionNonces.TryRemove(entry.Key, out _);
            }
        }

        RevokedSecuritySessionNonces[sessionNonce] = now.Add(PendingStateLifetime);
    }

    private static bool IsSecuritySessionNonceRevoked(string sessionNonce)
    {
        if (!RevokedSecuritySessionNonces.TryGetValue(sessionNonce, out var expiresAt))
        {
            return false;
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            RevokedSecuritySessionNonces.TryRemove(sessionNonce, out _);
            return false;
        }

        return true;
    }

    private string? GetClientIp()
    {
        return HttpContext?.Connection?.RemoteIpAddress?.ToString();
    }

    private void SetStatusMessage(string message)
    {
        if (TempData != null)
        {
            TempData["StatusMessage"] = message;
        }
    }

    private void SetErrorMessage(string message)
    {
        if (TempData != null)
        {
            TempData["ErrorMessage"] = message;
        }
    }

    private static string GetStepUpDispatchErrorMessage(GoogleStepUpDispatchResult dispatch) => dispatch.Status switch
    {
        GoogleStepUpDispatchStatus.CooldownActive => $"Please wait {dispatch.RetryAfterSeconds} second(s) before requesting another Telegram verification code.",
        GoogleStepUpDispatchStatus.TelegramNotVerified => "A verified personal Telegram account is required before managing Google Sign-In.",
        GoogleStepUpDispatchStatus.AlreadyLinked => "A Google account is already linked to your profile.",
        GoogleStepUpDispatchStatus.NotLinked => "No Google account is currently linked to your profile.",
        GoogleStepUpDispatchStatus.LocalPasswordRequired => "You must have an active local password before unlinking your Google account. Use Forgot Password to set a local password first.",
        GoogleStepUpDispatchStatus.RateLimited => "Too many verification requests. Please wait a minute and try again.",
        GoogleStepUpDispatchStatus.LockedOut => "Your account is temporarily locked. Please wait before trying again.",
        GoogleStepUpDispatchStatus.DeliveryFailed => "Unable to send the Telegram verification code to your linked Telegram account. Please check your bot connection and try again.",
        _ => "Unable to process your security verification request."
    };

    private static string GetStepUpVerifyErrorMessage(GoogleStepUpVerifyStatus status) => status switch
    {
        GoogleStepUpVerifyStatus.Expired => "The Telegram verification code has expired. Please request a new code.",
        GoogleStepUpVerifyStatus.LockedTooManyAttempts => "Too many incorrect attempts. Please request a new verification code.",
        GoogleStepUpVerifyStatus.InvalidCode => "Invalid Telegram verification code. Please check your message and try again.",
        GoogleStepUpVerifyStatus.NoActiveChallenge or GoogleStepUpVerifyStatus.AlreadyUsed => "This verification code is no longer active. Please request a new code.",
        GoogleStepUpVerifyStatus.PreconditionFailed => "Account security state changed. Please refresh and try again.",
        GoogleStepUpVerifyStatus.TelegramNotVerified => "A verified personal Telegram account is required.",
        _ => "Verification failed. Please try again."
    };

    private static string GetGoogleLinkErrorMessage(GoogleLinkStatus status) => status switch
    {
        GoogleLinkStatus.GoogleAlreadyLinkedToAnotherUser => "This Google account cannot be linked because it is already associated with another account.",
        GoogleLinkStatus.AlreadyHasGoogle => "A Google account is already linked to your profile. Unlink your existing Google account first to link a different one.",
        GoogleLinkStatus.TelegramNotVerified => "A verified personal Telegram account is required before linking Google.",
        GoogleLinkStatus.InvalidNonce => "The Google linking session could not be verified. Please start again.",
        GoogleLinkStatus.LockedOut => "Your account is temporarily locked.",
        _ => "Unable to complete Google account linking. Please try again."
    };

    private static string GetGoogleUnlinkErrorMessage(GoogleUnlinkStatus status) => status switch
    {
        GoogleUnlinkStatus.LocalPasswordRequired => "Unlinking Google requires an active local password so you do not lose access. Use Forgot Password to set a local password first.",
        GoogleUnlinkStatus.NotLinked => "No Google account is currently linked to your profile.",
        GoogleUnlinkStatus.Expired => "The Telegram verification code has expired. Please request a new code.",
        GoogleUnlinkStatus.LockedTooManyAttempts => "Too many incorrect attempts. Please request a new code.",
        GoogleUnlinkStatus.InvalidCode => "Invalid Telegram verification code.",
        GoogleUnlinkStatus.NoActiveChallenge or GoogleUnlinkStatus.AlreadyUsed => "No active verification challenge found. Please request a new code.",
        GoogleUnlinkStatus.TelegramNotVerified => "A verified personal Telegram account is required.",
        _ => "Unable to unlink your Google account."
    };
}
