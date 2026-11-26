using System.Text.Json;
using AUBDigitalAcademy.Controllers;
using AUBDigitalAcademy.Controllers.Api;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Implementations;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AUBDigitalAcademy.Data;

public static class ApiAuth2FaIntegrationTestRunner
{
    private sealed record DispatchedCode(string UniversityId, string StudentName, string Code, string? ChatId);

    private sealed class SpyTelegramService : ITelegramService
    {
        public List<DispatchedCode> Dispatched { get; } = new();
        public bool ShouldSucceed { get; set; } = true;

        public Task<bool> SendVerificationCodeAsync(string universityId, string studentName, string verificationCode, string? customChatId = null)
        {
            if (!ShouldSucceed)
            {
                return Task.FromResult(false);
            }

            Dispatched.Add(new DispatchedCode(universityId, studentName, verificationCode, customChatId));
            return Task.FromResult(true);
        }

        public Task<(bool Success, string? BotUsername, string? ErrorMessage)> TestBotConnectivityAsync()
            => throw new NotSupportedException();

        public Task<bool> SendMessageAsync(long chatId, string text, string parseMode = "HTML", CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset = null, int timeoutSeconds = 20, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(string LinkUrl, string PlainToken, DateTime ExpiresAt)> GenerateLinkTokenAsync(string userId, string? clientIp = null)
            => throw new NotSupportedException();

        public Task<(bool Success, string Message, User? User)> ProcessLinkTokenAsync(string plainToken, long chatId, string? telegramUsername)
            => throw new NotSupportedException();

        public Task<bool> IsAccountLinkedAsync(string userId)
            => throw new NotSupportedException();
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public StubUrlHelper(ActionContext actionContext) => ActionContext = actionContext;
        public ActionContext ActionContext { get; }
        public string? Action(UrlActionContext actionContext) => $"/{actionContext.Controller}/{actionContext.Action}";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url.StartsWith('/');
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  API AUTH & TELEGRAM 2FA REGRESSION TEST SUITE  ");
        Console.WriteLine("=================================================");

        int passed = 0;
        int failed = 0;

        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var context = sp.GetRequiredService<ApplicationDbContext>();
        var userManager = sp.GetRequiredService<UserManager<User>>();
        var signInManager = sp.GetRequiredService<SignInManager<User>>();
        var dataProtection = sp.GetRequiredService<IDataProtectionProvider>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var validConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "IntegrationTestSuperSecretSigningKey_AtLeast32Bytes!",
                ["Jwt:Issuer"] = "AUBDigitalAcademy",
                ["Jwt:Audience"] = "AUBDigitalAcademy.Api",
                ["Jwt:ExpirationMinutes"] = "60"
            })
            .Build();

        var missingSecretConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "AUBDigitalAcademy",
                ["Jwt:Audience"] = "AUBDigitalAcademy.Api"
            })
            .Build();

        var shortSecretConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "too-short-secret",
                ["Jwt:Issuer"] = "AUBDigitalAcademy",
                ["Jwt:Audience"] = "AUBDigitalAcademy.Api"
            })
            .Build();

        var spyTelegram = new SpyTelegramService();

        AuthService BuildAuthService(IConfiguration config) => new AuthService(
            userManager,
            context,
            spyTelegram,
            dataProtection,
            config,
            loggerFactory.CreateLogger<AuthService>());

        AuthController BuildAuthController(IConfiguration config, string? bearerToken = null)
        {
            var authService = BuildAuthService(config);
            var controller = new AuthController(
                userManager,
                authService,
                config,
                loggerFactory.CreateLogger<AuthController>());

            var httpContext = new DefaultHttpContext { RequestServices = sp };
            if (!string.IsNullOrEmpty(bearerToken))
            {
                httpContext.Request.Headers.Authorization = $"Bearer {bearerToken}";
            }

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        AccountController BuildAccountController(IConfiguration config, string? cookieHeader = null)
        {
            var authService = BuildAuthService(config);
            var controller = new AccountController(
                signInManager,
                userManager,
                context,
                spyTelegram,
                authService,
                dataProtection,
                loggerFactory.CreateLogger<AccountController>());

            var httpContext = new DefaultHttpContext { RequestServices = sp };
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                httpContext.Request.Headers.Cookie = cookieHeader;
            }

            if (sp.GetService<IHttpContextAccessor>() is { } httpContextAccessor)
            {
                httpContextAccessor.HttpContext = httpContext;
            }

            var actionContext = new ActionContext(httpContext, new RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
            controller.ControllerContext = new ControllerContext(actionContext);
            controller.Url = new StubUrlHelper(actionContext);
            controller.TempData = new TempDataDictionary(httpContext, tempDataProvider);
            return controller;
        }

        static JsonElement ToJson(IActionResult actionResult, out int statusCode)
        {
            if (actionResult is ObjectResult obj)
            {
                statusCode = obj.StatusCode ?? StatusCodes.Status200OK;
                var json = JsonSerializer.Serialize(obj.Value);
                return JsonDocument.Parse(json).RootElement;
            }

            throw new InvalidOperationException($"Expected ObjectResult, got {actionResult.GetType().Name}");
        }

        void Assert(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                Console.WriteLine($"[PASS] {name}");
                passed++;
            }
            else
            {
                Console.WriteLine($"[FAIL] {name} {(string.IsNullOrEmpty(detail) ? "" : $"- {detail}")}");
                failed++;
            }
        }

        const string testPassword = "TestUser@2026!";
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createdUserIds = new List<string>();

        async Task<User> CreateTestUserAsync(string tag, bool telegramLinked, long? chatId = null, bool isActive = true)
        {
            var user = new User
            {
                UserName = $"api2fa_{tag}_{suffix}@aub.test",
                Email = $"api2fa_{tag}_{suffix}@aub.test",
                FullName = $"Test {tag}",
                UniversityId = $"T{suffix}{createdUserIds.Count + 1}",
                IsActive = isActive,
                IsTelegramVerified = telegramLinked,
                TelegramChatId = chatId,
                EmailConfirmed = true,
                LockoutEnabled = true
            };

            var res = await userManager.CreateAsync(user, testPassword);
            if (!res.Succeeded)
            {
                throw new InvalidOperationException("Failed creating test user: " + string.Join(", ", res.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(user, "Student");
            createdUserIds.Add(user.Id);
            return user;
        }

        try
        {
            var baseChatId = Random.Shared.NextInt64(8_000_000_000L, 9_900_000_000L);
            var linkedUser = await CreateTestUserAsync("linked", telegramLinked: true, chatId: baseChatId);
            var unlinkedUser = await CreateTestUserAsync("unlinked", telegramLinked: false, chatId: null);
            var lockoutUser = await CreateTestUserAsync("lockout", telegramLinked: true, chatId: baseChatId + 1);
            var secondUser = await CreateTestUserAsync("second", telegramLinked: true, chatId: baseChatId + 2);

            // 1. Valid credentials -> Telegram OTP sent -> pending 2FA response, no JWT
            spyTelegram.Dispatched.Clear();
            var controller1 = BuildAuthController(validConfig);
            var loginRes1 = await controller1.Login(new ApiLoginRequest { LoginId = linkedUser.Email, Password = testPassword });
            var json1 = ToJson(loginRes1, out int code1);
            var otp1 = spyTelegram.Dispatched.SingleOrDefault()?.Code;
            var verificationId1 = json1.TryGetProperty("verificationId", out var vidProp1) ? vidProp1.GetString() : null;
            Assert(
                "1. Valid credentials -> Telegram OTP sent -> pending 2FA response, no JWT",
                code1 == 200 &&
                json1.GetProperty("success").GetBoolean() &&
                json1.GetProperty("status").GetString() == "TwoFactorPending" &&
                json1.GetProperty("requiresTwoFactor").GetBoolean() &&
                !json1.TryGetProperty("token", out _) &&
                !string.IsNullOrWhiteSpace(verificationId1) &&
                spyTelegram.Dispatched.Count == 1 &&
                otp1?.Length == 6 &&
                !json1.GetRawText().Contains(otp1));

            // 2. Valid credentials but Telegram not linked -> access denied, no JWT
            spyTelegram.Dispatched.Clear();
            var loginRes2 = await BuildAuthController(validConfig).Login(new ApiLoginRequest { LoginId = unlinkedUser.Email, Password = testPassword });
            var json2 = ToJson(loginRes2, out int code2);
            Assert(
                "2. Valid credentials but Telegram not linked -> access denied, no JWT",
                code2 == 403 &&
                !json2.GetProperty("success").GetBoolean() &&
                json2.GetProperty("status").GetString() == "TelegramNotLinked" &&
                !json2.TryGetProperty("token", out _) &&
                spyTelegram.Dispatched.Count == 0);

            // 3. Invalid credentials -> authentication rejected (wrong user & wrong password match)
            spyTelegram.Dispatched.Clear();
            var wrongUserRes = await BuildAuthController(validConfig).Login(new ApiLoginRequest { LoginId = $"missing_{suffix}@aub.test", Password = testPassword });
            var wrongUserJson = ToJson(wrongUserRes, out int wrongUserCode);
            var wrongPassRes = await BuildAuthController(validConfig).Login(new ApiLoginRequest { LoginId = linkedUser.Email, Password = "WrongPassword!99" });
            var wrongPassJson = ToJson(wrongPassRes, out int wrongPassCode);
            await userManager.ResetAccessFailedCountAsync(linkedUser);
            Assert(
                "3. Invalid credentials -> authentication rejected uniformly, no JWT",
                wrongUserCode == 401 &&
                wrongPassCode == 401 &&
                wrongUserJson.GetProperty("status").GetString() == "InvalidCredentials" &&
                wrongPassJson.GetProperty("status").GetString() == "InvalidCredentials" &&
                wrongUserJson.GetProperty("message").GetString() == wrongPassJson.GetProperty("message").GetString() &&
                !wrongUserJson.TryGetProperty("token", out _) &&
                !wrongPassJson.TryGetProperty("token", out _) &&
                spyTelegram.Dispatched.Count == 0);

            // 4. Locked-out account -> authentication rejected even with valid password
            spyTelegram.Dispatched.Clear();
            int lockStatus = 0;
            for (int i = 0; i < 6; i++)
            {
                var attemptRes = await BuildAuthController(validConfig).Login(new ApiLoginRequest { LoginId = lockoutUser.Email, Password = "WrongPassword!99" });
                ToJson(attemptRes, out lockStatus);
                if (lockStatus == 423) break;
            }
            var validWhileLockedRes = await BuildAuthController(validConfig).Login(new ApiLoginRequest { LoginId = lockoutUser.Email, Password = testPassword });
            var validWhileLockedJson = ToJson(validWhileLockedRes, out int validWhileLockedCode);
            Assert(
                "4. Locked-out account -> authentication rejected with 423, no JWT, no OTP",
                lockStatus == 423 &&
                validWhileLockedCode == 423 &&
                validWhileLockedJson.GetProperty("status").GetString() == "AccountLocked" &&
                !validWhileLockedJson.TryGetProperty("token", out _) &&
                spyTelegram.Dispatched.Count == 0);

            // 5. Valid OTP -> JWT issued & /api/Auth/me succeeds
            var verifyOkRes = await BuildAuthController(validConfig).Verify2Fa(new ApiVerify2FaRequest
            {
                VerificationId = verificationId1,
                Code = otp1
            });
            var verifyOkJson = ToJson(verifyOkRes, out int verifyOkCode);
            var jwt = verifyOkJson.TryGetProperty("token", out var tokProp) ? tokProp.GetString() : null;
            var meRes = await BuildAuthController(validConfig, jwt).GetMe();
            var meJson = ToJson(meRes, out int meCode);
            Assert(
                "5. Valid OTP -> JWT issued and /api/Auth/me authenticates user",
                verifyOkCode == 200 &&
                verifyOkJson.GetProperty("success").GetBoolean() &&
                verifyOkJson.GetProperty("status").GetString() == "Authenticated" &&
                !string.IsNullOrWhiteSpace(jwt) &&
                jwt.Split('.').Length == 3 &&
                meCode == 200 &&
                meJson.GetProperty("user").GetProperty("id").GetString() == linkedUser.Id);

            // 8 (check reuse of already-verified OTP from test 5)
            var reuseRes = await BuildAuthController(validConfig).Verify2Fa(new ApiVerify2FaRequest
            {
                VerificationId = verificationId1,
                Code = otp1
            });
            var reuseJson = ToJson(reuseRes, out int reuseCode);
            Assert(
                "8. Reused OTP -> JWT not issued",
                reuseCode == 401 &&
                reuseJson.GetProperty("status").GetString() == "OtpReused" &&
                !reuseJson.TryGetProperty("token", out _));

            // 6. Invalid OTP -> JWT not issued
            spyTelegram.Dispatched.Clear();
            var loginForInvalid = await BuildAuthController(validConfig).Login(new ApiLoginRequest { LoginId = linkedUser.Email, Password = testPassword });
            var vidInvalid = ToJson(loginForInvalid, out _).GetProperty("verificationId").GetString()!;
            var realOtpInvalid = spyTelegram.Dispatched.Single().Code;
            var wrongOtp = realOtpInvalid == "000000" ? "999999" : "000000";
            var invalidOtpRes = await BuildAuthController(validConfig).Verify2Fa(new ApiVerify2FaRequest { VerificationId = vidInvalid, Code = wrongOtp });
            var invalidOtpJson = ToJson(invalidOtpRes, out int invalidOtpCode);
            Assert(
                "6. Invalid OTP -> JWT not issued",
                invalidOtpCode == 401 &&
                invalidOtpJson.GetProperty("status").GetString() == "InvalidOtp" &&
                invalidOtpJson.GetProperty("remainingAttempts").GetInt32() == 4 &&
                !invalidOtpJson.TryGetProperty("token", out _));

            // 7. Expired OTP -> JWT not issued
            var latestReq = await context.TelegramVerificationRequests
                .Where(r => r.UserId == linkedUser.Id && !r.IsUsed)
                .OrderByDescending(r => r.CreatedAt)
                .FirstAsync();
            latestReq.ExpiresAt = DateTime.UtcNow.AddMinutes(-2);
            await context.SaveChangesAsync();
            var expiredRes = await BuildAuthController(validConfig).Verify2Fa(new ApiVerify2FaRequest { VerificationId = vidInvalid, Code = realOtpInvalid });
            var expiredJson = ToJson(expiredRes, out int expiredCode);
            Assert(
                "7. Expired OTP -> JWT not issued",
                expiredCode == 401 &&
                expiredJson.GetProperty("status").GetString() == "OtpExpired" &&
                !expiredJson.TryGetProperty("token", out _));

            // 9. Too many OTP attempts -> verification blocked even if correct OTP submitted next
            spyTelegram.Dispatched.Clear();
            var loginForAttempts = await BuildAuthController(validConfig).Login(new ApiLoginRequest { LoginId = linkedUser.Email, Password = testPassword });
            var vidAttempts = ToJson(loginForAttempts, out _).GetProperty("verificationId").GetString()!;
            var realOtpAttempts = spyTelegram.Dispatched.Single().Code;
            var badOtpAttempts = realOtpAttempts == "000000" ? "999999" : "000000";
            int fifthStatus = 0;
            string? fifthStatusStr = null;
            for (int i = 1; i <= 5; i++)
            {
                var r = await BuildAuthController(validConfig).Verify2Fa(new ApiVerify2FaRequest { VerificationId = vidAttempts, Code = badOtpAttempts });
                var j = ToJson(r, out fifthStatus);
                fifthStatusStr = j.GetProperty("status").GetString();
            }
            var afterLimitRes = await BuildAuthController(validConfig).Verify2Fa(new ApiVerify2FaRequest { VerificationId = vidAttempts, Code = realOtpAttempts });
            var afterLimitJson = ToJson(afterLimitRes, out int afterLimitCode);
            Assert(
                "9. Too many OTP attempts -> verification blocked and correct OTP rejected afterward",
                fifthStatus == 429 &&
                fifthStatusStr == "TooManyAttempts" &&
                (afterLimitCode == 401 || afterLimitCode == 429) &&
                !afterLimitJson.TryGetProperty("token", out _));

            // 10. Verification identifier belonging to another user/session -> rejected
            spyTelegram.Dispatched.Clear();
            var loginA = await BuildAuthController(validConfig).Login(new ApiLoginRequest { LoginId = linkedUser.Email, Password = testPassword });
            var vidA = ToJson(loginA, out _).GetProperty("verificationId").GetString()!;
            var otpA = spyTelegram.Dispatched.Single().Code;

            spyTelegram.Dispatched.Clear();
            var loginB = await BuildAuthController(validConfig).Login(new ApiLoginRequest { LoginId = secondUser.Email, Password = testPassword });
            var vidB = ToJson(loginB, out _).GetProperty("verificationId").GetString()!;
            var otpB = spyTelegram.Dispatched.Single().Code;
            if (otpA == otpB) otpA = otpA == "111111" ? "222222" : "111111"; // ensure distinct codes

            // Cross-user code mismatch & tampered/forged verificationId binding
            var crossUserRes = await BuildAuthController(validConfig).Verify2Fa(new ApiVerify2FaRequest { VerificationId = vidB, Code = otpA });
            var crossUserJson = ToJson(crossUserRes, out int crossUserCode);

            var reqA = await context.TelegramVerificationRequests
                .Where(r => r.UserId == linkedUser.Id && !r.IsUsed)
                .OrderByDescending(r => r.CreatedAt)
                .FirstAsync();
            var protector = dataProtection.CreateProtector("AUBDigitalAcademy.ApiTelegram2FA.v1").ToTimeLimitedDataProtector();
            var mismatchedSessionVid = protector.Protect($"{reqA.Id}|{secondUser.Id}|{Guid.NewGuid():N}", TimeSpan.FromMinutes(10));
            var mismatchedRes = await BuildAuthController(validConfig).Verify2Fa(new ApiVerify2FaRequest { VerificationId = mismatchedSessionVid, Code = reqA.Code });
            var mismatchedJson = ToJson(mismatchedRes, out int mismatchedCode);

            Assert(
                "10. Verification identifier belonging to another user/session -> rejected",
                crossUserCode == 401 &&
                !crossUserJson.TryGetProperty("token", out _) &&
                mismatchedCode == 401 &&
                mismatchedJson.GetProperty("status").GetString() == "InvalidVerification" &&
                !mismatchedJson.TryGetProperty("token", out _));

            // 11. Missing or weak JWT signing configuration -> no token issued and safe failure
            spyTelegram.Dispatched.Clear();
            var missingLoginRes = await BuildAuthController(missingSecretConfig).Login(new ApiLoginRequest { LoginId = linkedUser.Email, Password = testPassword });
            var missingLoginJson = ToJson(missingLoginRes, out int missingLoginCode);
            var shortLoginRes = await BuildAuthController(shortSecretConfig).Login(new ApiLoginRequest { LoginId = linkedUser.Email, Password = testPassword });
            var shortLoginJson = ToJson(shortLoginRes, out int shortLoginCode);
            var missingVerifyRes = await BuildAuthController(missingSecretConfig).Verify2Fa(new ApiVerify2FaRequest { VerificationId = vidA, Code = reqA.Code });
            var missingVerifyJson = ToJson(missingVerifyRes, out int missingVerifyCode);
            Assert(
                "11. Missing/weak JWT signing configuration -> no OTP sent, no token issued, safe 500 failure",
                missingLoginCode == 500 &&
                shortLoginCode == 500 &&
                missingVerifyCode == 500 &&
                missingLoginJson.GetProperty("status").GetString() == "ConfigurationError" &&
                shortLoginJson.GetProperty("status").GetString() == "ConfigurationError" &&
                missingVerifyJson.GetProperty("status").GetString() == "ConfigurationError" &&
                !missingVerifyJson.TryGetProperty("token", out _) &&
                spyTelegram.Dispatched.Count == 0);

            // 12. Existing Web Login and Telegram 2FA still work
            spyTelegram.Dispatched.Clear();
            // Clear prior unused requests so Web Login dispatches a fresh OTP we can observe
            var staleRequests = await context.TelegramVerificationRequests
                .Where(r => r.UserId == linkedUser.Id && !r.IsUsed)
                .ToListAsync();
            foreach (var sr in staleRequests)
            {
                sr.IsUsed = true;
                sr.UsedAt = DateTime.UtcNow;
            }
            await context.SaveChangesAsync();

            var webLoginController = BuildAccountController(validConfig);
            var webLoginAction = await webLoginController.Login(new LoginViewModel
            {
                LoginId = linkedUser.Email!,
                Password = testPassword,
                RememberMe = false
            });

            var webRedirect1 = webLoginAction as RedirectToActionResult;
            var pendingCookieHeader = webLoginController.Response.Headers.SetCookie.ToString();
            var webOtp = spyTelegram.Dispatched.SingleOrDefault()?.Code;

            // Extract cookie value from Set-Cookie header ("AUB_PendingVerification=<value>; ...")
            string? cookiePair = null;
            foreach (var rawCookie in webLoginController.Response.Headers.SetCookie)
            {
                if (rawCookie != null && rawCookie.StartsWith("AUB_PendingVerification=", StringComparison.Ordinal))
                {
                    cookiePair = rawCookie.Split(';', 2)[0];
                    break;
                }
            }

            var webVerifyController = BuildAccountController(validConfig, cookiePair);
            var webVerifyAction = await webVerifyController.VerifyTelegram(new VerifyTelegramViewModel
            {
                UserId = linkedUser.Id,
                Code = webOtp ?? string.Empty
            });
            var webRedirect2 = webVerifyAction as RedirectResult;
            Assert(
                "12. Existing Web Login and Telegram 2FA still work end-to-end",
                webRedirect1?.ActionName == "VerifyTelegram" &&
                !string.IsNullOrEmpty(cookiePair) &&
                !string.IsNullOrEmpty(webOtp) &&
                webRedirect2 != null &&
                webRedirect2.Url.Contains("/Student", StringComparison.OrdinalIgnoreCase),
                $"r1={webRedirect1?.ActionName}, hasCookie={!string.IsNullOrEmpty(cookiePair)}, hasOtp={!string.IsNullOrEmpty(webOtp)}, r2Type={webVerifyAction?.GetType().Name}, r2Url={webRedirect2?.Url}");
        }
        finally
        {
            foreach (var uid in createdUserIds)
            {
                var reqs = await context.TelegramVerificationRequests.Where(r => r.UserId == uid).ToListAsync();
                context.TelegramVerificationRequests.RemoveRange(reqs);
                var links = await context.TelegramLinkTokens.Where(l => l.UserId == uid).ToListAsync();
                context.TelegramLinkTokens.RemoveRange(links);
                await context.SaveChangesAsync();

                var u = await userManager.FindByIdAsync(uid);
                if (u != null)
                {
                    await userManager.DeleteAsync(u);
                }
            }
        }

        Console.WriteLine("=================================================");
        Console.WriteLine($"  SUMMARY: {passed} passed, {failed} failed");
        Console.WriteLine("=================================================");

        if (failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }
}
