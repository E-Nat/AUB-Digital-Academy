using System.Security.Claims;
using AUBDigitalAcademy.Controllers;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Implementations;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AUBDigitalAcademy.Data;

public static class GoogleOAuth2FaIntegrationTestRunner
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
        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("  GOOGLE OAUTH 2FA & SEEDDATA HARDENING INTEGRATION TEST SUITE   ");
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
        var signInManager = sp.GetRequiredService<SignInManager<User>>();
        var dataProtection = sp.GetRequiredService<IDataProtectionProvider>();
        var configuration = sp.GetRequiredService<IConfiguration>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();
        var authzService = sp.GetRequiredService<IAuthorizationService>();
        var cookieOpts = sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var appCookieName = cookieOpts.Get(IdentityConstants.ApplicationScheme).Cookie.Name ?? ".AspNetCore.Identity.Application";
        var extCookieName = cookieOpts.Get(IdentityConstants.ExternalScheme).Cookie.Name ?? IdentityConstants.ExternalScheme;

        var spyTelegram = new SpyTelegramService();

        async Task<(IActionResult Result, IHeaderDictionary ResponseHeaders, ModelStateDictionary ModelState, bool UserAuthenticated)> InvokeCallbackAsync(
            string? cookieHeader,
            string? returnUrl = null,
            string? remoteError = null)
        {
            using var reqScope = serviceProvider.CreateScope();
            var rsp = reqScope.ServiceProvider;
            var reqCtx = rsp.GetRequiredService<ApplicationDbContext>();
            var reqUm = rsp.GetRequiredService<UserManager<User>>();
            var reqSm = rsp.GetRequiredService<SignInManager<User>>();
            var reqDp = rsp.GetRequiredService<IDataProtectionProvider>();
            var reqCfg = rsp.GetRequiredService<IConfiguration>();
            var reqLf = rsp.GetRequiredService<ILoggerFactory>();
            var reqTdp = rsp.GetRequiredService<ITempDataProvider>();

            var authService = new AuthService(
                reqUm, reqCtx, spyTelegram, reqDp, reqCfg, reqLf.CreateLogger<AuthService>());

            var controller = new AccountController(
                reqSm, reqUm, reqCtx, spyTelegram, authService, reqDp, reqLf.CreateLogger<AccountController>());

            var httpContext = new DefaultHttpContext { RequestServices = rsp };
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                httpContext.Request.Headers.Cookie = cookieHeader;
            }

            if (rsp.GetService<IHttpContextAccessor>() is { } accessor)
            {
                accessor.HttpContext = httpContext;
            }

            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
            controller.ControllerContext = new ControllerContext(actionContext);
            controller.Url = new StubUrlHelper(actionContext);
            controller.TempData = new TempDataDictionary(httpContext, reqTdp);

            var result = await controller.ExternalLoginCallback(returnUrl, remoteError);
            return (result, controller.Response.Headers, controller.ModelState, controller.HttpContext.User.Identity?.IsAuthenticated ?? false);
        }

        async Task<(IActionResult Result, IHeaderDictionary ResponseHeaders, ModelStateDictionary ModelState)> InvokeVerifyTelegramAsync(
            string? cookieHeader,
            VerifyTelegramViewModel model)
        {
            using var reqScope = serviceProvider.CreateScope();
            var rsp = reqScope.ServiceProvider;
            var reqCtx = rsp.GetRequiredService<ApplicationDbContext>();
            var reqUm = rsp.GetRequiredService<UserManager<User>>();
            var reqSm = rsp.GetRequiredService<SignInManager<User>>();
            var reqDp = rsp.GetRequiredService<IDataProtectionProvider>();
            var reqCfg = rsp.GetRequiredService<IConfiguration>();
            var reqLf = rsp.GetRequiredService<ILoggerFactory>();
            var reqTdp = rsp.GetRequiredService<ITempDataProvider>();

            var authService = new AuthService(
                reqUm, reqCtx, spyTelegram, reqDp, reqCfg, reqLf.CreateLogger<AuthService>());

            var controller = new AccountController(
                reqSm, reqUm, reqCtx, spyTelegram, authService, reqDp, reqLf.CreateLogger<AccountController>());

            var httpContext = new DefaultHttpContext { RequestServices = rsp };
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                httpContext.Request.Headers.Cookie = cookieHeader;
            }

            if (rsp.GetService<IHttpContextAccessor>() is { } accessor)
            {
                accessor.HttpContext = httpContext;
            }

            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
            controller.ControllerContext = new ControllerContext(actionContext);
            controller.Url = new StubUrlHelper(actionContext);
            controller.TempData = new TempDataDictionary(httpContext, reqTdp);

            var result = await controller.VerifyTelegram(model);
            return (result, controller.Response.Headers, controller.ModelState);
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
                if (string.IsNullOrEmpty(raw) || !raw.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var pair = raw.Split(';', 2)[0];
                var value = pair[prefix.Length..];
                matched = string.IsNullOrEmpty(value) ? null : pair;
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

        async Task<string> IssueExternalCookieAsync(string provider, string providerKey, string? email, string? name, string? nonce = null)
        {
            using var reqScope = serviceProvider.CreateScope();
            var rsp = reqScope.ServiceProvider;
            var reqSm = rsp.GetRequiredService<SignInManager<User>>();

            var ctx = new DefaultHttpContext { RequestServices = rsp };
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("localhost");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, providerKey)
            };
            if (!string.IsNullOrEmpty(email))
            {
                claims.Add(new Claim(ClaimTypes.Email, email));
            }
            if (!string.IsNullOrEmpty(name))
            {
                claims.Add(new Claim(ClaimTypes.Name, name));
            }

            var identity = new ClaimsIdentity(claims, provider);
            var principal = new ClaimsPrincipal(identity);
            var props = reqSm.ConfigureExternalAuthenticationProperties(provider, "/Account/ExternalLoginCallback");
            props.Items["AubExternalNonce"] = nonce ?? Guid.NewGuid().ToString("N");

            await ctx.SignInAsync(IdentityConstants.ExternalScheme, principal, props);
            return ExtractSetCookiePair(ctx.Response.Headers, extCookieName)
                   ?? throw new InvalidOperationException($"Failed to issue {extCookieName} cookie for test.");
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createdUserIds = new List<string>();
        const string testPassword = "TestGoogle2Fa@2026!";

        async Task<User> CreateTestUserAsync(
            string tag,
            string role,
            bool isActive = true,
            bool telegramVerified = true,
            long? chatId = null,
            bool lockedOut = false,
            string? googleProviderKey = null)
        {
            var user = new User
            {
                UserName = $"goauth_{tag}_{suffix}@aub.test",
                Email = $"goauth_{tag}_{suffix}@aub.test",
                FullName = $"Google Test {tag}",
                UniversityId = $"G{suffix}{createdUserIds.Count + 1}",
                IsActive = isActive,
                IsTelegramVerified = telegramVerified,
                TelegramChatId = chatId,
                EmailConfirmed = true,
                LockoutEnabled = true,
                LockoutEnd = lockedOut ? DateTimeOffset.UtcNow.AddMinutes(30) : null
            };

            var createRes = await userManager.CreateAsync(user, testPassword);
            if (!createRes.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", createRes.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(user, role);
            createdUserIds.Add(user.Id);

            if (!string.IsNullOrEmpty(googleProviderKey))
            {
                var loginRes = await userManager.AddLoginAsync(user, new UserLoginInfo("Google", googleProviderKey, "Google"));
                if (!loginRes.Succeeded)
                {
                    throw new InvalidOperationException(string.Join("; ", loginRes.Errors.Select(e => e.Description)));
                }
            }

            return user;
        }

        try
        {
            var baseChatId = Random.Shared.NextInt64(7_000_000_000L, 7_999_999_999L);

            var studentUser = await CreateTestUserAsync(
                "student", "Student", isActive: true, telegramVerified: true, chatId: baseChatId,
                googleProviderKey: $"google-sub-student-{suffix}");
            var adminUser = await CreateTestUserAsync(
                "admin", "Admin", isActive: true, telegramVerified: true, chatId: baseChatId + 1,
                googleProviderKey: $"google-sub-admin-{suffix}");
            var inactiveUser = await CreateTestUserAsync(
                "inactive", "Student", isActive: false, telegramVerified: true, chatId: baseChatId + 2,
                googleProviderKey: $"google-sub-inactive-{suffix}");
            var lockedUser = await CreateTestUserAsync(
                "locked", "Student", isActive: true, telegramVerified: true, chatId: baseChatId + 3,
                lockedOut: true, googleProviderKey: $"google-sub-locked-{suffix}");
            var noTelegramUser = await CreateTestUserAsync(
                "notelegram", "Student", isActive: true, telegramVerified: false, chatId: null,
                googleProviderKey: $"google-sub-notelegram-{suffix}");
            var unlinkedGoogleLocalUser = await CreateTestUserAsync(
                "unlinkedgoogle", "Student", isActive: true, telegramVerified: true, chatId: baseChatId + 4,
                googleProviderKey: null);

            // 1. Successful Google authentication creates pending 2FA state but NO authenticated application cookie
            spyTelegram.Dispatched.Clear();
            var extCookie1 = await IssueExternalCookieAsync("Google", $"google-sub-student-{suffix}", studentUser.Email, studentUser.FullName);
            var cb1 = await InvokeCallbackAsync(extCookie1, returnUrl: "/Student/Dashboard");

            var pendingCookie1 = ExtractSetCookiePair(cb1.ResponseHeaders, "AUB_PendingVerification");
            var appCookie1 = ExtractSetCookiePair(cb1.ResponseHeaders, appCookieName);
            var extCleared1 = HasSetCookieDeletion(cb1.ResponseHeaders, extCookieName);
            var otp1 = spyTelegram.Dispatched.SingleOrDefault()?.Code;
            var redirect1 = cb1.Result as RedirectToActionResult;

            Assert(
                "1. Successful Google auth creates pending 2FA state, sends OTP, clears external cookie, issues NO app cookie",
                redirect1?.ActionName == "VerifyTelegram" &&
                !string.IsNullOrEmpty(pendingCookie1) &&
                string.IsNullOrEmpty(appCookie1) &&
                extCleared1 &&
                spyTelegram.Dispatched.Count == 1 &&
                spyTelegram.Dispatched[0].ChatId == baseChatId.ToString() &&
                otp1?.Length == 6 &&
                !cb1.UserAuthenticated,
                $"redirect={redirect1?.ActionName}, hasPending={!string.IsNullOrEmpty(pendingCookie1)}, hasApp={!string.IsNullOrEmpty(appCookie1)}, extCleared={extCleared1}, dispatched={spyTelegram.Dispatched.Count}");

            // 2. Correct Telegram OTP completes Google login and issues authenticated cookie
            var verify2 = await InvokeVerifyTelegramAsync(pendingCookie1, new VerifyTelegramViewModel
            {
                UserId = studentUser.Id,
                Code = otp1 ?? string.Empty
            });
            var appCookie2 = ExtractSetCookiePair(verify2.ResponseHeaders, appCookieName);
            var pendingCleared2 = HasSetCookieDeletion(verify2.ResponseHeaders, "AUB_PendingVerification");

            var authTicket2 = await AuthenticateAppCookieAsync(appCookie2);
            var usedOtpRow2 = await context.TelegramVerificationRequests
                .AsNoTracking()
                .Where(r => r.UserId == studentUser.Id)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            Assert(
                "2. Correct Telegram OTP completes Google login, clears pending cookie, issues valid authenticated cookie",
                verify2.Result is RedirectResult rr2 &&
                rr2.Url == "/Student/Dashboard" &&
                !string.IsNullOrEmpty(appCookie2) &&
                pendingCleared2 &&
                authTicket2.Succeeded &&
                authTicket2.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) == studentUser.Id &&
                (usedOtpRow2?.IsUsed ?? false),
                $"res={verify2.Result.GetType().Name}, hasApp={!string.IsNullOrEmpty(appCookie2)}, authOk={authTicket2.Succeeded}, used={usedOtpRow2?.IsUsed}");

            // 3. Incorrect OTP does not issue an authenticated cookie
            spyTelegram.Dispatched.Clear();
            var extCookie3 = await IssueExternalCookieAsync("Google", $"google-sub-student-{suffix}", studentUser.Email, studentUser.FullName);
            var cb3 = await InvokeCallbackAsync(extCookie3);
            var pendingCookie3 = ExtractSetCookiePair(cb3.ResponseHeaders, "AUB_PendingVerification");
            var realOtp3 = spyTelegram.Dispatched.Single().Code;
            var wrongOtp3 = realOtp3 == "000000" ? "999999" : "000000";

            var verify3 = await InvokeVerifyTelegramAsync(pendingCookie3, new VerifyTelegramViewModel
            {
                UserId = studentUser.Id,
                Code = wrongOtp3
            });
            var appCookie3 = ExtractSetCookiePair(verify3.ResponseHeaders, appCookieName);
            var reqRow3 = await context.TelegramVerificationRequests
                .Where(r => r.UserId == studentUser.Id && !r.IsUsed)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            Assert(
                "3. Incorrect OTP does not issue an authenticated cookie and increments failed attempts",
                verify3.Result is ViewResult &&
                !verify3.ModelState.IsValid &&
                string.IsNullOrEmpty(appCookie3) &&
                reqRow3?.AttemptsCount == 1);

            // 4. Expired OTP does not issue an authenticated cookie
            reqRow3!.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            await context.SaveChangesAsync();

            var verify4 = await InvokeVerifyTelegramAsync(pendingCookie3, new VerifyTelegramViewModel
            {
                UserId = studentUser.Id,
                Code = realOtp3
            });
            var appCookie4 = ExtractSetCookiePair(verify4.ResponseHeaders, appCookieName);
            await context.Entry(reqRow3).ReloadAsync();

            Assert(
                "4. Expired OTP does not issue an authenticated cookie and invalidates OTP request",
                verify4.Result is ViewResult &&
                !verify4.ModelState.IsValid &&
                string.IsNullOrEmpty(appCookie4) &&
                reqRow3.IsUsed);

            // 5. Replayed external login callback cannot issue another authenticated cookie or pending session
            spyTelegram.Dispatched.Clear();
            var sharedNonce5 = Guid.NewGuid().ToString("N");
            var extCookie5 = await IssueExternalCookieAsync("Google", $"google-sub-student-{suffix}", studentUser.Email, studentUser.FullName, sharedNonce5);
            var firstCb5 = await InvokeCallbackAsync(extCookie5);
            var firstPending5 = ExtractSetCookiePair(firstCb5.ResponseHeaders, "AUB_PendingVerification");

            // Replay exact same external cookie
            spyTelegram.Dispatched.Clear();
            var replayCb5 = await InvokeCallbackAsync(extCookie5);
            var replayPending5 = ExtractSetCookiePair(replayCb5.ResponseHeaders, "AUB_PendingVerification");
            var replayApp5 = ExtractSetCookiePair(replayCb5.ResponseHeaders, appCookieName);

            Assert(
                "5. Replayed external login callback is rejected with no pending session, no OTP, and no app cookie",
                firstCb5.Result is RedirectToActionResult &&
                !string.IsNullOrEmpty(firstPending5) &&
                replayCb5.Result is ViewResult vr5 &&
                vr5.ViewName == "Login" &&
                !replayCb5.ModelState.IsValid &&
                string.IsNullOrEmpty(replayPending5) &&
                string.IsNullOrEmpty(replayApp5) &&
                spyTelegram.Dispatched.Count == 0);

            // 6. Inactive local user cannot complete Google login
            spyTelegram.Dispatched.Clear();
            var extCookie6 = await IssueExternalCookieAsync("Google", $"google-sub-inactive-{suffix}", inactiveUser.Email, inactiveUser.FullName);
            var cb6 = await InvokeCallbackAsync(extCookie6);
            Assert(
                "6. Inactive local user cannot complete Google login",
                cb6.Result is ViewResult vr6 &&
                vr6.ViewName == "Login" &&
                !cb6.ModelState.IsValid &&
                string.IsNullOrEmpty(ExtractSetCookiePair(cb6.ResponseHeaders, "AUB_PendingVerification")) &&
                string.IsNullOrEmpty(ExtractSetCookiePair(cb6.ResponseHeaders, appCookieName)) &&
                spyTelegram.Dispatched.Count == 0);

            // 7. Locked-out local user cannot complete Google login
            spyTelegram.Dispatched.Clear();
            var extCookie7 = await IssueExternalCookieAsync("Google", $"google-sub-locked-{suffix}", lockedUser.Email, lockedUser.FullName);
            var cb7 = await InvokeCallbackAsync(extCookie7);
            Assert(
                "7. Locked-out local user cannot complete Google login",
                cb7.Result is ViewResult vr7 &&
                vr7.ViewName == "Login" &&
                !cb7.ModelState.IsValid &&
                string.IsNullOrEmpty(ExtractSetCookiePair(cb7.ResponseHeaders, "AUB_PendingVerification")) &&
                string.IsNullOrEmpty(ExtractSetCookiePair(cb7.ResponseHeaders, appCookieName)) &&
                spyTelegram.Dispatched.Count == 0);

            // 8. User without linked Telegram cannot bypass 2FA
            spyTelegram.Dispatched.Clear();
            var extCookie8 = await IssueExternalCookieAsync("Google", $"google-sub-notelegram-{suffix}", noTelegramUser.Email, noTelegramUser.FullName);
            var cb8 = await InvokeCallbackAsync(extCookie8);
            Assert(
                "8. User without linked Telegram cannot bypass 2FA via Google login",
                cb8.Result is ViewResult vr8 &&
                vr8.ViewName == "Login" &&
                !cb8.ModelState.IsValid &&
                string.IsNullOrEmpty(ExtractSetCookiePair(cb8.ResponseHeaders, "AUB_PendingVerification")) &&
                string.IsNullOrEmpty(ExtractSetCookiePair(cb8.ResponseHeaders, appCookieName)) &&
                spyTelegram.Dispatched.Count == 0);

            // 9. Unassociated Google account cannot auto-create a local account or silently link by email
            spyTelegram.Dispatched.Clear();
            var unlinkedExistingEmailCookie = await IssueExternalCookieAsync(
                "Google",
                $"google-sub-unlinked-existing-{suffix}",
                unlinkedGoogleLocalUser.Email,
                "Spoofed Name");
            var cb9a = await InvokeCallbackAsync(unlinkedExistingEmailCookie);
            var logins9a = await userManager.GetLoginsAsync(unlinkedGoogleLocalUser);

            var brandNewEmail = $"brand_new_google_{suffix}@aub.test";
            var brandNewEmailCookie = await IssueExternalCookieAsync(
                "Google",
                $"google-sub-brand-new-{suffix}",
                brandNewEmail,
                "Brand New User");
            var cb9b = await InvokeCallbackAsync(brandNewEmailCookie);
            var autoCreatedUser = await userManager.FindByEmailAsync(brandNewEmail);

            var err9a = cb9a.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            var err9b = cb9b.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();

            Assert(
                "9. Unassociated Google account cannot auto-create or silently link by email (and avoids enumeration)",
                cb9a.Result is ViewResult &&
                cb9b.Result is ViewResult &&
                logins9a.Count == 0 &&
                autoCreatedUser == null &&
                !string.IsNullOrEmpty(err9a) &&
                err9a == err9b &&
                string.IsNullOrEmpty(ExtractSetCookiePair(cb9a.ResponseHeaders, appCookieName)) &&
                string.IsNullOrEmpty(ExtractSetCookiePair(cb9b.ResponseHeaders, appCookieName)) &&
                spyTelegram.Dispatched.Count == 0);

            // 10. Google external login preserves role/authorization rules (and fails closed on OTP delivery failure)
            var studentAuthOk = (await authzService.AuthorizeAsync(authTicket2.Principal!, "RequireStudent")).Succeeded;
            var adminAuthBlocked = !(await authzService.AuthorizeAsync(authTicket2.Principal!, "RequireAdmin")).Succeeded;
            var teacherAuthBlocked = !(await authzService.AuthorizeAsync(authTicket2.Principal!, "RequireTeacher")).Succeeded;
            var staffAuthBlocked = !(await authzService.AuthorizeAsync(authTicket2.Principal!, "RequireCourseStaff")).Succeeded;

            spyTelegram.ShouldSucceed = false;
            var extCookie10Fail = await IssueExternalCookieAsync("Google", $"google-sub-admin-{suffix}", adminUser.Email, adminUser.FullName);
            var cb10Fail = await InvokeCallbackAsync(extCookie10Fail);
            var pending10Fail = ExtractSetCookiePair(cb10Fail.ResponseHeaders, "AUB_PendingVerification");
            var app10Fail = ExtractSetCookiePair(cb10Fail.ResponseHeaders, appCookieName);

            spyTelegram.ShouldSucceed = true;
            var extCookie10Admin = await IssueExternalCookieAsync("Google", $"google-sub-admin-{suffix}", adminUser.Email, adminUser.FullName);
            var cb10Admin = await InvokeCallbackAsync(extCookie10Admin);
            var pending10Admin = ExtractSetCookiePair(cb10Admin.ResponseHeaders, "AUB_PendingVerification");
            var otp10Admin = spyTelegram.Dispatched.Last().Code;
            var verify10Admin = await InvokeVerifyTelegramAsync(pending10Admin, new VerifyTelegramViewModel
            {
                UserId = adminUser.Id,
                Code = otp10Admin
            });
            var appCookie10Admin = ExtractSetCookiePair(verify10Admin.ResponseHeaders, appCookieName);
            var adminTicket = await AuthenticateAppCookieAsync(appCookie10Admin);
            var adminAuthOk = adminTicket.Succeeded && (await authzService.AuthorizeAsync(adminTicket.Principal!, "RequireAdmin")).Succeeded;

            Assert(
                "10. Google OAuth enforces existing roles/authorization policies and fails closed on OTP delivery failure",
                studentAuthOk && adminAuthBlocked && teacherAuthBlocked && staffAuthBlocked &&
                cb10Fail.Result is ViewResult &&
                string.IsNullOrEmpty(pending10Fail) &&
                string.IsNullOrEmpty(app10Fail) &&
                verify10Admin.Result is RedirectResult rr10 &&
                (rr10.Url == "/Admin/Index" || rr10.Url == "/Admin") &&
                adminAuthOk,
                $"studentAuth={studentAuthOk}, adminBlocked={adminAuthBlocked}, rr10={ (verify10Admin.Result as RedirectResult)?.Url }, adminAuth={adminAuthOk}");

            // 11-14. SeedData hardening tests in an isolated temporary SQL Server database
            var baseConnStr = configuration.GetConnectionString("DefaultConnection")
                ?? context.Database.GetConnectionString()
                ?? throw new InvalidOperationException("DefaultConnection is missing.");

            var tempDbName = $"AUBDigitalAcademy_SeedTest_{suffix}";
            var csBuilder = new SqlConnectionStringBuilder(baseConnStr)
            {
                InitialCatalog = tempDbName
            };

            var seedServices = new ServiceCollection();
            seedServices.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
            seedServices.AddDbContext<ApplicationDbContext>(opts => opts.UseSqlServer(csBuilder.ConnectionString));
            seedServices.AddIdentity<User, IdentityRole>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;
                options.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

            await using var seedProvider = seedServices.BuildServiceProvider();
            try
            {
                using (var initScope = seedProvider.CreateScope())
                {
                    var initDb = initScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    await initDb.Database.MigrateAsync();
                    await SeedData.InitializeAsync(initScope.ServiceProvider, allowDevTaGrading: true);
                }

                // 11. Seed initialization does not change an existing user's PasswordHash
                string adminHashAfterChange;
                string teacherHashAfterChange;
                using (var s1 = seedProvider.CreateScope())
                {
                    var um = s1.ServiceProvider.GetRequiredService<UserManager<User>>();
                    var seededAdmin = await um.FindByEmailAsync("admin@aub.com")
                        ?? throw new InvalidOperationException("Seeded admin missing in isolated DB.");
                    var seededTeacher = await um.FindByEmailAsync("teacher@aub.com")
                        ?? throw new InvalidOperationException("Seeded teacher missing in isolated DB.");

                    var chg1 = await um.ChangePasswordAsync(seededAdmin, "admin123", "CustomAdmin!2026");
                    var chg2 = await um.ChangePasswordAsync(seededTeacher, "teacher123", "CustomTeacher!2026");
                    if (!chg1.Succeeded || !chg2.Succeeded)
                    {
                        throw new InvalidOperationException("Failed to change password in isolated seed test DB.");
                    }

                    adminHashAfterChange = seededAdmin.PasswordHash!;
                    teacherHashAfterChange = seededTeacher.PasswordHash!;
                }

                using (var s2 = seedProvider.CreateScope())
                {
                    await SeedData.InitializeAsync(s2.ServiceProvider, allowDevTaGrading: true);
                }

                using (var s3 = seedProvider.CreateScope())
                {
                    var um = s3.ServiceProvider.GetRequiredService<UserManager<User>>();
                    var recheckedAdmin = await um.FindByEmailAsync("admin@aub.com");
                    var recheckedTeacher = await um.FindByEmailAsync("teacher@aub.com");

                    Assert(
                        "11. Seed initialization does not overwrite existing user PasswordHash",
                        recheckedAdmin != null &&
                        recheckedTeacher != null &&
                        recheckedAdmin.PasswordHash == adminHashAfterChange &&
                        recheckedTeacher.PasswordHash == teacherHashAfterChange &&
                        await um.CheckPasswordAsync(recheckedAdmin, "CustomAdmin!2026") &&
                        !await um.CheckPasswordAsync(recheckedAdmin, "admin123") &&
                        await um.CheckPasswordAsync(recheckedTeacher, "CustomTeacher!2026") &&
                        !await um.CheckPasswordAsync(recheckedTeacher, "teacher123"));
                }

                // 12. Seed initialization is idempotent across repeated startup calls
                for (int i = 0; i < 3; i++)
                {
                    using var repeatScope = seedProvider.CreateScope();
                    await SeedData.InitializeAsync(repeatScope.ServiceProvider, allowDevTaGrading: true);
                }

                using (var s4 = seedProvider.CreateScope())
                {
                    var db = s4.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    Assert(
                        "12. Seed initialization is idempotent across repeated startup calls",
                        await db.Users.CountAsync() == 4 &&
                        await db.Roles.CountAsync() == 4 &&
                        await db.Categories.CountAsync() == 2 &&
                        await db.Majors.CountAsync() == 6 &&
                        await db.Semesters.CountAsync() == 2 &&
                        await db.Courses.CountAsync() == 4 &&
                        await db.CourseAssistants.CountAsync() == 1);
                }

                // 13. Seed initialization does not reactivate an existing disabled account or recreate deleted users
                using (var s5 = seedProvider.CreateScope())
                {
                    var um = s5.ServiceProvider.GetRequiredService<UserManager<User>>();
                    var seededStudent = await um.FindByEmailAsync("student@aub.com")
                        ?? throw new InvalidOperationException("Seeded student missing.");
                    seededStudent.IsActive = false;
                    seededStudent.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
                    seededStudent.AccessFailedCount = 4;
                    await um.UpdateAsync(seededStudent);

                    var seededTa = await um.FindByEmailAsync("ta@aub.com")
                        ?? throw new InvalidOperationException("Seeded TA missing.");
                    await um.DeleteAsync(seededTa);
                }

                using (var s6 = seedProvider.CreateScope())
                {
                    await SeedData.InitializeAsync(s6.ServiceProvider, allowDevTaGrading: true);
                }

                using (var s7 = seedProvider.CreateScope())
                {
                    var um = s7.ServiceProvider.GetRequiredService<UserManager<User>>();
                    var recheckedStudent = await um.FindByEmailAsync("student@aub.com");
                    var recheckedTa = await um.FindByEmailAsync("ta@aub.com");

                    Assert(
                        "13. Seed initialization does not reactivate disabled/locked accounts or recreate deleted users",
                        recheckedStudent != null &&
                        !recheckedStudent.IsActive &&
                        recheckedStudent.LockoutEnd.HasValue &&
                        recheckedStudent.AccessFailedCount == 4 &&
                        recheckedTa == null);
                }

                // 14. Existing user profile fields and roles remain unchanged during normal seeding
                using (var s8 = seedProvider.CreateScope())
                {
                    var um = s8.ServiceProvider.GetRequiredService<UserManager<User>>();
                    var seededTeacher = await um.FindByEmailAsync("teacher@aub.com")
                        ?? throw new InvalidOperationException("Seeded teacher missing.");
                    seededTeacher.FullName = "Dr. Modified Teacher";
                    seededTeacher.IsTelegramVerified = false;
                    await um.UpdateAsync(seededTeacher);
                    await um.AddToRoleAsync(seededTeacher, "Assistance");
                }

                using (var s9 = seedProvider.CreateScope())
                {
                    await SeedData.InitializeAsync(s9.ServiceProvider, allowDevTaGrading: true);
                }

                using (var s10 = seedProvider.CreateScope())
                {
                    var um = s10.ServiceProvider.GetRequiredService<UserManager<User>>();
                    var recheckedTeacher = await um.FindByEmailAsync("teacher@aub.com");
                    var roles = recheckedTeacher != null ? await um.GetRolesAsync(recheckedTeacher) : Array.Empty<string>();

                    Assert(
                        "14. Existing user profile fields, Telegram flags, and roles remain unchanged during normal seeding",
                        recheckedTeacher != null &&
                        recheckedTeacher.FullName == "Dr. Modified Teacher" &&
                        !recheckedTeacher.IsTelegramVerified &&
                        roles.Contains("Teacher") &&
                        roles.Contains("Assistance"));
                }
            }
            finally
            {
                using var cleanupScope = seedProvider.CreateScope();
                var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await cleanupDb.Database.EnsureDeletedAsync();
            }
        }
        finally
        {
            using var cleanupScope = serviceProvider.CreateScope();
            var cleanupCtx = cleanupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cleanupUm = cleanupScope.ServiceProvider.GetRequiredService<UserManager<User>>();

            foreach (var uid in createdUserIds)
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
