using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using System.Security.Claims;

namespace AUBDigitalAcademy.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
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

        var user = await _userManager.FindByEmailAsync(model.LoginId)
                   ?? await FindByUniversityIdAsync(model.LoginId);

        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid University ID/Email or password.");
            if (isAjax) return Json(new { success = false, message = "Invalid University ID/Email or password." });
            return View(model);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Your account has been deactivated. Please contact the administrator.");
            if (isAjax) return Json(new { success = false, message = "Your account has been deactivated." });
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!, model.Password, model.RememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            _logger.LogInformation("User {UserId} logged in.", user.Id);
            
            var redirectUrl = GetRoleRedirectUrl(user);
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                redirectUrl = returnUrl;
            }

            if (isAjax)
            {
                return Json(new { success = true, redirectUrl });
            }
            
            return Redirect(redirectUrl);
        }

        ModelState.AddModelError(string.Empty, "Invalid University ID/Email or password.");
        if (isAjax) return Json(new { success = false, message = "Invalid University ID/Email or password." });
        
        return View(model);
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

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user != null)
        {
            // In a real app, generate token and send email
            // var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            _logger.LogInformation("Password reset requested for {Email}", model.Email);
        }

        return View("ForgotPasswordConfirmation");
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }
    
    [HttpGet]
    public IActionResult ResetPassword(string? token = null)
    {
        if (token == null)
        {
            // For testing purposes, we allow it to load without a token
            // In production, return BadRequest("A token must be supplied for password reset.");
            token = "dummy-token"; 
        }

        var model = new ResetPasswordViewModel { Token = token };
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
            // Don't reveal that the user does not exist
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        // In a real app, reset password using token
        // var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
        // if (result.Succeeded)
        
        // Simulating success
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
            var user = await _userManager.FindByEmailAsync(info.Principal.FindFirst(ClaimTypes.Email)!.Value);
            return Redirect(GetRoleRedirectUrl(user!));
        }

        // If the user does not have an account, create one
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
                    IsActive = true
                };

                var createResult = await _userManager.CreateAsync(user);
                if (createResult.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, "Student");
                }
            }

            await _userManager.AddLoginAsync(user, info);
            await _signInManager.SignInAsync(user, isPersistent: false);
            return Redirect(GetRoleRedirectUrl(user));
        }

        ModelState.AddModelError(string.Empty, "Error loading external login information.");
        return View("Login");
    }

    // Helper methods
    private async Task<User?> FindByUniversityIdAsync(string universityId)
    {
        return await _userManager.Users
            .FirstOrDefaultAsync(u => u.UniversityId == universityId);
    }

    private IActionResult RedirectToRoleDashboard()
    {
        if (User.IsInRole("Admin")) return RedirectToAction("Index", "Dashboard", new { area = "Admin" }); // or wherever Admin is mapped
        if (User.IsInRole("Teacher")) return RedirectToAction("Dashboard", "Teacher");
        if (User.IsInRole("Student")) return RedirectToAction("Dashboard", "Student");
        
        // Fallback using the new controllers
        return RedirectToAction("Index", "Dashboard"); 
    }
    
    private string GetRoleRedirectUrl(User user)
    {
        // Simple synchronous helper for AJAX redirect URL generation
        var roles = _userManager.GetRolesAsync(user).GetAwaiter().GetResult();
        if (roles.Contains("Admin")) return Url.Action("Index", "Admin") ?? "/Admin";
        if (roles.Contains("Teacher")) return Url.Action("Dashboard", "Teacher") ?? "/Teacher";
        if (roles.Contains("Student")) return Url.Action("Dashboard", "Student") ?? "/Student";
        return Url.Action("Index", "Dashboard") ?? "/";
    }
}
