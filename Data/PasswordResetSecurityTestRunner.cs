using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AUBDigitalAcademy.Controllers;
using AUBDigitalAcademy.Controllers.Api;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Implementations;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AUBDigitalAcademy.Data;

public static class PasswordResetSecurityTestRunner
{
    private sealed record DispatchedCode(string Kind, string UniversityId, string Name, string Code, string? ChatId);

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

            Dispatched.Add(new DispatchedCode("Login", universityId, studentName, verificationCode, customChatId));
            return Task.FromResult(true);
        }

        public Task<bool> SendPasswordResetCodeAsync(string universityId, string fullName, string verificationCode, string customChatId)
        {
            if (!ShouldSucceed || string.IsNullOrWhiteSpace(customChatId))
            {
                return Task.FromResult(false);
            }

            Dispatched.Add(new DispatchedCode("Reset", universityId, fullName, verificationCode, customChatId));
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

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"ok\":false,\"description\":\"Simulated Telegram outage\"}")
            });
        }
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "AUBDigitalAcademy";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public StubUrlHelper(ActionContext actionContext) => ActionContext = actionContext;
        public ActionContext ActionContext { get; }
        public string? Action(UrlActionContext actionContext) => $"/{actionContext.Controller}/{actionContext.Action}";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Messages);

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly ConcurrentQueue<string> _messages;

            public CapturingLogger(string category, ConcurrentQueue<string> messages)
            {
                _category = category;
                _messages = messages;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _messages.Enqueue($"[{logLevel}] {_category}: {formatter(state, exception)} {exception}");
            }
        }
    }

    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("      PASSWORD RESET SECURITY INTEGRATION TEST SUITE             ");
        Console.WriteLine("=================================================================");

        int passed = 0;
        int failed = 0;

        void Assert(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                Console.WriteLine($"[PASS] {name}");
                passed++;
            }
            else
            {
                Console.WriteLine($"[FAIL] {name}{(string.IsNullOrEmpty(detail) ? "" : $" - {detail}")}");
                failed++;
            }
        }

        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var context = sp.GetRequiredService<ApplicationDbContext>();
        var userManager = sp.GetRequiredService<UserManager<User>>();
        var dataProtection = sp.GetRequiredService<IDataProtectionProvider>();
        var cookieOpts = sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var appCookieName = cookieOpts.Get(IdentityConstants.ApplicationScheme).Cookie.Name ?? ".AspNetCore.Identity.Application";

        var jwtConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "PasswordResetIntegrationTestSecretKey_AtLeast32Bytes!",
                ["Jwt:Issuer"] = "AUBDigitalAcademy",
                ["Jwt:Audience"] = "AUBDigitalAcademy.Api",
                ["Jwt:ExpirationMinutes"] = "60",
                ["Smtp:Host"] = "smtp.aub.test"
            })
            .Build();

        var spyTelegram = new SpyTelegramService();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createdUserIds = new List<string>();
        const string initialPassword = "OldPass@2026";
        const string updatedPassword = "NewPass@2026";

        async Task<(TResult Result, IHeaderDictionary Headers, ModelStateDictionary ModelState, ITempDataDictionary TempData, int StatusCode)>
            InvokeAccountAsync<TResult>(
                ITelegramService telegramImpl,
                Func<AccountController, Task<TResult>> action,
                string? cookieHeader = null,
                string? remoteIp = null,
                ILoggerFactory? customLoggerFactory = null)
        {
            using var reqScope = serviceProvider.CreateScope();
            var rsp = reqScope.ServiceProvider;
            var reqCtx = rsp.GetRequiredService<ApplicationDbContext>();
            var reqUm = rsp.GetRequiredService<UserManager<User>>();
            var reqSm = rsp.GetRequiredService<SignInManager<User>>();
            var reqDp = rsp.GetRequiredService<IDataProtectionProvider>();
            var reqLf = customLoggerFactory ?? rsp.GetRequiredService<ILoggerFactory>();
            var reqTdp = rsp.GetRequiredService<ITempDataProvider>();

            var authService = new AuthService(
                reqUm, reqCtx, telegramImpl, reqDp, jwtConfig, reqLf.CreateLogger<AuthService>());
            var controller = new AccountController(
                reqSm, reqUm, reqCtx, telegramImpl, authService, reqDp, reqLf.CreateLogger<AccountController>());

            var httpContext = new DefaultHttpContext { RequestServices = rsp };
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");
            if (!string.IsNullOrEmpty(remoteIp))
            {
                httpContext.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            }
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                httpContext.Request.Headers.Cookie = cookieHeader;
            }
            if (rsp.GetService<IHttpContextAccessor>() is { } accessor)
            {
                accessor.HttpContext = httpContext;
            }

            var actionContext = new ActionContext(httpContext, new RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
            controller.ControllerContext = new ControllerContext(actionContext);
            controller.Url = new StubUrlHelper(actionContext);
            controller.TempData = new TempDataDictionary(httpContext, reqTdp);

            var res = await action(controller);
            return (res, controller.Response.Headers, controller.ModelState, controller.TempData, controller.Response.StatusCode);
        }

        async Task<(IActionResult Result, int StatusCode, JsonElement Json)> InvokeApiForgotAsync(
            string loginId,
            string? remoteIp = null)
        {
            using var reqScope = serviceProvider.CreateScope();
            var rsp = reqScope.ServiceProvider;
            var reqCtx = rsp.GetRequiredService<ApplicationDbContext>();
            var reqUm = rsp.GetRequiredService<UserManager<User>>();
            var reqDp = rsp.GetRequiredService<IDataProtectionProvider>();
            var reqLf = rsp.GetRequiredService<ILoggerFactory>();

            var authService = new AuthService(
                reqUm, reqCtx, spyTelegram, reqDp, jwtConfig, reqLf.CreateLogger<AuthService>());
            var controller = new AuthController(
                reqUm, authService, jwtConfig, reqLf.CreateLogger<AuthController>());

            var httpContext = new DefaultHttpContext { RequestServices = rsp };
            if (!string.IsNullOrEmpty(remoteIp))
            {
                httpContext.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            }
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var res = await controller.ForgotPassword(new ApiForgotPasswordRequest { LoginId = loginId });
            var obj = (ObjectResult)res;
            var statusCode = obj.StatusCode ?? StatusCodes.Status200OK;
            var json = JsonDocument.Parse(JsonSerializer.Serialize(obj.Value)).RootElement;
            return (res, statusCode, json);
        }

        async Task<AuthenticateResult> AuthenticateAppCookieAsync(string? appCookie)
        {
            using var reqScope = serviceProvider.CreateScope();
            var rsp = reqScope.ServiceProvider;
            var authCtx = new DefaultHttpContext { RequestServices = rsp };
            authCtx.Request.Scheme = "https";
            authCtx.Request.Host = new HostString("localhost");
            authCtx.Request.Headers.Cookie = appCookie ?? string.Empty;
            if (rsp.GetService<IHttpContextAccessor>() is { } accessor)
            {
                accessor.HttpContext = authCtx;
            }
            return await authCtx.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        }

        static string? ExtractSetCookiePair(IHeaderDictionary headers, string cookieName)
        {
            var prefix = cookieName + "=";
            string? matched = null;
            foreach (var raw in headers.SetCookie)
            {
                if (!string.IsNullOrEmpty(raw) && raw.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var pair = raw.Split(';', 2)[0];
                    var value = pair[prefix.Length..];
                    matched = string.IsNullOrEmpty(value) ? null : pair;
                }
            }
            return matched;
        }

        static bool HasSetCookieDeletion(IHeaderDictionary headers, string cookieName)
        {
            var prefix = cookieName + "=";
            foreach (var raw in headers.SetCookie)
            {
                if (!string.IsNullOrEmpty(raw) &&
                    raw.StartsWith(prefix, StringComparison.Ordinal) &&
                    (raw.StartsWith(prefix + ";", StringComparison.Ordinal) ||
                     raw.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            return false;
        }

        async Task<User> ReloadUserFreshAsync(string userId)
        {
            using var s = serviceProvider.CreateScope();
            return await s.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId)
                ?? throw new InvalidOperationException($"User '{userId}' not found.");
        }

        async Task<User> CreateUserAsync(
            string tag,
            string? email,
            bool isActive = true,
            bool telegramVerified = true,
            long? chatId = null,
            bool lockedOut = false)
        {
            var uid = $"PR{suffix}{createdUserIds.Count + 1}";
            var user = new User
            {
                UserName = email ?? uid,
                Email = email,
                UniversityId = uid,
                FullName = $"PwdReset {tag}",
                IsActive = isActive,
                IsTelegramVerified = telegramVerified,
                TelegramChatId = chatId,
                EmailConfirmed = email != null,
                LockoutEnabled = true,
                LockoutEnd = lockedOut ? DateTimeOffset.UtcNow.AddMinutes(30) : null
            };
            var res = await userManager.CreateAsync(user, initialPassword);
            if (!res.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", res.Errors.Select(e => e.Description)));
            }
            await userManager.AddToRoleAsync(user, "Student");
            createdUserIds.Add(user.Id);
            return user;
        }

        try
        {
            var baseChatId = Random.Shared.NextInt64(5_000_000_000L, 5_999_999_999L);

            var eligibleEmailUser = await CreateUserAsync("eligible_email", $"pwreset_a_{suffix}@aub.test", true, true, baseChatId);
            var eligibleNoEmailStudent = await CreateUserAsync("eligible_noemail", null, true, true, baseChatId + 1);
            var inactiveUser = await CreateUserAsync("inactive", $"pwreset_inact_{suffix}@aub.test", false, true, baseChatId + 2);
            var lockedUser = await CreateUserAsync("locked", $"pwreset_lock_{suffix}@aub.test", true, true, baseChatId + 3, lockedOut: true);
            var unlinkedUser = await CreateUserAsync("unlinked", $"pwreset_unlk_{suffix}@aub.test", true, false, null);
            var secondUser = await CreateUserAsync("second", $"pwreset_b_{suffix}@aub.test", true, true, baseChatId + 4);
            var concurrentUser = await CreateUserAsync("concurrent", $"pwreset_c_{suffix}@aub.test", true, true, baseChatId + 5);

            // ─────────────────────────────────────────────────────────────
            // 15 & 16 setup: Issue valid JWT & Identity cookie BEFORE password reset
            // ─────────────────────────────────────────────────────────────
            var preResetAuthSvc = new AuthService(
                userManager, context, spyTelegram, dataProtection, jwtConfig, NullLogger<AuthService>.Instance);
            var oldJwt = preResetAuthSvc.GenerateJwtToken(eligibleEmailUser, "STUDENT")!.Token;

            spyTelegram.Dispatched.Clear();
            var preLogin = await InvokeAccountAsync(spyTelegram, c => c.Login(new LoginViewModel
            {
                LoginId = eligibleEmailUser.Email!,
                Password = initialPassword
            }));
            var prePendingCookie = ExtractSetCookiePair(preLogin.Headers, "AUB_PendingVerification");
            var preLoginOtp = spyTelegram.Dispatched.Single().Code;
            var preVerify = await InvokeAccountAsync(spyTelegram, c => c.VerifyTelegram(new VerifyTelegramViewModel
            {
                UserId = eligibleEmailUser.Id,
                Code = preLoginOtp
            }), cookieHeader: prePendingCookie);
            var oldAppCookie = ExtractSetCookiePair(preVerify.Headers, appCookieName);
            var oldCookieValidBefore = (await AuthenticateAppCookieAsync(oldAppCookie)).Succeeded;
            var oldJwtValidBefore = (await preResetAuthSvc.ValidateApiBearerTokenAsync(oldJwt)) != null;

            // Also create pending login OTP & link token on eligibleNoEmailStudent for test 19
            var extraLinkToken = new TelegramLinkToken
            {
                UserId = eligibleNoEmailStudent.Id,
                TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("pending_link_" + suffix))).ToLowerInvariant(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                IsUsed = false
            };
            context.TelegramLinkTokens.Add(extraLinkToken);
            await context.SaveChangesAsync();

            // ─────────────────────────────────────────────────────────────
            // 17. Login OTP cannot be reused for Password Reset
            // ─────────────────────────────────────────────────────────────
            spyTelegram.Dispatched.Clear();
            var loginForCrossTest = await InvokeAccountAsync(spyTelegram, c => c.Login(new LoginViewModel
            {
                LoginId = eligibleNoEmailStudent.UniversityId!,
                Password = initialPassword
            }));
            var loginPendingCookie = ExtractSetCookiePair(loginForCrossTest.Headers, "AUB_PendingVerification");
            var activeLoginOtp = spyTelegram.Dispatched.Single().Code;

            // Start password reset for eligibleNoEmailStudent
            spyTelegram.Dispatched.Clear();
            var fpNoEmail = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel
            {
                Email = eligibleNoEmailStudent.UniversityId!
            }));
            var resetCookieNoEmail = ExtractSetCookiePair(fpNoEmail.Headers, "AUB_PendingPasswordReset");
            var resetOtpNoEmail = spyTelegram.Dispatched.Single().Code;
            if (activeLoginOtp == resetOtpNoEmail)
            {
                // Ensure distinct codes to avoid coincidental 1-in-10^6 collision
                var row = await context.TelegramVerificationRequests.Where(r => r.UserId == eligibleNoEmailStudent.Id && r.Code.StartsWith("RST:")).OrderByDescending(r => r.Id).FirstAsync();
                resetOtpNoEmail = resetOtpNoEmail == "111111" ? "222222" : "111111";
                row.Code = $"RST:{resetOtpNoEmail}";
                await context.SaveChangesAsync();
            }

            var crossLoginOtpOnReset = await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
            {
                Code = activeLoginOtp,
                NewPassword = updatedPassword,
                ConfirmPassword = updatedPassword
            }), cookieHeader: resetCookieNoEmail);
            Assert(
                "17. Login OTP cannot be reused for Password Reset",
                crossLoginOtpOnReset.Result is ViewResult &&
                !crossLoginOtpOnReset.ModelState.IsValid &&
                await userManager.CheckPasswordAsync(await ReloadUserFreshAsync(eligibleNoEmailStudent.Id), initialPassword));

            // 18. Password Reset OTP cannot be reused for Login
            var crossResetOtpOnLogin = await InvokeAccountAsync(spyTelegram, c => c.VerifyTelegram(new VerifyTelegramViewModel
            {
                UserId = eligibleNoEmailStudent.Id,
                Code = resetOtpNoEmail
            }), cookieHeader: loginPendingCookie);
            Assert(
                "18. Password Reset OTP cannot be reused for Login",
                crossResetOtpOnLogin.Result is ViewResult &&
                !crossResetOtpOnLogin.ModelState.IsValid &&
                string.IsNullOrEmpty(ExtractSetCookiePair(crossResetOtpOnLogin.Headers, appCookieName)));

            // 1. Successful Telegram OTP password reset (email-less student via UniversityId) & 19. Pending state invalidation
            var resetSuccessNoEmail = await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
            {
                Code = resetOtpNoEmail,
                NewPassword = updatedPassword,
                ConfirmPassword = updatedPassword
            }), cookieHeader: resetCookieNoEmail);
            var reloadedNoEmail = await ReloadUserFreshAsync(eligibleNoEmailStudent.Id);
            var activeOtpsLeft = await context.TelegramVerificationRequests.AsNoTracking().CountAsync(r => r.UserId == eligibleNoEmailStudent.Id && !r.IsUsed);
            var activeLinksLeft = await context.TelegramLinkTokens.AsNoTracking().CountAsync(t => t.UserId == eligibleNoEmailStudent.Id && !t.IsUsed);

            Assert(
                "1. Successful Telegram OTP password reset works for email-less student and updates password",
                resetSuccessNoEmail.Result is RedirectToActionResult r1 &&
                r1.ActionName == "ResetPasswordConfirmation" &&
                HasSetCookieDeletion(resetSuccessNoEmail.Headers, "AUB_PendingPasswordReset") &&
                !await userManager.CheckPasswordAsync(reloadedNoEmail, initialPassword) &&
                await userManager.CheckPasswordAsync(reloadedNoEmail, updatedPassword) &&
                reloadedNoEmail.IsTelegramVerified,
                $"res={resetSuccessNoEmail.Result.GetType().Name}");

            Assert(
                "19. Pending authentication state (Login OTPs, link tokens, and pending cookies) invalidated after password reset",
                activeOtpsLeft == 0 &&
                activeLinksLeft == 0 &&
                HasSetCookieDeletion(resetSuccessNoEmail.Headers, "AUB_PendingVerification"));

            // ─────────────────────────────────────────────────────────────
            // 15 & 16. Old JWT and old Identity cookie invalidated after password reset
            // ─────────────────────────────────────────────────────────────
            spyTelegram.Dispatched.Clear();
            var fpEmailUser = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel
            {
                Email = eligibleEmailUser.Email!
            }));
            var resetCookieEmail = ExtractSetCookiePair(fpEmailUser.Headers, "AUB_PendingPasswordReset");
            var resetOtpEmail = spyTelegram.Dispatched.Single().Code;

            var stampBeforeReset = (await ReloadUserFreshAsync(eligibleEmailUser.Id)).SecurityStamp;
            var resetEmailSuccess = await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
            {
                Code = resetOtpEmail,
                NewPassword = updatedPassword,
                ConfirmPassword = updatedPassword
            }), cookieHeader: resetCookieEmail);
            var reloadedEmailUser = await ReloadUserFreshAsync(eligibleEmailUser.Id);
            var stampAfterReset = reloadedEmailUser.SecurityStamp;

            var oldCookieValidAfter = (await AuthenticateAppCookieAsync(oldAppCookie)).Succeeded;

            // Validate old & new JWTs in a fresh request scope (and verify AuthController.GetMe rejects old and accepts new)
            using var apiScope = serviceProvider.CreateScope();
            var apiAuthSvc = new AuthService(
                apiScope.ServiceProvider.GetRequiredService<UserManager<User>>(),
                apiScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                spyTelegram, dataProtection, jwtConfig, NullLogger<AuthService>.Instance);

            var oldJwtValidAfter = (await apiAuthSvc.ValidateApiBearerTokenAsync(oldJwt)) != null;
            var newJwt = apiAuthSvc.GenerateJwtToken(reloadedEmailUser, "STUDENT")!.Token;
            var newJwtValidAfter = (await apiAuthSvc.ValidateApiBearerTokenAsync(newJwt)) != null;

            var apiCtrlOld = new AuthController(userManager, apiAuthSvc, jwtConfig, NullLogger<AuthController>.Instance);
            apiCtrlOld.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            apiCtrlOld.HttpContext.Request.Headers.Authorization = $"Bearer {oldJwt}";
            var oldGetMeRes = await apiCtrlOld.GetMe() as ObjectResult;

            var apiCtrlNew = new AuthController(userManager, apiAuthSvc, jwtConfig, NullLogger<AuthController>.Instance);
            apiCtrlNew.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            apiCtrlNew.HttpContext.Request.Headers.Authorization = $"Bearer {newJwt}";
            var newGetMeRes = await apiCtrlNew.GetMe() as ObjectResult;

            Assert(
                "15. Old JWT invalidated across API endpoints after password reset (SecurityStamp rotated)",
                oldJwtValidBefore &&
                !oldJwtValidAfter &&
                oldGetMeRes?.StatusCode == StatusCodes.Status401Unauthorized &&
                newJwtValidAfter &&
                newGetMeRes?.StatusCode == StatusCodes.Status200OK &&
                stampBeforeReset != stampAfterReset,
                $"oldBefore={oldJwtValidBefore}, oldAfter={oldJwtValidAfter}, oldGetMe={oldGetMeRes?.StatusCode}, newAfter={newJwtValidAfter}, newGetMe={newGetMeRes?.StatusCode}, stampChanged={stampBeforeReset != stampAfterReset}");

            Assert(
                "16. Old Identity cookie invalidated immediately after password reset",
                oldCookieValidBefore &&
                !oldCookieValidAfter &&
                resetEmailSuccess.Result is RedirectToActionResult);

            // ─────────────────────────────────────────────────────────────
            // 4. OTP replay rejection
            // ─────────────────────────────────────────────────────────────
            var replayReset = await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
            {
                Code = resetOtpEmail,
                NewPassword = "AnotherPass@2026",
                ConfirmPassword = "AnotherPass@2026"
            }), cookieHeader: resetCookieEmail);
            Assert(
                "4. Replayed password reset session and OTP rejected",
                replayReset.Result is RedirectToActionResult rRep &&
                rRep.ActionName == "ForgotPassword" &&
                await userManager.CheckPasswordAsync(await ReloadUserFreshAsync(eligibleEmailUser.Id), updatedPassword),
                $"res={replayReset.Result.GetType().Name}");

            // ─────────────────────────────────────────────────────────────
            // 2, 3, 5, 13. Invalid OTP, Expired OTP, Max Attempts, Password Policy
            // ─────────────────────────────────────────────────────────────
            spyTelegram.Dispatched.Clear();
            var fpSecond = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel
            {
                Email = secondUser.Email!
            }));
            var resetCookieSecond = ExtractSetCookiePair(fpSecond.Headers, "AUB_PendingPasswordReset");
            var resetOtpSecond = spyTelegram.Dispatched.Single().Code;

            // 13. Password policy enforcement (with valid OTP, weak password rejected without changing password)
            var weakPolicyRes = await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
            {
                Code = resetOtpSecond,
                NewPassword = "weak",
                ConfirmPassword = "weak"
            }), cookieHeader: resetCookieSecond);
            Assert(
                "13. Password policy enforcement rejects weak password and preserves original password",
                weakPolicyRes.Result is ViewResult &&
                !weakPolicyRes.ModelState.IsValid &&
                await userManager.CheckPasswordAsync(await userManager.FindByIdAsync(secondUser.Id) ?? throw new Exception(), initialPassword));

            // 2. Invalid OTP rejection
            var wrongCode = resetOtpSecond == "000000" ? "999999" : "000000";
            var badOtpRes = await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
            {
                Code = wrongCode,
                NewPassword = updatedPassword,
                ConfirmPassword = updatedPassword
            }), cookieHeader: resetCookieSecond);
            Assert(
                "2. Invalid OTP rejected without changing password",
                badOtpRes.Result is ViewResult &&
                !badOtpRes.ModelState.IsValid &&
                await userManager.CheckPasswordAsync(await userManager.FindByIdAsync(secondUser.Id) ?? throw new Exception(), initialPassword));

            // 5. Max attempts enforcement (4 more wrong guesses = 5 total; subsequent real OTP rejected & user NOT locked out)
            for (int i = 0; i < 4; i++)
            {
                await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
                {
                    Code = wrongCode,
                    NewPassword = updatedPassword,
                    ConfirmPassword = updatedPassword
                }), cookieHeader: resetCookieSecond);
            }
            var afterMaxAttemptsRes = await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
            {
                Code = resetOtpSecond,
                NewPassword = updatedPassword,
                ConfirmPassword = updatedPassword
            }), cookieHeader: resetCookieSecond);
            var secondUserAfterMax = await userManager.FindByIdAsync(secondUser.Id);
            Assert(
                "5. Maximum attempts (5) invalidates challenge and blocks even the real OTP without locking victim's login",
                afterMaxAttemptsRes.Result is RedirectToActionResult rMax &&
                rMax.ActionName == "ForgotPassword" &&
                !await userManager.IsLockedOutAsync(secondUserAfterMax!) &&
                await userManager.CheckPasswordAsync(secondUserAfterMax!, initialPassword));

            // 3. Expired OTP rejection
            var secondReqRow = await context.TelegramVerificationRequests
                .Where(r => r.UserId == secondUser.Id && r.Code.StartsWith("RST:"))
                .OrderByDescending(r => r.Id)
                .FirstAsync();
            secondReqRow.CreatedAt = DateTime.UtcNow.AddMinutes(-15);
            await context.SaveChangesAsync();

            spyTelegram.Dispatched.Clear();
            var fpSecondFresh = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel
            {
                Email = secondUser.Email!
            }));
            var resetCookieSecondFresh = ExtractSetCookiePair(fpSecondFresh.Headers, "AUB_PendingPasswordReset");
            var resetOtpSecondFresh = spyTelegram.Dispatched.Single().Code;

            var expRow = await context.TelegramVerificationRequests
                .Where(r => r.UserId == secondUser.Id && r.Code.StartsWith("RST:") && !r.IsUsed)
                .OrderByDescending(r => r.Id)
                .FirstAsync();
            expRow.ExpiresAt = DateTime.UtcNow.AddMinutes(-2);
            await context.SaveChangesAsync();

            var expiredOtpRes = await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
            {
                Code = resetOtpSecondFresh,
                NewPassword = updatedPassword,
                ConfirmPassword = updatedPassword
            }), cookieHeader: resetCookieSecondFresh);
            await context.Entry(expRow).ReloadAsync();
            Assert(
                "3. Expired OTP rejected and marked used without changing password",
                expiredOtpRes.Result is ViewResult &&
                !expiredOtpRes.ModelState.IsValid &&
                expRow.IsUsed &&
                await userManager.CheckPasswordAsync(await userManager.FindByIdAsync(secondUser.Id) ?? throw new Exception(), initialPassword));

            // ─────────────────────────────────────────────────────────────
            // 6. Resend cooldown across different login identifiers
            // ─────────────────────────────────────────────────────────────
            // Create a fresh user for clean cooldown check
            var cooldownUser = await CreateUserAsync("cooldown", $"pwreset_cd_{suffix}@aub.test", true, true, baseChatId + 6);
            spyTelegram.Dispatched.Clear();
            var cdInit1 = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel
            {
                Email = cooldownUser.Email!
            }));
            var cdCookie = ExtractSetCookiePair(cdInit1.Headers, "AUB_PendingPasswordReset");
            // Try again immediately using UniversityId instead of Email -> must NOT dispatch a 2nd OTP
            await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel
            {
                Email = cooldownUser.UniversityId!
            }));
            // Try ResendPasswordResetCode immediately -> rate limited
            var cdResend = await InvokeAccountAsync(spyTelegram, c => c.ResendPasswordResetCode(), cookieHeader: cdCookie);
            Assert(
                "6. Resend cooldown (60s) prevents OTP spam across Email and UniversityId identifiers",
                spyTelegram.Dispatched.Count == 1 &&
                cdResend.TempData["ErrorMessage"] != null);

            // ─────────────────────────────────────────────────────────────
            // 7. Account mismatch rejection
            // ─────────────────────────────────────────────────────────────
            var mismatchCookieRes = await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
            {
                Code = spyTelegram.Dispatched.Single().Code,
                NewPassword = updatedPassword,
                ConfirmPassword = updatedPassword
            }), cookieHeader: resetCookieSecondFresh);
            Assert(
                "7. Account mismatch (cookie for User B with OTP for User A) is rejected",
                mismatchCookieRes.Result is ViewResult &&
                !mismatchCookieRes.ModelState.IsValid &&
                await userManager.CheckPasswordAsync(await userManager.FindByIdAsync(cooldownUser.Id) ?? throw new Exception(), initialPassword));

            // ─────────────────────────────────────────────────────────────
            // 8, 9, 11. Inactive, locked, unlinked accounts & enumeration resistance
            // ─────────────────────────────────────────────────────────────
            spyTelegram.Dispatched.Clear();
            var rEligible = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel { Email = eligibleEmailUser.Email! }));
            var rInactive = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel { Email = inactiveUser.Email! }));
            var rLocked = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel { Email = lockedUser.Email! }));
            var rUnlinked = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel { Email = unlinkedUser.Email! }));
            var rUnknown = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel { Email = $"nonexistent_{suffix}@aub.test" }));

            var cInactive = ExtractSetCookiePair(rInactive.Headers, "AUB_PendingPasswordReset");
            var cLocked = ExtractSetCookiePair(rLocked.Headers, "AUB_PendingPasswordReset");
            var cUnlinked = ExtractSetCookiePair(rUnlinked.Headers, "AUB_PendingPasswordReset");
            var cUnknown = ExtractSetCookiePair(rUnknown.Headers, "AUB_PendingPasswordReset");

            // Check API /api/Auth/forgot-password enumeration resistance (with Smtp:Host configured in jwtConfig!)
            var apiElig = await InvokeApiForgotAsync(eligibleEmailUser.Email!);
            var apiInact = await InvokeApiForgotAsync(inactiveUser.Email!);
            var apiUnlk = await InvokeApiForgotAsync(unlinkedUser.Email!);
            var apiNoEmail = await InvokeApiForgotAsync(eligibleNoEmailStudent.UniversityId!);
            var apiUnk = await InvokeApiForgotAsync($"nonexistent_{suffix}@aub.test");

            Assert(
                "8 & 9. Inactive, locked, and unlinked Telegram accounts never receive OTPs and cannot reset password",
                spyTelegram.Dispatched.Count == 0 && // eligibleEmailUser was within 60s cooldown or none for inactive/locked/unlinked/unknown!
                !string.IsNullOrEmpty(cInactive) &&
                !string.IsNullOrEmpty(cLocked) &&
                !string.IsNullOrEmpty(cUnlinked) &&
                !string.IsNullOrEmpty(cUnknown));

            Assert(
                "11. Web and API ForgotPassword are enumeration-resistant across all account states",
                (rEligible.Result as RedirectToActionResult)?.ActionName == "ResetPassword" &&
                (rInactive.Result as RedirectToActionResult)?.ActionName == "ResetPassword" &&
                (rLocked.Result as RedirectToActionResult)?.ActionName == "ResetPassword" &&
                (rUnlinked.Result as RedirectToActionResult)?.ActionName == "ResetPassword" &&
                (rUnknown.Result as RedirectToActionResult)?.ActionName == "ResetPassword" &&
                rInactive.TempData["InfoMessage"]?.ToString() == rUnknown.TempData["InfoMessage"]?.ToString() &&
                apiElig.StatusCode == 200 &&
                apiInact.StatusCode == 200 &&
                apiUnlk.StatusCode == 200 &&
                apiNoEmail.StatusCode == 200 &&
                apiUnk.StatusCode == 200 &&
                apiElig.Json.GetProperty("message").GetString() == apiUnk.Json.GetProperty("message").GetString() &&
                apiNoEmail.Json.GetProperty("message").GetString() == apiUnk.Json.GetProperty("message").GetString() &&
                !apiElig.Json.TryGetProperty("emailConfigured", out _));

            // ─────────────────────────────────────────────────────────────
            // 10. Telegram delivery failure fails closed
            // ─────────────────────────────────────────────────────────────
            var failDeliveryUser = await CreateUserAsync("faildelivery", $"pwreset_fd_{suffix}@aub.test", true, true, baseChatId + 7);
            var failingTelegramCfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:BotToken"] = "123456:TEST_FAILING_BOT_TOKEN_9876543210" })
                .Build();
            using var failingHttp = new HttpClient(new FailingHttpMessageHandler());
            var failingTelegram = new TelegramService(
                failingHttp,
                failingTelegramCfg,
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<TelegramService>.Instance,
                new StubHostEnvironment());

            await InvokeAccountAsync(failingTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel
            {
                Email = failDeliveryUser.Email!
            }));
            var activeOtpsAfterFailDelivery = await context.TelegramVerificationRequests.AsNoTracking()
                .CountAsync(r => r.UserId == failDeliveryUser.Id && !r.IsUsed);
            Assert(
                "10. Telegram delivery failure fails closed and leaves zero active OTPs",
                activeOtpsAfterFailDelivery == 0);

            // ─────────────────────────────────────────────────────────────
            // 12. Rate limiting (per-IP throttle + RateLimiter attributes)
            // ─────────────────────────────────────────────────────────────
            const string rateLimitIp = "198.51.100.77";
            int rateLimitedStatus = 0;
            for (int i = 0; i < 12; i++)
            {
                var rlRes = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel
                {
                    Email = $"rl_{i}_{suffix}@aub.test"
                }), remoteIp: rateLimitIp);
                if (rlRes.StatusCode == StatusCodes.Status429TooManyRequests)
                {
                    rateLimitedStatus = rlRes.StatusCode;
                    break;
                }
            }
            var apiRl = await InvokeApiForgotAsync($"rl_api_{suffix}@aub.test", remoteIp: rateLimitIp);
            var acctForgotPostAttr = typeof(AccountController)
                .GetMethod(nameof(AccountController.ForgotPassword), new[] { typeof(ForgotPasswordViewModel) })!
                .GetCustomAttributes<EnableRateLimitingAttribute>().Any(a => a.PolicyName == "PasswordResetPolicy");
            var apiForgotAttr = typeof(AuthController)
                .GetMethod(nameof(AuthController.ForgotPassword))!
                .GetCustomAttributes<EnableRateLimitingAttribute>().Any(a => a.PolicyName == "PasswordResetPolicy");

            Assert(
                "12. Rate limiting enforces 429 throttling per IP on both Web and API endpoints",
                rateLimitedStatus == StatusCodes.Status429TooManyRequests &&
                apiRl.StatusCode == StatusCodes.Status429TooManyRequests &&
                acctForgotPostAttr &&
                apiForgotAttr);

            // ─────────────────────────────────────────────────────────────
            // 14. Concurrent reset attempts cannot consume the same OTP twice
            // ─────────────────────────────────────────────────────────────
            spyTelegram.Dispatched.Clear();
            var fpConcurrent = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel
            {
                Email = concurrentUser.Email!
            }));
            var concurrentOtp = spyTelegram.Dispatched.Single().Code;
            var concurrentReq = await context.TelegramVerificationRequests.AsNoTracking()
                .Where(r => r.UserId == concurrentUser.Id && r.Code.StartsWith("RST:") && !r.IsUsed)
                .OrderByDescending(r => r.Id)
                .FirstAsync();

            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<PasswordResetCompleteResult> RunConcurrentResetAsync(string targetPwd)
            {
                await gate.Task;
                using var cScope = serviceProvider.CreateScope();
                var csp = cScope.ServiceProvider;
                var svc = new AuthService(
                    csp.GetRequiredService<UserManager<User>>(),
                    csp.GetRequiredService<ApplicationDbContext>(),
                    spyTelegram,
                    csp.GetRequiredService<IDataProtectionProvider>(),
                    jwtConfig,
                    NullLogger<AuthService>.Instance);
                return await svc.CompletePasswordResetAsync(concurrentUser.Id, concurrentReq.Id, true, concurrentOtp, targetPwd);
            }

            var t1 = Task.Run(() => RunConcurrentResetAsync("RacePass1@2026"));
            var t2 = Task.Run(() => RunConcurrentResetAsync("RacePass2@2026"));
            gate.SetResult(true);
            var raceResults = await Task.WhenAll(t1, t2);
            var raceWins = raceResults.Count(r => r.Status == PasswordResetCompleteStatus.Succeeded);

            Assert(
                "14. Concurrent reset attempts allow strictly one winner",
                raceWins == 1 && raceResults.Count(r => r.Status == PasswordResetCompleteStatus.InvalidOrExpiredCode) == 1);

            // ─────────────────────────────────────────────────────────────
            // 20. No sensitive values in application logs
            // ─────────────────────────────────────────────────────────────
            var logUser = await CreateUserAsync("logcheck", $"pwreset_log_{suffix}@aub.test", true, true, baseChatId + 8);
            const string sensitiveNewPassword = "SuperSecretLogCheck@987";
            var capLoggerProvider = new CapturingLoggerProvider();
            using var capLoggerFactory = LoggerFactory.Create(b =>
            {
                b.SetMinimumLevel(LogLevel.Trace);
                b.AddProvider(capLoggerProvider);
            });

            spyTelegram.Dispatched.Clear();
            var fpLog = await InvokeAccountAsync(spyTelegram, c => c.ForgotPassword(new ForgotPasswordViewModel
            {
                Email = logUser.Email!
            }), customLoggerFactory: capLoggerFactory);
            var logResetCookie = ExtractSetCookiePair(fpLog.Headers, "AUB_PendingPasswordReset");
            var logOtp = spyTelegram.Dispatched.Single().Code;

            await InvokeAccountAsync(spyTelegram, c => c.ResetPassword(new ResetPasswordViewModel
            {
                Code = logOtp,
                NewPassword = sensitiveNewPassword,
                ConfirmPassword = sensitiveNewPassword
            }), cookieHeader: logResetCookie, customLoggerFactory: capLoggerFactory);

            var allLogs = string.Join("\n", capLoggerProvider.Messages);
            Assert(
                "20. Application logs contain zero raw identifiers/emails, OTPs, passwords, or ChatIds",
                !allLogs.Contains(logUser.Email!, StringComparison.OrdinalIgnoreCase) &&
                !allLogs.Contains(logOtp, StringComparison.Ordinal) &&
                !allLogs.Contains(sensitiveNewPassword, StringComparison.Ordinal) &&
                !allLogs.Contains((baseChatId + 8).ToString(), StringComparison.Ordinal));
        }
        finally
        {
            using var cleanupScope = serviceProvider.CreateScope();
            var cleanupCtx = cleanupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cleanupUm = cleanupScope.ServiceProvider.GetRequiredService<UserManager<User>>();

            foreach (var uid in createdUserIds.Distinct())
            {
                var reqs = await cleanupCtx.TelegramVerificationRequests.Where(r => r.UserId == uid).ToListAsync();
                if (reqs.Count > 0)
                {
                    cleanupCtx.TelegramVerificationRequests.RemoveRange(reqs);
                }

                var links = await cleanupCtx.TelegramLinkTokens.Where(l => l.UserId == uid).ToListAsync();
                if (links.Count > 0)
                {
                    cleanupCtx.TelegramLinkTokens.RemoveRange(links);
                }

                await cleanupCtx.SaveChangesAsync();

                var u = await cleanupUm.FindByIdAsync(uid);
                if (u != null)
                {
                    await cleanupUm.DeleteAsync(u);
                }
            }
        }

        Console.WriteLine("=================================================================");
        Console.WriteLine($"  SUMMARY: {passed} passed, {failed} failed");
        Console.WriteLine("=================================================================");

        if (failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }
}
