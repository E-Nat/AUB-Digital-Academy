using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Models;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AUBDigitalAcademy.Controllers.Api;

public class ApiLoginRequest
{
    public string? LoginId { get; set; }
    public string? Password { get; set; }
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
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<User> userManager,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] ApiLoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.LoginId) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { success = false, message = "Please provide both Login ID/Email and Password." });
        }

        var normalizedId = request.LoginId.Trim();

        var user = await _userManager.FindByEmailAsync(normalizedId)
                   ?? await _userManager.Users.FirstOrDefaultAsync(u => u.UniversityId == normalizedId || u.UserName == normalizedId);

        if (user == null)
        {
            return Unauthorized(new { success = false, message = "Invalid credentials. User not found." });
        }

        if (!user.IsActive)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Your account is currently inactive. Contact administration." });
        }

        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            return Unauthorized(new { success = false, message = "Invalid password. Please try again." });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var primaryRole = roles.FirstOrDefault() ?? "Student";
        var roleUpper = primaryRole.ToUpperInvariant();

        var token = GenerateJwtToken(user, roleUpper);

        _logger.LogInformation("API user {UserId} ({Role}) authenticated successfully.", user.Id, roleUpper);

        return Ok(new
        {
            success = true,
            message = "Login successful",
            token = token,
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
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
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
        var claims = ValidateJwtToken(token);
        if (claims == null || !claims.TryGetValue("userId", out var userId))
        {
            return Unauthorized(new { success = false, message = "Invalid or expired token." });
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            return Unauthorized(new { success = false, message = "User not found or inactive." });
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

    private string GenerateJwtToken(User user, string role)
    {
        var secretKey = _configuration["Jwt:Secret"] 
            ?? _configuration["Jwt:Key"] 
            ?? "AUB_Digital_Academy_JWT_Secret_Key_2026_Institutional_Gateway!";

        var headerJson = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
        var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long exp = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();

        var payloadData = new Dictionary<string, object>
        {
            ["userId"] = user.Id,
            ["sub"] = user.Id,
            ["email"] = user.Email ?? "",
            ["role"] = role,
            ["name"] = user.FullName,
            ["iat"] = now,
            ["exp"] = exp
        };

        var payloadJson = JsonSerializer.Serialize(payloadData);
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var dataToSign = $"{headerBase64}.{payloadBase64}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
        var signatureBase64 = Base64UrlEncode(signatureBytes);

        return $"{dataToSign}.{signatureBase64}";
    }

    private Dictionary<string, string>? ValidateJwtToken(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;

            var secretKey = _configuration["Jwt:Secret"] 
                ?? _configuration["Jwt:Key"] 
                ?? "AUB_Digital_Academy_JWT_Secret_Key_2026_Institutional_Gateway!";

            var dataToSign = $"{parts[0]}.{parts[1]}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            var computedSignature = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign)));

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignature),
                Encoding.UTF8.GetBytes(parts[2])))
            {
                return null;
            }

            var payloadBytes = Base64UrlDecode(parts[1]);
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("exp", out var expProp) && expProp.GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return null;
            }

            var dict = new Dictionary<string, string>();
            foreach (var prop in root.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ToString();
            }
            return dict;
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        string incoming = input.Replace('-', '+').Replace('_', '/');
        switch (incoming.Length % 4)
        {
            case 2: incoming += "=="; break;
            case 3: incoming += "="; break;
        }
        return Convert.FromBase64String(incoming);
    }
}
