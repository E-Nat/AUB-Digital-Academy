using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;
using System.Security.Claims;
using System.Security.Cryptography;

namespace AUBDigitalAcademy.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ITelegramService _telegramService;
    private readonly IAuthService _authService;
    private readonly IDataProtector _protector;
    private readonly ILogger<AccountController> _logger;

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
        _protector = dataProtectionProvider.CreateProtector("AUBDigitalAcademy.TelegramVerification");
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

        // Password is valid. Check if Telegram account is linked
        if (authResult.Status == CredentialCheckStatus.TelegramNotLinked)
        {
            SetPendingVerificationSession(user.Id, model.RememberMe, returnUrl);
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
        await EnsureVerificationCodeDispatchedAsync(user);

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

        // Check if Student ID / UniversityId is already in use
        var existingUser = await _userManager.Users
            .FirstOrDefaultAsync(u => u.UniversityId == trimmedUid || u.UserName == trimmedUid);

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
        await _userManager.AddToRoleAsync(user, "Student");

        // Generate short-lived, single-use, 6-digit verification code
        var verificationCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        var request = new TelegramVerificationRequest
        {
            UserId = user.Id,
            UniversityId = user.UniversityId,
            Code = verificationCode,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            IsUsed = false,
            AttemptsCount = 0,
            MaxAttempts = 5
        };

        _context.TelegramVerificationRequests.Add(request);
        await _context.SaveChangesAsync();

        // Deliver code via Telegram without logging code or credentials
        await _telegramService.SendVerificationCodeAsync(user.UniversityId, user.FullName, verificationCode, user.TelegramChatId?.ToString());

        // Bind pending verification session to this client
        SetPendingVerificationSession(user.Id);

        TempData["InfoMessage"] = "Registration successful! A single-use verification code has been dispatched to Telegram. Please enter your code to activate your account.";
        return RedirectToAction("VerifyTelegram", new { userId = user.Id });
    }

    [HttpGet]
    public async Task<IActionResult> LinkTelegram(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return RedirectToAction("Login");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return RedirectToAction("Login");
        }

        if (!ValidatePendingVerificationSession(user.Id))
        {
            TempData["ErrorMessage"] = "Session expired. Please sign in to link your Telegram account.";
            return RedirectToAction("Login");
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
    public async Task<IActionResult> CheckTelegramLinkStatus(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Json(new { linked = false });
        }
        var isLinked = await _telegramService.IsAccountLinkedAsync(userId);
        return Json(new { linked = isLinked });
    }

    [HttpGet]
    public async Task<IActionResult> VerifyTelegram(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return RedirectToAction("Login");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return RedirectToAction("Login");
        }

        // Prevent cross-account verification access without proof of session ownership
        if (!ValidatePendingVerificationSession(user.Id))
        {
            TempData["ErrorMessage"] = "For security, please sign in with your credentials to verify your account.";
            return RedirectToAction("Login");
        }

        // Ensure active code exists upon arriving at page
        await EnsureVerificationCodeDispatchedAsync(user);

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
    public async Task<IActionResult> VerifyTelegram(VerifyTelegramViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Resolve pending account directly from database without trusting client user metadata
        var user = await _userManager.FindByIdAsync(model.UserId);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid verification session.");
            return View(model);
        }

        model.UniversityId = user.UniversityId ?? string.Empty;
        model.FullName = user.FullName;

        // Prevent cross-account verification: verify session token
        if (!ValidatePendingVerificationSession(user.Id))
        {
            TempData["ErrorMessage"] = "Your verification session has expired. Please sign in to verify your account.";
            return RedirectToAction("Login");
        }

        // Verify server-side pending verification request via shared auth service
        var verifyResult = await _authService.VerifyWebTwoFactorAsync(model.UserId, model.Code ?? string.Empty);

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

        // Verification successful: mark user verified
        user.IsTelegramVerified = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var (rememberMe, returnUrl) = GetPendingVerificationOptions();
        ClearPendingVerificationSession();

        // Establish the authenticated identity session!
        await _signInManager.SignInAsync(user, isPersistent: rememberMe);

        _logger.LogInformation("Telegram 2FA verification completed successfully for user {UniversityId} ({UserId}).", user.UniversityId, user.Id);

        var redirectUrl = await GetRoleRedirectUrlAsync(user);
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            redirectUrl = returnUrl;
        }

        return Redirect(redirectUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendTelegramCode(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return RedirectToAction("Login");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return RedirectToAction("Login");
        }

        // Verify session authorization before resending
        if (!ValidatePendingVerificationSession(user.Id))
        {
            TempData["ErrorMessage"] = "Session expired. Please sign in to request a new code.";
            return RedirectToAction("Login");
        }

        // Rate limiting cooldown: 60 seconds between resend requests
        var latestRequest = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestRequest != null && (DateTime.UtcNow - latestRequest.CreatedAt).TotalSeconds < 60)
        {
            int waitSeconds = 60 - (int)(DateTime.UtcNow - latestRequest.CreatedAt).TotalSeconds;
            TempData["ErrorMessage"] = $"Please wait {waitSeconds} seconds before requesting a new code.";
            return RedirectToAction("VerifyTelegram", new { userId });
        }

        // Mark previous pending requests as used/invalidated
        var pendingRequests = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == userId && !r.IsUsed)
            .ToListAsync();

        foreach (var req in pendingRequests)
        {
            req.IsUsed = true;
            req.UsedAt = DateTime.UtcNow;
        }

        // Generate fresh code
        var verificationCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var newRequest = new TelegramVerificationRequest
        {
            UserId = user.Id,
            UniversityId = user.UniversityId ?? string.Empty,
            Code = verificationCode,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            IsUsed = false,
            AttemptsCount = 0,
            MaxAttempts = 5
        };

        _context.TelegramVerificationRequests.Add(newRequest);
        await _context.SaveChangesAsync();

        await _telegramService.SendVerificationCodeAsync(user.UniversityId ?? string.Empty, user.FullName, verificationCode, user.TelegramChatId?.ToString());

        TempData["InfoMessage"] = "A new verification code has been dispatched to Telegram.";
        return RedirectToAction("VerifyTelegram", new { userId });
    }

    private async Task EnsureVerificationCodeDispatchedAsync(User user)
    {
        var req = await _authService.EnsureWebVerificationCodeDispatchedAsync(user);
        if (req.CreatedAt >= DateTime.UtcNow.AddSeconds(-5))
        {
            TempData["InfoMessage"] = "A single-use verification code has been dispatched to Telegram.";
        }
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
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Action("ExternalLoginCallback", "Account", new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError != null)
        {
            ModelState.AddModelError(string.Empty, $"Error from external provider: {remoteError}");
            return View("Login");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return RedirectToAction("Login");
        }

        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false);

        if (result.Succeeded)
        {
            var ssoUser = await _userManager.FindByEmailAsync(info.Principal.FindFirst(ClaimTypes.Email)!.Value);
            if (ssoUser != null && !ssoUser.IsActive)
            {
                await _signInManager.SignOutAsync();
                ModelState.AddModelError(string.Empty, "Your account has been deactivated. Please contact the administrator.");
                return View("Login");
            }
            return Redirect(await GetRoleRedirectUrlAsync(ssoUser!));
        }

        var email = info.Principal.FindFirst(ClaimTypes.Email)?.Value;
        var name = info.Principal.FindFirst(ClaimTypes.Name)?.Value;

        if (email != null)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new User
                {
                    UserName = email,
                    Email = email,
                    FullName = name ?? email,
                    EmailConfirmed = true,
                    IsActive = true,
                    IsTelegramVerified = true
                };

                var createResult = await _userManager.CreateAsync(user);
                if (createResult.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, "Student");
                }
            }
            else if (!user.IsActive)
            {
                ModelState.AddModelError(string.Empty, "Your account has been deactivated. Please contact the administrator.");
                return View("Login");
            }

            await _userManager.AddLoginAsync(user, info);
            await _signInManager.SignInAsync(user, isPersistent: false);
            return Redirect(await GetRoleRedirectUrlAsync(user));
        }

        ModelState.AddModelError(string.Empty, "Error loading external login information.");
        return View("Login");
    }

    // Helper methods
    private void SetPendingVerificationSession(string userId, bool rememberMe = false, string? returnUrl = null)
    {
        var safeReturnUrl = returnUrl ?? string.Empty;
        var token = _protector.Protect($"{userId}:{rememberMe}:{safeReturnUrl}:{DateTime.UtcNow.Ticks}");
        Response.Cookies.Append("AUB_PendingVerification", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(15),
            Secure = Request?.IsHttps ?? false
        });
    }

    private bool ValidatePendingVerificationSession(string userId)
    {
        if (Request == null || !Request.Cookies.TryGetValue("AUB_PendingVerification", out var token) || string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            var unprotected = _protector.Unprotect(token);
            var parts = unprotected.Split(':');
            if (parts.Length >= 2 && parts[0] == userId)
            {
                var ticksIndex = parts.Length - 1;
                var createdTicks = long.Parse(parts[ticksIndex]);
                var created = new DateTime(createdTicks, DateTimeKind.Utc);
                return (DateTime.UtcNow - created).TotalMinutes <= 15;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private (bool RememberMe, string? ReturnUrl) GetPendingVerificationOptions()
    {
        if (Request == null || !Request.Cookies.TryGetValue("AUB_PendingVerification", out var token) || string.IsNullOrEmpty(token))
        {
            return (false, null);
        }

        try
        {
            var unprotected = _protector.Unprotect(token);
            var parts = unprotected.Split(':');
            if (parts.Length >= 4)
            {
                bool.TryParse(parts[1], out var rememberMe);
                var returnUrl = string.IsNullOrWhiteSpace(parts[2]) ? null : parts[2];
                return (rememberMe, returnUrl);
            }
        }
        catch { }

        return (false, null);
    }

    private void ClearPendingVerificationSession()
    {
        Response.Cookies.Delete("AUB_PendingVerification");
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
