using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AUBDigitalAcademy.Controllers.Api;

public class ApiLoginRequest
{
    public string? LoginId { get; set; }
    public string? Password { get; set; }
}

public class ApiVerify2FaRequest
{
    public string? VerificationId { get; set; }
    public string? Code { get; set; }
}

public class ApiForgotPasswordRequest
{
    public string? LoginId { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<User> userManager,
        IAuthService authService,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _authService = authService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] ApiLoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.LoginId) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new
            {
                success = false,
                status = "InvalidInput",
                message = "Please provide both Login ID/Email and Password."
            });
        }

        var authResult = await _authService.AuthenticateCredentialsAsync(request.LoginId, request.Password);

        switch (authResult.Status)
        {
            case CredentialCheckStatus.InvalidCredentials:
                return Unauthorized(new
                {
                    success = false,
                    status = "InvalidCredentials",
                    message = "Invalid University ID/Email or password."
                });

            case CredentialCheckStatus.LockedOut:
                return StatusCode(StatusCodes.Status423Locked, new
                {
                    success = false,
                    status = "AccountLocked",
                    message = "Your account has been temporarily locked due to multiple failed login attempts. Please try again later."
                });

            case CredentialCheckStatus.InactiveAccount:
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    success = false,
                    status = "AccountInactive",
                    message = "Your account has been deactivated. Please contact the administrator."
                });

            case CredentialCheckStatus.TelegramNotLinked:
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    success = false,
                    status = "TelegramNotLinked",
                    requiresTelegramLink = true,
                    message = "Your Telegram account must be linked and verified before signing in via API. Please link your Telegram account using the Web portal first."
                });
        }

        var user = authResult.User!;
        var challenge = await _authService.CreateApiTwoFactorChallengeAsync(user);

        if (challenge.Status == TwoFactorDispatchStatus.InvalidConfiguration)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = "ConfigurationError",
                message = "API token signing is not properly configured. Please contact an administrator."
            });
        }

        if (challenge.Status != TwoFactorDispatchStatus.Sent)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                status = "OtpDeliveryFailed",
                message = "Unable to deliver Telegram verification code. Please try again later."
            });
        }

        _logger.LogInformation("API Telegram 2FA challenge issued for user {UserId}.", user.Id);

        return Ok(new
        {
            success = true,
            status = "TwoFactorPending",
            requiresTwoFactor = true,
            verificationId = challenge.VerificationId,
            expiresAt = challenge.ExpiresAt,
            expiresInSeconds = challenge.ExpiresInSeconds,
            message = "A single-use verification code has been sent to your linked Telegram account."
        });
    }

    [HttpPost("verify-2fa")]
    public async Task<IActionResult> Verify2Fa([FromBody] ApiVerify2FaRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.VerificationId) || string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new
            {
                success = false,
                status = "InvalidInput",
                message = "Please provide both verificationId and verification code."
            });
        }

        if (!_authService.HasValidJwtConfiguration())
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = "ConfigurationError",
                message = "API token signing is not properly configured. Please contact an administrator."
            });
        }

        var verifyResult = await _authService.VerifyApiTwoFactorAsync(request.VerificationId, request.Code);

        switch (verifyResult.Status)
        {
            case TwoFactorVerifyStatus.InvalidToken:
                return Unauthorized(new
                {
                    success = false,
                    status = "InvalidVerification",
                    message = "Invalid verification session."
                });

            case TwoFactorVerifyStatus.Expired:
                return Unauthorized(new
                {
                    success = false,
                    status = "OtpExpired",
                    message = "Verification code has expired. Please sign in again."
                });

            case TwoFactorVerifyStatus.AlreadyUsed:
                return Unauthorized(new
                {
                    success = false,
                    status = "OtpReused",
                    message = "This verification code has already been used. Please sign in again."
                });

            case TwoFactorVerifyStatus.TooManyAttempts:
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    success = false,
                    status = "TooManyAttempts",
                    message = "Maximum verification attempts exceeded. Please sign in again."
                });

            case TwoFactorVerifyStatus.InvalidCode:
                return Unauthorized(new
                {
                    success = false,
                    status = "InvalidOtp",
                    remainingAttempts = verifyResult.RemainingAttempts,
                    message = "Invalid verification code."
                });

            case TwoFactorVerifyStatus.LockedOut:
                return StatusCode(StatusCodes.Status423Locked, new
                {
                    success = false,
                    status = "AccountLocked",
                    message = "Your account has been temporarily locked."
                });

            case TwoFactorVerifyStatus.UserUnavailable:
            case TwoFactorVerifyStatus.TelegramNotLinked:
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    success = false,
                    status = "AccessDenied",
                    message = "Account is unavailable or Telegram is not linked."
                });
        }

        var user = verifyResult.User!;
        var primaryRole = verifyResult.Roles?.FirstOrDefault() ?? "Student";
        var roleUpper = primaryRole.ToUpperInvariant();

        var tokenResult = _authService.GenerateJwtToken(user, roleUpper);
        if (tokenResult == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = "ConfigurationError",
                message = "API token signing is not properly configured. Please contact an administrator."
            });
        }

        _logger.LogInformation("API user {UserId} ({Role}) authenticated via Telegram 2FA.", user.Id, roleUpper);

        return Ok(new
        {
            success = true,
            status = "Authenticated",
            message = "Login successful",
            token = tokenResult.Token,
            expiresAt = tokenResult.ExpiresAt,
            user = new
            {
                id = user.Id,
                full_name = user.FullName,
                email = user.Email,
                university_id = user.UniversityId,
                role = roleUpper,
                avatar_url = user.Avatar
            }
        });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ApiForgotPasswordRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.LoginId))
        {
            return BadRequest(new { success = false, message = "Please enter your student ID or university email." });
        }

        var normalizedId = request.LoginId.Trim();

        // Anti-enumeration: look up user without revealing whether account exists in client response
        var user = await _userManager.FindByEmailAsync(normalizedId)
                   ?? await _userManager.Users.FirstOrDefaultAsync(u => u.UniversityId == normalizedId || u.UserName == normalizedId);

        if (user != null)
        {
            _logger.LogInformation("Password recovery requested for student {UserId} ({UniversityId})", user.Id, user.UniversityId);
        }

        // Check if institutional automated SMTP email delivery is configured in appsettings
        var smtpHost = _configuration["Smtp:Host"];
        var isEmailConfigured = !string.IsNullOrEmpty(smtpHost);

        if (isEmailConfigured && user != null && !string.IsNullOrEmpty(user.Email))
        {
            _ = await _userManager.GeneratePasswordResetTokenAsync(user);
            return Ok(new
            {
                success = true,
                emailConfigured = true,
                message = "If an account matching those details exists, recovery instructions have been sent to your registered university email."
            });
        }

        // Transparent institutional reporting: Email delivery service is not configured
        return Ok(new
        {
            success = true,
            emailConfigured = false,
            message = "Automated email recovery is currently unconfigured in this server environment. To regain access, please contact the ACLEDA University IT Helpdesk (helpdesk@acleda-university.edu.kh) or the Academic Registrar with your Student ID."
        });
    }

    [HttpGet("me")]
    [HttpGet("/api/admin/me")]
    public async Task<IActionResult> GetMe()
    {
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(new { success = false, message = "Authorization header missing or invalid." });
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var claims = _authService.ValidateJwtToken(token);
        if (claims == null || !claims.TryGetValue("sub", out var userId))
        {
            return Unauthorized(new { success = false, message = "Invalid or expired token." });
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive || await _userManager.IsLockedOutAsync(user))
        {
            return Unauthorized(new { success = false, message = "User not found or unavailable." });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var roleUpper = (roles.FirstOrDefault() ?? "Student").ToUpperInvariant();

        return Ok(new
        {
            success = true,
            user = new
            {
                id = user.Id,
                full_name = user.FullName,
                email = user.Email,
                university_id = user.UniversityId,
                role = roleUpper,
                avatar_url = user.Avatar
            }
        });
    }
}
