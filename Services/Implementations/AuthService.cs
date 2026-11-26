using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AUBDigitalAcademy.Services.Implementations;

public class AuthService : IAuthService
{
    private const string TwoFactorPurpose = "AUBDigitalAcademy.ApiTelegram2FA.v1";
    private const int OtpValidityMinutes = 10;
    private const int DefaultJwtExpirationMinutes = 60;
    private const int MinimumJwtSecretBytes = 32;

    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ITelegramService _telegramService;
    private readonly ITimeLimitedDataProtector _twoFactorProtector;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<User> userManager,
        ApplicationDbContext context,
        ITelegramService telegramService,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _context = context;
        _telegramService = telegramService;
        _twoFactorProtector = dataProtectionProvider
            .CreateProtector(TwoFactorPurpose)
            .ToTimeLimitedDataProtector();
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<User?> FindByLoginIdAsync(string loginId)
    {
        if (string.IsNullOrWhiteSpace(loginId))
        {
            return null;
        }

        var trimmed = loginId.Trim();
        return await _userManager.FindByEmailAsync(trimmed)
               ?? await _userManager.Users.FirstOrDefaultAsync(u => u.UniversityId == trimmed)
               ?? await _userManager.FindByNameAsync(trimmed);
    }

    public async Task<CredentialCheckResult> AuthenticateCredentialsAsync(string loginId, string password)
    {
        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrEmpty(password))
        {
            return new CredentialCheckResult(CredentialCheckStatus.InvalidCredentials);
        }

        var user = await FindByLoginIdAsync(loginId);
        if (user == null)
        {
            return new CredentialCheckResult(CredentialCheckStatus.InvalidCredentials);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return new CredentialCheckResult(CredentialCheckStatus.LockedOut, user);
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, password);
        if (!passwordValid)
        {
            await _userManager.AccessFailedAsync(user);
            if (await _userManager.IsLockedOutAsync(user))
            {
                return new CredentialCheckResult(CredentialCheckStatus.LockedOut, user);
            }

            return new CredentialCheckResult(CredentialCheckStatus.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            return new CredentialCheckResult(CredentialCheckStatus.InactiveAccount, user);
        }

        if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue)
        {
            return new CredentialCheckResult(CredentialCheckStatus.TelegramNotLinked, user);
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return new CredentialCheckResult(CredentialCheckStatus.Success, user, roles);
    }

    public async Task<TelegramVerificationRequest> EnsureWebVerificationCodeDispatchedAsync(User user)
    {
        var now = DateTime.UtcNow;
        var activeRequest = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed && r.ExpiresAt > now)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (activeRequest != null)
        {
            return activeRequest;
        }

        var code = GenerateSixDigitCode();
        var request = new TelegramVerificationRequest
        {
            UserId = user.Id,
            UniversityId = user.UniversityId ?? user.Email ?? user.UserName ?? user.Id,
            Code = code,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(OtpValidityMinutes)
        };

        _context.TelegramVerificationRequests.Add(request);
        await _context.SaveChangesAsync();

        var targetChatId = user.TelegramChatId?.ToString();
        await _telegramService.SendVerificationCodeAsync(
            request.UniversityId,
            user.FullName,
            code,
            targetChatId);

        return request;
    }

    public async Task<ApiTwoFactorChallengeResult> CreateApiTwoFactorChallengeAsync(User user)
    {
        if (!HasValidJwtConfiguration())
        {
            return new ApiTwoFactorChallengeResult(TwoFactorDispatchStatus.InvalidConfiguration);
        }

        if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue)
        {
            return new ApiTwoFactorChallengeResult(TwoFactorDispatchStatus.DeliveryFailed);
        }

        var now = DateTime.UtcNow;
        var existingActive = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed)
            .ToListAsync();

        foreach (var prior in existingActive)
        {
            prior.IsUsed = true;
            prior.UsedAt = now;
        }

        var code = GenerateSixDigitCode();
        var request = new TelegramVerificationRequest
        {
            UserId = user.Id,
            UniversityId = user.UniversityId ?? user.Email ?? user.UserName ?? user.Id,
            Code = code,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(OtpValidityMinutes),
            IsUsed = false,
            AttemptsCount = 0,
            MaxAttempts = 5
        };

        _context.TelegramVerificationRequests.Add(request);
        await _context.SaveChangesAsync();

        var sent = await _telegramService.SendVerificationCodeAsync(
            request.UniversityId,
            user.FullName,
            code,
            user.TelegramChatId.Value.ToString());

        if (!sent)
        {
            request.IsUsed = true;
            request.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return new ApiTwoFactorChallengeResult(TwoFactorDispatchStatus.DeliveryFailed);
        }

        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var payload = $"{request.Id}|{user.Id}|{nonce}";
        var verificationId = _twoFactorProtector.Protect(payload, TimeSpan.FromMinutes(OtpValidityMinutes));

        return new ApiTwoFactorChallengeResult(
            TwoFactorDispatchStatus.Sent,
            verificationId,
            request.ExpiresAt,
            OtpValidityMinutes * 60);
    }

    public async Task<TwoFactorVerifyResult> VerifyApiTwoFactorAsync(string verificationId, string code)
    {
        if (string.IsNullOrWhiteSpace(verificationId) || string.IsNullOrWhiteSpace(code))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.InvalidToken);
        }

        string payload;
        try
        {
            payload = _twoFactorProtector.Unprotect(verificationId.Trim(), out _);
        }
        catch (CryptographicException ex)
        {
            if (ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
            {
                return new TwoFactorVerifyResult(TwoFactorVerifyStatus.Expired);
            }

            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.InvalidToken);
        }

        var parts = payload.Split('|');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var requestId) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.InvalidToken);
        }

        var expectedUserId = parts[1];
        var request = await _context.TelegramVerificationRequests
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null || !string.Equals(request.UserId, expectedUserId, StringComparison.Ordinal))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.InvalidToken);
        }

        var user = await _userManager.FindByIdAsync(expectedUserId);
        if (user == null || !user.IsActive)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.UserUnavailable);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.LockedOut, user);
        }

        if (!user.IsTelegramVerified || !user.TelegramChatId.HasValue)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.TelegramNotLinked, user);
        }

        return await ValidateAndConsumeOtpRequestAsync(request, user, code);
    }

    public async Task<TwoFactorVerifyResult> VerifyWebTwoFactorAsync(string userId, string code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.InvalidToken);
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.UserUnavailable);
        }

        var request = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (request == null)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.AlreadyUsed, user);
        }

        return await ValidateAndConsumeOtpRequestAsync(request, user, code);
    }

    public bool HasValidJwtConfiguration()
    {
        return GetJwtSigningKeyBytes() != null;
    }

    public JwtTokenResult? GenerateJwtToken(User user, string role)
    {
        var keyBytes = GetJwtSigningKeyBytes();
        if (keyBytes == null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var expirationMinutes = GetJwtExpirationMinutes();
        var expiresAt = now.AddMinutes(expirationMinutes);

        var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));

        var payloadObj = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["email"] = user.Email ?? string.Empty,
            ["role"] = role.ToUpperInvariant(),
            ["name"] = user.FullName,
            ["university_id"] = user.UniversityId ?? string.Empty,
            ["iss"] = GetJwtIssuer(),
            ["aud"] = GetJwtAudience(),
            ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            ["nbf"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            ["exp"] = new DateTimeOffset(expiresAt).ToUnixTimeSeconds()
        };

        var payloadJson = JsonSerializer.Serialize(payloadObj);
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signingInput = $"{header}.{payload}";
        using var hmac = new HMACSHA256(keyBytes);
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        var signature = Base64UrlEncode(signatureBytes);

        return new JwtTokenResult($"{signingInput}.{signature}", expiresAt);
    }

    public Dictionary<string, string>? ValidateJwtToken(string token)
    {
        var keyBytes = GetJwtSigningKeyBytes();
        if (keyBytes == null || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            using var headerDoc = JsonDocument.Parse(headerJson);
            if (!headerDoc.RootElement.TryGetProperty("alg", out var algProp) ||
                !string.Equals(algProp.GetString(), "HS256", StringComparison.Ordinal))
            {
                return null;
            }

            var signingInput = $"{parts[0]}.{parts[1]}";
            using var hmac = new HMACSHA256(keyBytes);
            var expectedSig = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
            var providedSig = Base64UrlDecode(parts[2]);

            if (providedSig.Length != expectedSig.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedSig, providedSig))
            {
                return null;
            }

            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            const long clockSkewSeconds = 30;

            if (!root.TryGetProperty("iss", out var issProp) ||
                !string.Equals(issProp.GetString(), GetJwtIssuer(), StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("aud", out var audProp) ||
                !string.Equals(audProp.GetString(), GetJwtAudience(), StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("exp", out var expProp) ||
                expProp.GetInt64() <= nowUnix - clockSkewSeconds)
            {
                return null;
            }

            if (root.TryGetProperty("nbf", out var nbfProp) &&
                nbfProp.GetInt64() > nowUnix + clockSkewSeconds)
            {
                return null;
            }

            if (!root.TryGetProperty("sub", out var subProp) ||
                string.IsNullOrWhiteSpace(subProp.GetString()))
            {
                return null;
            }

            var claims = new Dictionary<string, string>();
            foreach (var prop in root.EnumerateObject())
            {
                claims[prop.Name] = prop.Value.ToString();
            }

            return claims;
        }
        catch
        {
            return null;
        }
    }

    private async Task<TwoFactorVerifyResult> ValidateAndConsumeOtpRequestAsync(
        TelegramVerificationRequest request,
        User user,
        string code)
    {
        if (request.IsUsed)
        {
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.AlreadyUsed, user);
        }

        if (DateTime.UtcNow > request.ExpiresAt)
        {
            request.IsUsed = true;
            request.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.Expired, user);
        }

        if (request.AttemptsCount >= request.MaxAttempts)
        {
            request.IsUsed = true;
            request.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return new TwoFactorVerifyResult(TwoFactorVerifyStatus.TooManyAttempts, user);
        }

        var normalizedInput = code.Trim();
        var inputHash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedInput));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Code));

        if (!CryptographicOperations.FixedTimeEquals(inputHash, expectedHash))
        {
            request.AttemptsCount++;
            if (request.AttemptsCount >= request.MaxAttempts)
            {
                request.IsUsed = true;
                request.UsedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return new TwoFactorVerifyResult(TwoFactorVerifyStatus.TooManyAttempts, user);
            }

            await _context.SaveChangesAsync();
            return new TwoFactorVerifyResult(
                TwoFactorVerifyStatus.InvalidCode,
                user,
                RemainingAttempts: request.MaxAttempts - request.AttemptsCount);
        }

        var now = DateTime.UtcNow;
        request.IsUsed = true;
        request.UsedAt = now;

        var siblingUnused = await _context.TelegramVerificationRequests
            .Where(r => r.UserId == user.Id && !r.IsUsed && r.Id != request.Id)
            .ToListAsync();

        foreach (var sibling in siblingUnused)
        {
            sibling.IsUsed = true;
            sibling.UsedAt = now;
        }

        await _context.SaveChangesAsync();
        await _userManager.ResetAccessFailedCountAsync(user);

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return new TwoFactorVerifyResult(TwoFactorVerifyStatus.Verified, user, roles);
    }

    private byte[]? GetJwtSigningKeyBytes()
    {
        var secret = _configuration["Jwt:Secret"]
            ?? _configuration["Jwt:Key"]
            ?? _configuration["JWT_SECRET"];

        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("JWT signing key is not configured.");
            return null;
        }

        var keyBytes = Encoding.UTF8.GetBytes(secret.Trim());
        if (keyBytes.Length < MinimumJwtSecretBytes)
        {
            _logger.LogWarning("Configured JWT signing key does not meet minimum length requirements.");
            return null;
        }

        return keyBytes;
    }

    private string GetJwtIssuer()
    {
        var issuer = _configuration["Jwt:Issuer"];
        return string.IsNullOrWhiteSpace(issuer) ? "AUBDigitalAcademy" : issuer.Trim();
    }

    private string GetJwtAudience()
    {
        var audience = _configuration["Jwt:Audience"];
        return string.IsNullOrWhiteSpace(audience) ? "AUBDigitalAcademy.Api" : audience.Trim();
    }

    private int GetJwtExpirationMinutes()
    {
        if (int.TryParse(_configuration["Jwt:ExpirationMinutes"], out var minutes) && minutes > 0)
        {
            return minutes;
        }

        return DefaultJwtExpirationMinutes;
    }

    private static string GenerateSixDigitCode()
    {
        return RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        return Convert.FromBase64String(base64);
    }
}
