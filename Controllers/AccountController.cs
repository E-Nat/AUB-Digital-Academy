using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

namespace AUBDigitalAcademy.Controllers;

public class AccountController : Controller
{
    private const string PendingVerificationCookieName = "AUB_PendingVerification";
    private static readonly TimeSpan PendingVerificationLifetime = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> RevokedPendingSessionNonces = new();

    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ITelegramService _telegramService;
    private readonly IAuthService _authService;
    private readonly ITimeLimitedDataProtector _protector;
    private readonly ILogger<AccountController> _logger;

    private sealed record PendingVerificationPayload(
        string UserId,
        bool RememberMe,
        string? ReturnUrl,
        string AuthMethod,
        DateTimeOffset ExpiresAtUtc,
        string SessionNonce);

    public AccountController(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        ApplicationDbContext context,
        ITelegramService telegramService,
        IAuthService authService,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _context = context;
        _telegramService = telegramService;
        _authService = authService;
        _protector = dataProtectionProvider
            .CreateProtector("AUBDigitalAcademy.TelegramVerification")
            .ToTimeLimitedDataProtector();
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToRoleDashboard();
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        
        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = "Invalid input.", errors });
            }
            return View(model);
        }

        var authResult = await _authService.AuthenticateCredentialsAsync(model.LoginId, model.Password);

        if (authResult.Status == CredentialCheckStatus.InvalidCredentials)
        {
            ModelState.AddModelError(string.Empty, "Invalid University ID/Email or password.");
            if (isAjax) return Json(new { success = false, message = "Invalid University ID/Email or password." });
            return View(model);
        }

        if (authResult.Status == CredentialCheckStatus.LockedOut)
        {
            const string lockMsg = "Your account has been temporarily locked due to multiple failed login attempts. Please try again later.";
            _logger.LogWarning("User {UserId} account locked out.", authResult.User?.Id);
            ModelState.AddModelError(string.Empty, lockMsg);
            if (isAjax) return Json(new { success = false, message = lockMsg });
            return View(model);
        }

        if (authResult.Status == CredentialCheckStatus.InactiveAccount)
        {
            ModelState.AddModelError(string.Empty, "Your account has been deactivated. Please contact the administrator.");
            if (isAjax) return Json(new { success = false, message = "Your account has been deactivated." });
            return View(model);
        }

        var user = authResult.User!;

        // Password is valid. Check if Telegram account is linked and verified
        if (authResult.Status == CredentialCheckStatus.TelegramNotLinked)
        {
            SetPendingVerificationSession(user.Id, model.RememberMe, returnUrl);
            if (user.TelegramChatId.HasValue && user.TelegramChatId.Value > 0)
            {
                if (!await EnsureVerificationCodeDispatchedAsync(user))
                {
                    ClearPendingVerificationSession();
                    const string failMsg = "Unable to deliver the Telegram verification code. Please try again later.";
                    ModelState.AddModelError(string.Empty, failMsg);
                    if (isAjax) return Json(new { success = false, message = failMsg });
                    return View(model);
                }

                const string pendingVerifyMsg = "A single-use verification code has been dispatched to Telegram.";
                if (isAjax)
                {
                    return Json(new
                    {
                        success = false,
                        requiresVerification = true,
                        redirectUrl = Url.Action("VerifyTelegram", "Account", new { userId = user.Id }),
                        message = pendingVerifyMsg
                    });
                }
                return RedirectToAction("VerifyTelegram", new { userId = user.Id });
            }

            const string linkMsg = "Please pair your Telegram account to enable required Two-Factor Authentication.";
            if (isAjax)
            {
                return Json(new
                {
                    success = false,
                    requiresLink = true,
                    redirectUrl = Url.Action("LinkTelegram", "Account", new { userId = user.Id }),
                    message = linkMsg
                });
            }
            return RedirectToAction("LinkTelegram", new { userId = user.Id });
        }

        // Enforce mandatory Telegram 2FA on EVERY login for all roles
        SetPendingVerificationSession(user.Id, model.RememberMe, returnUrl);
        if (!await EnsureVerificationCodeDispatchedAsync(user))
        {
            ClearPendingVerificationSession();
            const string failMsg = "Unable to deliver the Telegram verification code. Please try again later.";
            ModelState.AddModelError(string.Empty, failMsg);
            if (isAjax) return Json(new { success = false, message = failMsg });
            return View(model);
        }

        const string verifyMsg = "A single-use verification code has been dispatched to Telegram.";
        ModelState.AddModelError(string.Empty, verifyMsg);
        if (isAjax)
        {
            return Json(new
            {
                success = false,
                requiresVerification = true,
                redirectUrl = Url.Action("VerifyTelegram", "Account", new { userId = user.Id }),
                message = verifyMsg
            });
        }
        return RedirectToAction("VerifyTelegram", new { userId = user.Id });
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToRoleDashboard();
        }

        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToRoleDashboard();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var trimmedUid = model.UniversityId.Trim();
        var trimmedName = model.FullName.Trim();
        var normalizedUid = _userManager.NormalizeName(trimmedUid);

        // Check if Student ID / UniversityId is already in use
        var existingUser = await _userManager.Users
            .FirstOrDefaultAsync(u =>
                u.UniversityId == trimmedUid ||
                u.UserName == trimmedUid ||
                (normalizedUid != null && u.NormalizedUserName == normalizedUid));

        if (existingUser != null)
        {
            ModelState.AddModelError("UniversityId", "This Student ID is already registered. Please sign in or contact the registrar.");
            return View(model);
        }

        // Student registration requires only: Student Name, Student ID (UniversityId), and Password (no email required)
        var user = new User
        {
            UserName = trimmedUid,
            UniversityId = trimmedUid,
            FullName = trimmedName,
            Email = null,
            EmailConfirmed = false,
            IsActive = true,
            IsTelegramVerified = false,
            TelegramChatId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        // Securely assign the Student role only
        var roleResult = await _userManager.AddToRoleAsync(user, "Student");
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "Unable to complete registration role assignment. Please try again.");
            return View(model);
        }

        // Bind pending verification session to this client; never issue auth cookie or dispatch OTP before Telegram linking
        SetPendingVerificationSession(user.Id, rememberMe: false, returnUrl: null, authMethod: "Registration");

        TempData["InfoMessage"] = "Account created! Please link your personal Telegram account to receive your verification code.";
        return RedirectToAction(nameof(LinkTelegram), new { userId = user.Id });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LinkTelegram(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            ClearPendingVerificationSession();
            return RedirectToAction(nameof(Login));
        }

        var (ok, user, _, _) = await ResolveEligiblePendingUserAsync(userId, requireUnverifiedRegistration: true);
        if (!ok || user == null)
        {
            ClearPendingVerificationSession();
            TempData["ErrorMessage"] = "Session expired or invalid. Please sign in to link your Telegram account.";
            return RedirectToAction(nameof(Login));
        }

        // Never overwrite an existing TelegramChatId association; proceed directly to OTP verification
        if (user.TelegramChatId.HasValue && user.TelegramChatId.Value > 0)
        {
            return RedirectToAction(nameof(VerifyTelegram), new { userId = user.Id });
        }

        var (linkUrl, plainToken, expiresAt) = await _telegramService.GenerateLinkTokenAsync(user.Id, HttpContext.Connection.RemoteIpAddress?.ToString());

        ViewBag.LinkUrl = linkUrl;
        ViewBag.PlainToken = plainToken;
        ViewBag.ExpiresAt = expiresAt;
        ViewBag.StudentName = user.FullName;
        ViewBag.UniversityId = user.UniversityId ?? user.UserName;
        ViewBag.UserId = user.Id;

        return View();
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CheckTelegramLinkStatus(string? userId = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            ClearPendingVerificationSession();
            return Unauthorized(new { linked = false });
        }

        var (ok, user, _, failureReason) = await ResolveEligiblePendingUserAsync(userId, requireUnverifiedRegistration: true);
        if (!ok || user == null)
        {
            if (!string.Equals(failureReason, "user_mismatch", StringComparison.Ordinal))
            {
                ClearPendingVerificationSession();
            }
            return Unauthorized(new { linked = false });
        }

        var isLinked = user.TelegramChatId.HasValue &&
                       user.TelegramChatId.Value > 0 &&
                       await _telegramService.IsAccountLinkedAsync(user.Id);

        return Json(new { linked = isLinked });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> VerifyTelegram(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            ClearPendingVerificationSession();
            return RedirectToAction(nameof(Login));
        }

        var (ok, user, _, _) = await ResolveEligiblePendingUserAsync(userId, requireUnverifiedRegistration: true);
        if (!ok || user == null)
        {
            ClearPendingVerificationSession();
            TempData["ErrorMessage"] = "For security, please sign in with your credentials to verify your account.";
            return RedirectToAction(nameof(Login));
        }

        // Require Telegram linking before dispatching or verifying OTP
        if (!user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
        {
            TempData["InfoMessage"] = "Please link your personal Telegram account before verifying.";
            return RedirectToAction(nameof(LinkTelegram), new { userId = user.Id });
        }

        // Ensure active code exists upon arriving at page (only for linked TelegramChatId)
        if (!await EnsureVerificationCodeDispatchedAsync(user))
        {
            TempData["ErrorMessage"] = "Unable to deliver the Telegram verification code. Please try again later.";
        }

        var model = new VerifyTelegramViewModel
        {
            UserId = user.Id,
            UniversityId = user.UniversityId ?? string.Empty,
            FullName = user.FullName
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> VerifyTelegram(VerifyTelegramViewModel model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.UserId))
        {
            ClearPendingVerificationSession();
            TempData["ErrorMessage"] = "Your verification session has expired. Please sign in to verify your account.";
            return RedirectToAction(nameof(Login));
        }

        // Prevent cross-account verification and reject missing, expired, tampered, replayed, or ineligible sessions
        var (ok, user, pendingSession, _) = await ResolveEligiblePendingUserAsync(model.UserId, requireUnverifiedRegistration: true);
        if (!ok || user == null || pendingSession == null)
        {
            ClearPendingVerificationSession();
            TempData["ErrorMessage"] = "Your verification session has expired. Please sign in to verify your account.";
            return RedirectToAction(nameof(Login));
        }

        // Never trust client-supplied user metadata
        model.UserId = user.Id;
        model.UniversityId = user.UniversityId ?? string.Empty;
        model.FullName = user.FullName;

        if (!user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
        {
            TempData["ErrorMessage"] = "Please link your personal Telegram account before completing verification.";
            return RedirectToAction(nameof(LinkTelegram), new { userId = user.Id });
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Verify server-side pending verification request via shared auth service
        var verifyResult = await _authService.VerifyWebTwoFactorAsync(user.Id, model.Code ?? string.Empty);

        if (verifyResult.Status == TwoFactorVerifyStatus.LockedOut)
        {
            ClearPendingVerificationSession();
            TempData["ErrorMessage"] = "Your account has been temporarily locked due to multiple failed login attempts. Please try again later.";
            return RedirectToAction(nameof(Login));
        }

        if (verifyResult.Status == TwoFactorVerifyStatus.UserUnavailable)
        {
            ClearPendingVerificationSession();
            TempData["ErrorMessage"] = "Your account has been deactivated or is unavailable. Please contact the administrator.";
            return RedirectToAction(nameof(Login));
        }

        if (verifyResult.Status == TwoFactorVerifyStatus.TelegramNotLinked)
        {
            ClearPendingVerificationSession();
            TempData["ErrorMessage"] = "Your Telegram account must be linked and verified before signing in.";
            return RedirectToAction(nameof(Login));
        }

        if (verifyResult.Status == TwoFactorVerifyStatus.AlreadyUsed)
        {
            ModelState.AddModelError(string.Empty, "No active verification code was found. Please request a new code.");
            return View(model);
        }

        if (verifyResult.Status == TwoFactorVerifyStatus.Expired)
        {
            ModelState.AddModelError(string.Empty, "Your verification code has expired. Please request a new code.");
            return View(model);
        }

        if (verifyResult.Status == TwoFactorVerifyStatus.TooManyAttempts)
        {
            ModelState.AddModelError("Code", "Maximum verification attempts reached. Please request a new code.");
            return View(model);
        }

        if (verifyResult.Status != TwoFactorVerifyStatus.Verified)
        {
            int remainingAttempts = verifyResult.RemainingAttempts ?? 0;
            ModelState.AddModelError("Code", $"Invalid verification code. You have {remainingAttempts} attempt(s) remaining.");
            return View(model);
        }

        if (!await _signInManager.CanSignInAsync(user))
        {
            ClearPendingVerificationSession();
            TempData["ErrorMessage"] = "Your account is not eligible to sign in. Please contact the administrator.";
            return RedirectToAction(nameof(Login));
        }

        // Verification successful: transition user to verified state only after OTP check passes
        user.IsTelegramVerified = true;
        user.UpdatedAt = DateTime.UtcNow;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Unable to finalize account verification. Please try again.");
            return View(model);
        }

        ClearPendingVerificationSession();
        await SignOutExternalSchemeSafelyAsync();

        // Establish the authenticated identity session only after Telegram 2FA succeeds
        await _signInManager.SignInAsync(user, isPersistent: pendingSession.RememberMe);

        _logger.LogInformation("Telegram 2FA verification completed successfully for user {UniversityId} ({UserId}) via {AuthMethod}.",
            user.UniversityId, user.Id, pendingSession.AuthMethod);

        var redirectUrl = await GetRoleRedirectUrlAsync(user);
        if (!string.IsNullOrEmpty(pendingSession.ReturnUrl) && Url.IsLocalUrl(pendingSession.ReturnUrl))
        {
            redirectUrl = pendingSession.ReturnUrl;
        }

        return Redirect(redirectUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResendTelegramCode(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            ClearPendingVerificationSession();
            return RedirectToAction(nameof(Login));
        }

        // Verify session authorization and eligibility before resending
        var (ok, user, _, _) = await ResolveEligiblePendingUserAsync(userId, requireUnverifiedRegistration: true);
        if (!ok || user == null)
        {
            ClearPendingVerificationSession();
            TempData["ErrorMessage"] = "Session expired. Please sign in to request a new code.";
            return RedirectToAction(nameof(Login));
        }

        if (!user.TelegramChatId.HasValue || user.TelegramChatId.Value <= 0)
        {
            TempData["InfoMessage"] = "Please link your personal Telegram account first.";
            return RedirectToAction(nameof(LinkTelegram), new { userId = user.Id });
        }

        var dispatch = await _authService.ResendWebVerificationCodeAsync(user);
        if (dispatch.Status == TwoFactorDispatchStatus.RateLimited)
        {
            int waitSeconds = dispatch.RetryAfterSeconds ?? 60;
            TempData["ErrorMessage"] = $"Please wait {waitSeconds} seconds before requesting a new code.";
            return RedirectToAction(nameof(VerifyTelegram), new { userId = user.Id });
        }

        if (dispatch.Status != TwoFactorDispatchStatus.Sent)
        {
            TempData["ErrorMessage"] = "Unable to deliver the Telegram verification code. Please try again later.";
            return RedirectToAction(nameof(VerifyTelegram), new { userId = user.Id });
        }

        TempData["InfoMessage"] = "A new verification code has been dispatched to Telegram.";
        return RedirectToAction(nameof(VerifyTelegram), new { userId = user.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelVerification()
    {
        if (TryGetPendingVerificationSession(out var session))
        {
            await _authService.InvalidatePendingChallengesAsync(session.UserId);
        }

        ClearPendingVerificationSession();
        await SignOutExternalSchemeSafelyAsync();
        TempData["InfoMessage"] = "Verification was cancelled.";
        return RedirectToAction(nameof(Login));
    }

    private async Task<bool> EnsureVerificationCodeDispatchedAsync(User user)
    {
        var result = await _authService.EnsureWebVerificationCodeDispatchedAsync(user);
        if (result.Status != TwoFactorDispatchStatus.Sent || result.Request == null)
        {
            return false;
        }

        if (result.Request.CreatedAt >= DateTime.UtcNow.AddSeconds(-5))
        {
            TempData["InfoMessage"] = "A single-use verification code has been dispatched to Telegram.";
        }

        return true;
    }

    [HttpGet]
    public IActionResult LogoutConfirm()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        if (TryGetPendingVerificationSession(out var session))
        {
            await _authService.InvalidatePendingChallengesAsync(session.UserId);
        }

        ClearPendingVerificationSession();
        await SignOutExternalSchemeSafelyAsync();
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User logged out.");
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var normalizedInput = model.Email.Trim();
        var user = await _userManager.FindByEmailAsync(normalizedInput)
                   ?? await FindByUniversityIdAsync(normalizedInput);

        if (user != null)
        {
            _logger.LogInformation("Password reset requested for {Identifier}", normalizedInput);
        }

        return View("ForgotPasswordConfirmation");
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }
    
    [HttpGet]
    public IActionResult ResetPassword(string? token = null, string? email = null)
    {
        if (string.IsNullOrEmpty(token))
        {
            return BadRequest("A valid password reset token is required.");
        }

        var model = new ResetPasswordViewModel { Token = token, Email = email ?? string.Empty };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        return RedirectToAction(nameof(ResetPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    // Google OAuth
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExternalLogin(string provider, string? returnUrl = null)
    {
        ClearPendingVerificationSession();
        await SignOutExternalSchemeSafelyAsync();

        if (string.IsNullOrWhiteSpace(provider))
        {
            TempData["ErrorMessage"] = "External login provider is required.";
            return RedirectToAction(nameof(Login));
        }

        var externalSchemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        var matchedScheme = externalSchemes.FirstOrDefault(s =>
            string.Equals(s.Name, provider.Trim(), StringComparison.OrdinalIgnoreCase));

        if (matchedScheme == null)
        {
            TempData["ErrorMessage"] = "The selected external login provider is not configured.";
            return RedirectToAction(nameof(Login));
        }

        var safeReturnUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl = safeReturnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(matchedScheme.Name, redirectUrl);
        properties.Items["AubExternalNonce"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return Challenge(properties, matchedScheme.Name);
    }

    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(remoteError))
            {
                ClearPendingVerificationSession();
                await SignOutExternalSchemeSafelyAsync();
                _logger.LogWarning("External authentication returned an error/cancellation response.");
                ModelState.AddModelError(string.Empty, "External authentication was cancelled or failed. Please try again.");
                return View("Login");
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            // Always clear the temporary external authentication cookie immediately after reading it
            await SignOutExternalSchemeSafelyAsync();

            if (info == null)
            {
                ClearPendingVerificationSession();
                ModelState.AddModelError(string.Empty, "External login session expired or could not be verified. Please try again.");
                return View("Login");
            }

            var externalResult = await _authService.InitiateExternalTwoFactorLoginAsync(info);
            if (externalResult.Status != ExternalLoginStatus.TwoFactorPending || externalResult.User == null)
            {
                ClearPendingVerificationSession();
                ModelState.AddModelError(string.Empty, GetExternalLoginErrorMessage(externalResult.Status));
                return View("Login");
            }

            var user = externalResult.User;
            if (!await _signInManager.CanSignInAsync(user))
            {
                ClearPendingVerificationSession();
                ModelState.AddModelError(string.Empty, "Your account is not eligible for external sign-in. Please contact the administrator.");
                return View("Login");
            }

            var safeReturnUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
            SetPendingVerificationSession(user.Id, rememberMe: false, returnUrl: safeReturnUrl, authMethod: info.LoginProvider);
            TempData["InfoMessage"] = "A single-use verification code has been dispatched to Telegram.";

            return RedirectToAction(nameof(VerifyTelegram), new { userId = user.Id });
        }
        finally
        {
            await SignOutExternalSchemeSafelyAsync();
        }
    }

    private static string GetExternalLoginErrorMessage(ExternalLoginStatus status) => status switch
    {
        ExternalLoginStatus.Replayed => "This external login response has already been used or expired. Please sign in again.",
        ExternalLoginStatus.NotAssociated => "This Google account is not associated with an existing AUB Digital Academy account. Please sign in with your Student ID/Email and password, or contact IT Support.",
        ExternalLoginStatus.InactiveAccount => "Your account has been deactivated. Please contact the administrator.",
        ExternalLoginStatus.LockedOut => "Your account has been temporarily locked due to multiple failed login attempts. Please try again later.",
        ExternalLoginStatus.TelegramNotLinked => "Your Telegram account must be linked and verified before signing in with Google. Please sign in with your Student ID/Email and password to link Telegram first.",
        ExternalLoginStatus.OtpDeliveryFailed => "Unable to deliver the Telegram verification code. Please try again later.",
        _ => "Error loading external login information."
    };

    private async Task SignOutExternalSchemeSafelyAsync()
    {
        if (HttpContext?.RequestServices?.GetService(typeof(IAuthenticationService)) != null)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    // Helper methods
    private async Task<(bool Ok, User? User, PendingVerificationPayload? Session, string FailureReason)> ResolveEligiblePendingUserAsync(
        string? expectedUserId = null,
        bool requireUnverifiedRegistration = false)
    {
        if (!TryGetPendingVerificationSession(out var session))
        {
            return (false, null, null, "missing_or_invalid_session");
        }

        if (!string.IsNullOrWhiteSpace(expectedUserId) &&
            !string.Equals(session.UserId, expectedUserId, StringComparison.Ordinal))
        {
            return (false, null, null, "user_mismatch");
        }

        var user = await _userManager.FindByIdAsync(session.UserId);
        if (user == null || !user.IsActive)
        {
            return (false, null, null, "user_unavailable");
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return (false, null, null, "locked_out");
        }

        if (requireUnverifiedRegistration &&
            string.Equals(session.AuthMethod, "Registration", StringComparison.Ordinal) &&
            user.IsTelegramVerified)
        {
            return (false, null, null, "already_verified");
        }

        return (true, user, session, string.Empty);
    }

    private void SetPendingVerificationSession(
        string userId,
        bool rememberMe = false,
        string? returnUrl = null,
        string authMethod = "Password")
    {
        // Revoke any prior pending session cookie before issuing a fresh one
        if (TryGetPendingVerificationSessionPayloadUnsafe(out var priorPayload))
        {
            RevokePendingSessionNonce(priorPayload.SessionNonce, priorPayload.ExpiresAtUtc);
        }

        var safeReturnUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        var expiresAt = DateTimeOffset.UtcNow.Add(PendingVerificationLifetime);
        var sessionNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var payload = new PendingVerificationPayload(userId, rememberMe, safeReturnUrl, authMethod, expiresAt, sessionNonce);
        var json = JsonSerializer.Serialize(payload);
        var token = _protector.Protect(json, PendingVerificationLifetime);

        Response.Cookies.Append(PendingVerificationCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt,
            Secure = Request?.IsHttps ?? false,
            IsEssential = true
        });
    }

    private bool TryGetPendingVerificationSessionPayloadUnsafe(out PendingVerificationPayload payload)
    {
        payload = default!;
        if (Request == null ||
            !Request.Cookies.TryGetValue(PendingVerificationCookieName, out var token) ||
            string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var json = _protector.Unprotect(token, out var protectorExpiration);
            if (protectorExpiration <= DateTimeOffset.UtcNow)
            {
                return false;
            }

            var parsed = JsonSerializer.Deserialize<PendingVerificationPayload>(json);
            if (parsed == null ||
                string.IsNullOrWhiteSpace(parsed.UserId) ||
                string.IsNullOrWhiteSpace(parsed.SessionNonce) ||
                parsed.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                return false;
            }

            payload = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetPendingVerificationSession(out PendingVerificationPayload payload)
    {
        if (!TryGetPendingVerificationSessionPayloadUnsafe(out payload))
        {
            return false;
        }

        if (IsPendingSessionNonceRevoked(payload.SessionNonce))
        {
            payload = default!;
            return false;
        }

        return true;
    }

    private void ClearPendingVerificationSession()
    {
        if (TryGetPendingVerificationSessionPayloadUnsafe(out var existing))
        {
            RevokePendingSessionNonce(existing.SessionNonce, existing.ExpiresAtUtc);
        }

        Response?.Cookies.Delete(PendingVerificationCookieName);
    }

    private static void RevokePendingSessionNonce(string nonce, DateTimeOffset expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(nonce))
        {
            return;
        }

        PruneExpiredRevokedNonces();
        var expiry = expiresAtUtc > DateTimeOffset.UtcNow
            ? expiresAtUtc
            : DateTimeOffset.UtcNow.Add(PendingVerificationLifetime);
        RevokedPendingSessionNonces[nonce] = expiry;
    }

    private static bool IsPendingSessionNonceRevoked(string nonce)
    {
        PruneExpiredRevokedNonces();
        return RevokedPendingSessionNonces.TryGetValue(nonce, out var expiresAt) &&
               expiresAt > DateTimeOffset.UtcNow;
    }

    private static void PruneExpiredRevokedNonces()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in RevokedPendingSessionNonces)
        {
            if (kvp.Value <= now)
            {
                RevokedPendingSessionNonces.TryRemove(kvp.Key, out _);
            }
        }
    }

    private async Task<User?> FindByUniversityIdAsync(string universityId)
    {
        return await _userManager.Users
            .FirstOrDefaultAsync(u => u.UniversityId == universityId || u.UserName == universityId);
    }

    private IActionResult RedirectToRoleDashboard()
    {
        if (User.IsInRole("Admin")) return RedirectToAction("Index", "Admin");
        if (User.IsInRole("Teacher") || User.IsInRole("Assistance")) return RedirectToAction("Dashboard", "Teacher");
        if (User.IsInRole("Student")) return RedirectToAction("Dashboard", "Student");
        
        return RedirectToAction("Index", "Admin"); 
    }
    
    private async Task<string> GetRoleRedirectUrlAsync(User user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains("Admin")) return Url.Action("Index", "Admin") ?? "/Admin";
        if (roles.Contains("Teacher") || roles.Contains("Assistance")) return Url.Action("Dashboard", "Teacher") ?? "/Teacher/Dashboard";
        if (roles.Contains("Student")) return Url.Action("Dashboard", "Student") ?? "/Student/Dashboard";
        return Url.Action("Index", "Admin") ?? "/Admin";
    }
}
