using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AUBDigitalAcademy.Data;

public static class RegistrationTelegramSecurityTestRunner
{
    private sealed record DispatchedCode(string UniversityId, string StudentName, string Code, string? ChatId);

    private sealed class SpyTelegramService : ITelegramService
    {
        private readonly ITelegramService _inner;
        public List<DispatchedCode> Dispatched { get; } = new();
        public List<(long ChatId, string Text)> SentMessages { get; } = new();
        public bool ShouldSucceed { get; set; } = true;

        public SpyTelegramService(ITelegramService inner)
        {
            _inner = inner;
        }

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
            => _inner.TestBotConnectivityAsync();

        public Task<bool> SendMessageAsync(long chatId, string text, string parseMode = "HTML", CancellationToken cancellationToken = default)
        {
            SentMessages.Add((chatId, text));
            return Task.FromResult(ShouldSucceed);
        }

        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset = null, int timeoutSeconds = 20, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TelegramUpdate>>(Array.Empty<TelegramUpdate>());

        public Task<(string LinkUrl, string PlainToken, DateTime ExpiresAt)> GenerateLinkTokenAsync(string userId, string? clientIp = null)
            => _inner.GenerateLinkTokenAsync(userId, clientIp);

        public Task<(bool Success, string Message, User? User)> ProcessLinkTokenAsync(string plainToken, long chatId, string? telegramUsername)
            => _inner.ProcessLinkTokenAsync(plainToken, chatId, telegramUsername);

        public Task<bool> IsAccountLinkedAsync(string userId)
            => _inner.IsAccountLinkedAsync(userId);
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"ok\":false,\"description\":\"Simulated Telegram API outage\"}")
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

    private sealed record UserSnapshot(
        string Id,
        string? PasswordHash,
        bool IsTelegramVerified,
        long? TelegramChatId,
        bool IsActive,
        string RolesCsv);

    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("  REGISTRATION & TELEGRAM LINK SECURITY INTEGRATION TEST SUITE   ");
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
        var authzService = sp.GetRequiredService<IAuthorizationService>();
        var appCookieName = cookieOpts.Get(IdentityConstants.ApplicationScheme).Cookie.Name ?? ".AspNetCore.Identity.Application";

        var realTelegram = sp.GetRequiredService<ITelegramService>();
        var spyTelegram = new SpyTelegramService(realTelegram);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createdUserIds = new List<string>();
        const string testPassword = "RegSec@2026!";

        // Snapshot existing users so we can verify zero unintended side effects
        var existingUsersBefore = await userManager.Users.AsNoTracking().ToListAsync();
        var existingSnapshots = new List<UserSnapshot>();
        foreach (var eu in existingUsersBefore)
        {
            var roles = await userManager.GetRolesAsync(eu);
            existingSnapshots.Add(new UserSnapshot(
                eu.Id,
                eu.PasswordHash,
                eu.IsTelegramVerified,
                eu.TelegramChatId,
                eu.IsActive,
                string.Join(",", roles.OrderBy(r => r, StringComparer.Ordinal))));
        }

        AccountController CreateController(
            IServiceProvider rsp,
            ITelegramService telegramImpl,
            string? cookieHeader = null,
            bool isHttps = true,
            string? queryString = null)
        {
            var reqCtx = rsp.GetRequiredService<ApplicationDbContext>();
            var reqUm = rsp.GetRequiredService<UserManager<User>>();
            var reqSm = rsp.GetRequiredService<SignInManager<User>>();
            var reqDp = rsp.GetRequiredService<IDataProtectionProvider>();
            var reqCfg = rsp.GetRequiredService<IConfiguration>();
            var reqLf = rsp.GetRequiredService<ILoggerFactory>();
            var reqTdp = rsp.GetRequiredService<ITempDataProvider>();

            var authService = new AuthService(
                reqUm, reqCtx, telegramImpl, reqDp, reqCfg, reqLf.CreateLogger<AuthService>());

            var controller = new AccountController(
                reqSm, reqUm, reqCtx, telegramImpl, authService, reqDp, reqLf.CreateLogger<AccountController>());

            var httpContext = new DefaultHttpContext { RequestServices = rsp };
            httpContext.Request.Scheme = isHttps ? "https" : "http";
            httpContext.Request.Host = new HostString("localhost");
            if (!string.IsNullOrEmpty(queryString))
            {
                httpContext.Request.QueryString = new QueryString(queryString);
            }
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
            return controller;
        }

        async Task<(TResult Result, IHeaderDictionary Headers, ModelStateDictionary ModelState, ITempDataDictionary TempData, bool Authenticated)>
            InvokeControllerAsync<TResult>(
                ITelegramService telegramImpl,
                Func<AccountController, Task<TResult>> action,
                string? cookieHeader = null,
                bool isHttps = true,
                string? queryString = null)
        {
            using var reqScope = serviceProvider.CreateScope();
            var controller = CreateController(reqScope.ServiceProvider, telegramImpl, cookieHeader, isHttps, queryString);
            var res = await action(controller);
            return (res, controller.Response.Headers, controller.ModelState, controller.TempData, controller.HttpContext.User.Identity?.IsAuthenticated ?? false);
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

        static string? ExtractSetCookieRaw(IHeaderDictionary headers, string cookieName)
        {
            var prefix = cookieName + "=";
            string? matched = null;
            foreach (var raw in headers.SetCookie)
            {
                if (!string.IsNullOrEmpty(raw) && raw.StartsWith(prefix, StringComparison.Ordinal))
                {
                    matched = raw;
                }
            }
            return matched;
        }

        static string? ExtractSetCookiePair(IHeaderDictionary headers, string cookieName)
        {
            var raw = ExtractSetCookieRaw(headers, cookieName);
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            var pair = raw.Split(';', 2)[0];
            var value = pair[(cookieName.Length + 1)..];
            return string.IsNullOrEmpty(value) ? null : pair;
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

        static int GetStatusCode(IActionResult result) => result switch
        {
            UnauthorizedObjectResult => StatusCodes.Status401Unauthorized,
            UnauthorizedResult => StatusCodes.Status401Unauthorized,
            JsonResult jr => jr.StatusCode ?? StatusCodes.Status200OK,
            ObjectResult or => or.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult scr => scr.StatusCode,
            _ => 0
        };

        static bool? ReadLinkedJson(IActionResult result)
        {
            object? val = result switch
            {
                JsonResult jr => jr.Value,
                ObjectResult or => or.Value,
                _ => null
            };
            if (val == null) return null;
            var json = JsonSerializer.Serialize(val);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("linked", out var p) ? p.GetBoolean() : null;
        }

        try
        {
            var baseChatId = Random.Shared.NextInt64(6_000_000_000L, 6_999_999_999L);

            var studentUid1 = $"RSEC1_{suffix}";
            var studentUid2 = $"RSEC2_{suffix}";
            var studentUid3 = $"RSEC3_{suffix}";
            var studentUid4 = $"RSEC4_{suffix}";
            var studentUid5 = $"RSEC5_{suffix}";

            // ─────────────────────────────────────────────────────────────
            // Tests 1–4: Registration behavior before Telegram link/OTP
            // ─────────────────────────────────────────────────────────────
            spyTelegram.Dispatched.Clear();
            var reg1 = await InvokeControllerAsync(spyTelegram, c => c.Register(new RegisterViewModel
            {
                FullName = "Reg Sec One",
                UniversityId = studentUid1,
                Password = testPassword,
                ConfirmPassword = testPassword
            }), isHttps: true);

            var registeredUser1 = await userManager.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UniversityId == studentUid1);
            if (registeredUser1 != null)
            {
                createdUserIds.Add(registeredUser1.Id);
            }

            var reg1Redirect = reg1.Result as RedirectToActionResult;
            var pendingRaw1 = ExtractSetCookieRaw(reg1.Headers, "AUB_PendingVerification");
            var pendingCookie1 = ExtractSetCookiePair(reg1.Headers, "AUB_PendingVerification");
            var appCookie1 = ExtractSetCookiePair(reg1.Headers, appCookieName);
            var otpRowsAfterReg1 = registeredUser1 != null
                ? await context.TelegramVerificationRequests.AsNoTracking().CountAsync(r => r.UserId == registeredUser1.Id)
                : -1;

            Assert(
                "1. Registration with no Telegram link redirects to LinkTelegram and sets secure pending cookie",
                reg1Redirect?.ActionName == "LinkTelegram" &&
                !string.IsNullOrEmpty(pendingCookie1) &&
                pendingRaw1 != null &&
                pendingRaw1.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
                pendingRaw1.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase) &&
                pendingRaw1.Contains("secure", StringComparison.OrdinalIgnoreCase),
                $"action={reg1Redirect?.ActionName}, raw={pendingRaw1}");

            Assert(
                "2. Registration does not dispatch OTP when TelegramChatId is null",
                spyTelegram.Dispatched.Count == 0 && otpRowsAfterReg1 == 0,
                $"dispatched={spyTelegram.Dispatched.Count}, otpRows={otpRowsAfterReg1}");

            Assert(
                "3. New registration does not set IsTelegramVerified prematurely",
                registeredUser1 != null &&
                !registeredUser1.IsTelegramVerified &&
                !registeredUser1.TelegramChatId.HasValue);

            Assert(
                "4. Registration does not issue an authenticated cookie before verification",
                string.IsNullOrEmpty(appCookie1) && !reg1.Authenticated);

            // Also verify VerifyTelegram GET and ResendTelegramCode POST cannot dispatch OTP when TelegramChatId is null
            spyTelegram.Dispatched.Clear();
            var directVerifyGetUnlinked = await InvokeControllerAsync(
                spyTelegram,
                c => c.VerifyTelegram(registeredUser1!.Id),
                cookieHeader: pendingCookie1);
            var directResendUnlinked = await InvokeControllerAsync(
                spyTelegram,
                c => c.ResendTelegramCode(registeredUser1!.Id),
                cookieHeader: pendingCookie1);

            Assert(
                "4b. Direct VerifyTelegram GET / ResendTelegramCode while unlinked redirect to LinkTelegram and dispatch zero OTPs",
                (directVerifyGetUnlinked.Result as RedirectToActionResult)?.ActionName == "LinkTelegram" &&
                (directResendUnlinked.Result as RedirectToActionResult)?.ActionName == "LinkTelegram" &&
                spyTelegram.Dispatched.Count == 0 &&
                string.IsNullOrEmpty(ExtractSetCookiePair(directVerifyGetUnlinked.Headers, appCookieName)));

            // ─────────────────────────────────────────────────────────────
            // Tests 11–14: CheckTelegramLinkStatus hardening (before linking)
            // ─────────────────────────────────────────────────────────────
            // 11. Missing pending state
            var statusMissing = await InvokeControllerAsync(
                spyTelegram,
                c => c.CheckTelegramLinkStatus(registeredUser1!.Id),
                cookieHeader: null);
            Assert(
                "11. CheckTelegramLinkStatus rejects missing pending state with 401",
                GetStatusCode(statusMissing.Result) == StatusCodes.Status401Unauthorized &&
                ReadLinkedJson(statusMissing.Result) == false);

            // 12. Expired pending state
            var protector = dataProtection.CreateProtector("AUBDigitalAcademy.TelegramVerification").ToTimeLimitedDataProtector();
            var expiredPayload = JsonSerializer.Serialize(new
            {
                UserId = registeredUser1!.Id,
                RememberMe = false,
                ReturnUrl = (string?)null,
                AuthMethod = "Registration",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                SessionNonce = Guid.NewGuid().ToString("N")
            });
            var expiredToken = protector.Protect(expiredPayload, TimeSpan.FromMinutes(5));
            var statusExpired = await InvokeControllerAsync(
                spyTelegram,
                c => c.CheckTelegramLinkStatus(registeredUser1.Id),
                cookieHeader: $"AUB_PendingVerification={expiredToken}");
            Assert(
                "12. CheckTelegramLinkStatus rejects expired pending state with 401 and clears cookie",
                GetStatusCode(statusExpired.Result) == StatusCodes.Status401Unauthorized &&
                ReadLinkedJson(statusExpired.Result) == false &&
                HasSetCookieDeletion(statusExpired.Headers, "AUB_PendingVerification"));

            // 13. Mismatched UserId (and no enumeration difference between existing other user vs nonexistent user)
            var regOther = await InvokeControllerAsync(spyTelegram, c => c.Register(new RegisterViewModel
            {
                FullName = "Reg Sec Two",
                UniversityId = studentUid2,
                Password = testPassword,
                ConfirmPassword = testPassword
            }));
            var registeredUser2 = await userManager.Users.AsNoTracking().FirstAsync(u => u.UniversityId == studentUid2);
            createdUserIds.Add(registeredUser2.Id);

            var statusMismatchedReal = await InvokeControllerAsync(
                spyTelegram,
                c => c.CheckTelegramLinkStatus(registeredUser2.Id),
                cookieHeader: pendingCookie1);
            var statusMismatchedFake = await InvokeControllerAsync(
                spyTelegram,
                c => c.CheckTelegramLinkStatus("nonexistent-user-id-0000"),
                cookieHeader: pendingCookie1);

            Assert(
                "13. CheckTelegramLinkStatus rejects mismatched UserId uniformly without revealing unrelated accounts",
                GetStatusCode(statusMismatchedReal.Result) == StatusCodes.Status401Unauthorized &&
                GetStatusCode(statusMismatchedFake.Result) == StatusCodes.Status401Unauthorized &&
                ReadLinkedJson(statusMismatchedReal.Result) == false &&
                ReadLinkedJson(statusMismatchedFake.Result) == false);

            // 14. Tampered pending state & client-supplied verification flags ignored
            var tamperedToken = pendingCookie1! + "TAMPERED";
            var statusTampered = await InvokeControllerAsync(
                spyTelegram,
                c => c.CheckTelegramLinkStatus(registeredUser1.Id),
                cookieHeader: tamperedToken);
            var statusClientFlags = await InvokeControllerAsync(
                spyTelegram,
                c => c.CheckTelegramLinkStatus(registeredUser1.Id),
                cookieHeader: pendingCookie1,
                queryString: $"?userId={registeredUser1.Id}&linked=true&isTelegramVerified=true&telegramChatId=99999999");

            Assert(
                "14. CheckTelegramLinkStatus rejects tampered state and ignores client-supplied verification flags",
                GetStatusCode(statusTampered.Result) == StatusCodes.Status401Unauthorized &&
                ReadLinkedJson(statusTampered.Result) == false &&
                HasSetCookieDeletion(statusTampered.Headers, "AUB_PendingVerification") &&
                GetStatusCode(statusClientFlags.Result) == StatusCodes.Status200OK &&
                ReadLinkedJson(statusClientFlags.Result) == false);

            // ─────────────────────────────────────────────────────────────
            // Tests 5–10: Telegram Linking Security & Bot Polling Checks
            // ─────────────────────────────────────────────────────────────
            // Navigate to LinkTelegram to generate real link token
            var linkGet1 = await InvokeControllerAsync(
                spyTelegram,
                c => c.LinkTelegram(registeredUser1.Id),
                cookieHeader: pendingCookie1);
            var linkView1 = linkGet1.Result as ViewResult;
            var plainToken1 = linkView1?.ViewData["PlainToken"] as string;

            // 6. Invalid challenge & untrusted chat/bot updates rejected
            var invalidLinkRes = await realTelegram.ProcessLinkTokenAsync("invalid-nonexistent-token-xyz", baseChatId, "baduser");
            var emptyLinkRes = await realTelegram.ProcessLinkTokenAsync("   ", baseChatId, "baduser");

            using (var pollScope = serviceProvider.CreateScope())
            {
                var polling = new TelegramPollingService(
                    pollScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                    pollScope.ServiceProvider.GetRequiredService<IConfiguration>(),
                    NullLogger<TelegramPollingService>.Instance);

                // Group chat attempt (negative chat id + type group)
                await polling.HandleUpdateAsync(spyTelegram, new TelegramUpdate
                {
                    UpdateId = 900001,
                    Message = new TelegramMessage
                    {
                        Chat = new TelegramChat { Id = -100123456789, Type = "group" },
                        From = new TelegramUser { Id = 111111, IsBot = false, FirstName = "GroupUser" },
                        Text = $"/start link_{plainToken1}"
                    }
                });

                // Bot sender attempt
                await polling.HandleUpdateAsync(spyTelegram, new TelegramUpdate
                {
                    UpdateId = 900002,
                    Message = new TelegramMessage
                    {
                        Chat = new TelegramChat { Id = baseChatId, Type = "private" },
                        From = new TelegramUser { Id = baseChatId, IsBot = true, FirstName = "EvilBot" },
                        Text = $"/link {plainToken1}"
                    }
                });
            }

            var user1AfterBadLinks = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser1.Id);
            Assert(
                "6. Invalid linking challenge, group chat linking, and bot sender linking are rejected",
                !invalidLinkRes.Success &&
                !emptyLinkRes.Success &&
                !user1AfterBadLinks.TelegramChatId.HasValue &&
                !user1AfterBadLinks.IsTelegramVerified);

            // 7. Expired linking challenge rejected
            var (_, expiredChallengeToken, _) = await realTelegram.GenerateLinkTokenAsync(registeredUser2.Id, "127.0.0.1");
            var expHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expiredChallengeToken))).ToLowerInvariant();
            var expTokenRow = await context.TelegramLinkTokens.FirstAsync(t => t.TokenHash == expHash);
            expTokenRow.ExpiresAt = DateTime.UtcNow.AddMinutes(-2);
            await context.SaveChangesAsync();

            var expiredLinkRes = await realTelegram.ProcessLinkTokenAsync(expiredChallengeToken, baseChatId + 50, "expuser");
            var user2AfterExp = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser2.Id);
            Assert(
                "7. Expired linking challenge is rejected",
                !expiredLinkRes.Success && !user2AfterExp.TelegramChatId.HasValue);

            // 5. Valid Telegram linking via polling updates ONLY intended user and does NOT set IsTelegramVerified=true
            using (var pollScope = serviceProvider.CreateScope())
            {
                var polling = new TelegramPollingService(
                    pollScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                    pollScope.ServiceProvider.GetRequiredService<IConfiguration>(),
                    NullLogger<TelegramPollingService>.Instance);

                await polling.HandleUpdateAsync(spyTelegram, new TelegramUpdate
                {
                    UpdateId = 900010,
                    Message = new TelegramMessage
                    {
                        Chat = new TelegramChat { Id = baseChatId, Type = "private" },
                        From = new TelegramUser { Id = baseChatId, IsBot = false, FirstName = "RegSec", Username = "regsec_one" },
                        Text = $"/start link_{plainToken1}"
                    }
                });

                // Duplicate UpdateId is ignored idempotently
                await polling.HandleUpdateAsync(spyTelegram, new TelegramUpdate
                {
                    UpdateId = 900010,
                    Message = new TelegramMessage
                    {
                        Chat = new TelegramChat { Id = baseChatId, Type = "private" },
                        From = new TelegramUser { Id = baseChatId, IsBot = false, FirstName = "RegSec", Username = "regsec_one" },
                        Text = $"/start link_{plainToken1}"
                    }
                });
            }

            var user1Linked = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser1.Id);
            var user2Unchanged = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser2.Id);
            Assert(
                "5. Valid Telegram linking updates only intended user and keeps IsTelegramVerified=false until OTP",
                user1Linked.TelegramChatId == baseChatId &&
                user1Linked.TelegramUsername == "regsec_one" &&
                !user1Linked.IsTelegramVerified &&
                !user2Unchanged.TelegramChatId.HasValue);

            // 8. Reused linking challenge is rejected
            var reusedLinkRes = await realTelegram.ProcessLinkTokenAsync(plainToken1!, baseChatId + 99, "attacker");
            var user1AfterReuse = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser1.Id);
            Assert(
                "8. Reused linking challenge is rejected",
                !reusedLinkRes.Success && user1AfterReuse.TelegramChatId == baseChatId);

            // 9. Telegram account already associated with another user cannot be linked
            var (_, tokenUser2, _) = await realTelegram.GenerateLinkTokenAsync(registeredUser2.Id, "127.0.0.1");
            var duplicateChatLinkRes = await realTelegram.ProcessLinkTokenAsync(tokenUser2, baseChatId, "regsec_one");
            var user2AfterDupChat = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser2.Id);
            var user1AfterDupChat = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser1.Id);
            Assert(
                "9. Telegram account already associated with another user cannot be linked",
                !duplicateChatLinkRes.Success &&
                !user2AfterDupChat.TelegramChatId.HasValue &&
                user1AfterDupChat.TelegramChatId == baseChatId);

            // 10. Existing Telegram association cannot be silently overwritten
            bool generateBlockedForLinked = false;
            try
            {
                await realTelegram.GenerateLinkTokenAsync(registeredUser1.Id, "127.0.0.1");
            }
            catch (InvalidOperationException)
            {
                generateBlockedForLinked = true;
            }

            // Even if a stale unused token existed in DB, ProcessLinkTokenAsync must refuse overwriting TelegramChatId
            var forgedOverwritePlain = "forged_overwrite_token_" + suffix;
            var forgedOverwriteHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(forgedOverwritePlain))).ToLowerInvariant();
            context.TelegramLinkTokens.Add(new TelegramLinkToken
            {
                UserId = registeredUser1.Id,
                TokenHash = forgedOverwriteHash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                IsUsed = false
            });
            await context.SaveChangesAsync();

            var overwriteAttempt = await realTelegram.ProcessLinkTokenAsync(forgedOverwritePlain, baseChatId + 777, "attacker2");
            var user1AfterOverwrite = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser1.Id);
            Assert(
                "10. Existing Telegram association cannot be silently overwritten",
                generateBlockedForLinked &&
                !overwriteAttempt.Success &&
                user1AfterOverwrite.TelegramChatId == baseChatId);

            // ─────────────────────────────────────────────────────────────
            // Tests 15–19: OTP Dispatch & Verification Flow
            // ─────────────────────────────────────────────────────────────
            var statusLinked = await InvokeControllerAsync(
                spyTelegram,
                c => c.CheckTelegramLinkStatus(registeredUser1.Id),
                cookieHeader: pendingCookie1);

            spyTelegram.Dispatched.Clear();
            var verifyGet1 = await InvokeControllerAsync(
                spyTelegram,
                c => c.VerifyTelegram(registeredUser1.Id),
                cookieHeader: pendingCookie1);

            var dispatchedOtp1 = spyTelegram.Dispatched.SingleOrDefault();
            Assert(
                "15. OTP is dispatched only after successful Telegram linking to user's own TelegramChatId",
                GetStatusCode(statusLinked.Result) == StatusCodes.Status200OK &&
                ReadLinkedJson(statusLinked.Result) == true &&
                verifyGet1.Result is ViewResult &&
                spyTelegram.Dispatched.Count == 1 &&
                dispatchedOtp1?.ChatId == baseChatId.ToString() &&
                dispatchedOtp1?.Code.Length == 6 &&
                string.IsNullOrEmpty(ExtractSetCookiePair(verifyGet1.Headers, appCookieName)));

            // 16. Invalid OTP does not complete registration
            var firstCode = dispatchedOtp1?.Code ?? "123456";
            var wrongOtp = firstCode == "000000" ? "999999" : "000000";
            var verifyBadOtp = await InvokeControllerAsync(
                spyTelegram,
                c => c.VerifyTelegram(new VerifyTelegramViewModel
                {
                    UserId = registeredUser1.Id,
                    Code = wrongOtp
                }),
                cookieHeader: pendingCookie1);
            var user1AfterBadOtp = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser1.Id);
            Assert(
                "16. Invalid OTP does not complete registration and issues no auth cookie",
                verifyBadOtp.Result is ViewResult &&
                !verifyBadOtp.ModelState.IsValid &&
                !user1AfterBadOtp.IsTelegramVerified &&
                string.IsNullOrEmpty(ExtractSetCookiePair(verifyBadOtp.Headers, appCookieName)));

            // 17. Expired OTP does not complete registration
            var activeOtpRow = await context.TelegramVerificationRequests
                .Where(r => r.UserId == registeredUser1.Id && !r.IsUsed)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();
            if (activeOtpRow != null)
            {
                activeOtpRow.ExpiresAt = DateTime.UtcNow.AddMinutes(-2);
                await context.SaveChangesAsync();
            }

            var verifyExpiredOtp = await InvokeControllerAsync(
                spyTelegram,
                c => c.VerifyTelegram(new VerifyTelegramViewModel
                {
                    UserId = registeredUser1.Id,
                    Code = firstCode
                }),
                cookieHeader: pendingCookie1);
            if (activeOtpRow != null)
            {
                await context.Entry(activeOtpRow).ReloadAsync();
            }
            var user1AfterExpiredOtp = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser1.Id);
            Assert(
                "17. Expired OTP does not complete registration and invalidates OTP request",
                verifyExpiredOtp.Result is ViewResult &&
                !verifyExpiredOtp.ModelState.IsValid &&
                (activeOtpRow?.IsUsed ?? false) &&
                !user1AfterExpiredOtp.IsTelegramVerified &&
                string.IsNullOrEmpty(ExtractSetCookiePair(verifyExpiredOtp.Headers, appCookieName)));

            // 19. Valid OTP completes registration successfully (dispatch fresh OTP via VerifyTelegram GET)
            spyTelegram.Dispatched.Clear();
            var verifyGetFresh = await InvokeControllerAsync(
                spyTelegram,
                c => c.VerifyTelegram(registeredUser1.Id),
                cookieHeader: pendingCookie1);
            var freshOtp1 = spyTelegram.Dispatched.SingleOrDefault()?.Code ?? "000000";

            var verifySuccess = await InvokeControllerAsync(
                spyTelegram,
                c => c.VerifyTelegram(new VerifyTelegramViewModel
                {
                    UserId = registeredUser1.Id,
                    Code = freshOtp1
                }),
                cookieHeader: pendingCookie1);

            var finalAppCookie = ExtractSetCookiePair(verifySuccess.Headers, appCookieName);
            var pendingClearedAfterSuccess = HasSetCookieDeletion(verifySuccess.Headers, "AUB_PendingVerification");
            var finalUser1 = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == registeredUser1.Id);
            var authTicket1 = await AuthenticateAppCookieAsync(finalAppCookie);
            var user1Roles = await userManager.GetRolesAsync(finalUser1);

            Assert(
                "19. Valid OTP completes registration successfully, sets IsTelegramVerified=true, clears pending state, issues Student cookie",
                verifySuccess.Result is RedirectResult rrSuccess &&
                rrSuccess.Url.Contains("/Student", StringComparison.OrdinalIgnoreCase) &&
                finalUser1.IsTelegramVerified &&
                pendingClearedAfterSuccess &&
                !string.IsNullOrEmpty(finalAppCookie) &&
                authTicket1.Succeeded &&
                authTicket1.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) == finalUser1.Id &&
                user1Roles.Count == 1 &&
                user1Roles[0] == "Student" &&
                (await authzService.AuthorizeAsync(authTicket1.Principal!, "RequireStudent")).Succeeded &&
                !(await authzService.AuthorizeAsync(authTicket1.Principal!, "RequireAdmin")).Succeeded,
                $"res={verifySuccess.Result.GetType().Name}, verified={finalUser1.IsTelegramVerified}, authOk={authTicket1.Succeeded}");

            // 18. Replayed OTP and replayed pending cookie cannot complete registration twice
            var replayStatusCheck = await InvokeControllerAsync(
                spyTelegram,
                c => c.CheckTelegramLinkStatus(registeredUser1.Id),
                cookieHeader: pendingCookie1);
            var replayVerifyPost = await InvokeControllerAsync(
                spyTelegram,
                c => c.VerifyTelegram(new VerifyTelegramViewModel
                {
                    UserId = registeredUser1.Id,
                    Code = freshOtp1
                }),
                cookieHeader: pendingCookie1);
            var directReplayVerifyService = await new AuthService(
                userManager,
                context,
                spyTelegram,
                dataProtection,
                sp.GetRequiredService<IConfiguration>(),
                NullLogger<AuthService>.Instance).VerifyWebTwoFactorAsync(registeredUser1.Id, freshOtp1);

            Assert(
                "18. Replayed OTP and replayed pending session are rejected and issue no auth cookie",
                GetStatusCode(replayStatusCheck.Result) == StatusCodes.Status401Unauthorized &&
                replayVerifyPost.Result is RedirectToActionResult rReplay &&
                rReplay.ActionName == "Login" &&
                string.IsNullOrEmpty(ExtractSetCookiePair(replayVerifyPost.Headers, appCookieName)) &&
                directReplayVerifyService.Status == TwoFactorVerifyStatus.AlreadyUsed);

            // ─────────────────────────────────────────────────────────────
            // Test 20: Telegram API failure fails closed
            // ─────────────────────────────────────────────────────────────
            var reg3 = await InvokeControllerAsync(spyTelegram, c => c.Register(new RegisterViewModel
            {
                FullName = "Reg Sec Three",
                UniversityId = studentUid3,
                Password = testPassword,
                ConfirmPassword = testPassword
            }));
            var user3 = await userManager.Users.AsNoTracking().FirstAsync(u => u.UniversityId == studentUid3);
            createdUserIds.Add(user3.Id);
            var pendingCookie3 = ExtractSetCookiePair(reg3.Headers, "AUB_PendingVerification");
            var (_, tokenUser3, _) = await realTelegram.GenerateLinkTokenAsync(user3.Id, "127.0.0.1");
            var linkRes3 = await realTelegram.ProcessLinkTokenAsync(tokenUser3, baseChatId + 3, "regsec_three");

            // Use real TelegramService wired to a failing HTTP 500 HttpClient
            var failingTelegramConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:BotToken"] = "123456:TEST_FAILING_BOT_TOKEN_1234567890" })
                .Build();
            using var failingHttpClient = new HttpClient(new FailingHttpMessageHandler());
            var failingTelegramService = new TelegramService(
                failingHttpClient,
                failingTelegramConfig,
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<TelegramService>.Instance,
                new StubHostEnvironment());

            var verifyGetApiFail = await InvokeControllerAsync(
                failingTelegramService,
                c => c.VerifyTelegram(user3.Id),
                cookieHeader: pendingCookie3);
            var user3AfterApiFail = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == user3.Id);
            var activeOtpAfterApiFail = await context.TelegramVerificationRequests.AsNoTracking()
                .CountAsync(r => r.UserId == user3.Id && !r.IsUsed);

            Assert(
                "20. Telegram API failure fails closed, invalidates OTP row, and does not mark user verified",
                linkRes3.Success &&
                verifyGetApiFail.TempData["ErrorMessage"] != null &&
                !user3AfterApiFail.IsTelegramVerified &&
                activeOtpAfterApiFail == 0 &&
                string.IsNullOrEmpty(ExtractSetCookiePair(verifyGetApiFail.Headers, appCookieName)));

            // ─────────────────────────────────────────────────────────────
            // Test 21: Concurrent linking requests cannot claim the same challenge twice
            // ─────────────────────────────────────────────────────────────
            var reg4 = await InvokeControllerAsync(spyTelegram, c => c.Register(new RegisterViewModel
            {
                FullName = "Reg Sec Four",
                UniversityId = studentUid4,
                Password = testPassword,
                ConfirmPassword = testPassword
            }));
            var user4 = await userManager.Users.AsNoTracking().FirstAsync(u => u.UniversityId == studentUid4);
            createdUserIds.Add(user4.Id);

            var (_, concurrentToken, _) = await realTelegram.GenerateLinkTokenAsync(user4.Id, "127.0.0.1");
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var t1 = Task.Run(async () =>
            {
                await gate.Task;
                return await realTelegram.ProcessLinkTokenAsync(concurrentToken, baseChatId + 41, "race_one");
            });
            var t2 = Task.Run(async () =>
            {
                await gate.Task;
                return await realTelegram.ProcessLinkTokenAsync(concurrentToken, baseChatId + 42, "race_two");
            });
            gate.SetResult(true);
            var raceResults = await Task.WhenAll(t1, t2);
            var winCount = raceResults.Count(r => r.Success);
            var user4AfterRace = await userManager.Users.AsNoTracking().FirstAsync(u => u.Id == user4.Id);

            Assert(
                "21. Concurrent linking requests cannot claim the same challenge twice (strictly 1 winner)",
                winCount == 1 &&
                (user4AfterRace.TelegramChatId == baseChatId + 41 || user4AfterRace.TelegramChatId == baseChatId + 42) &&
                !user4AfterRace.IsTelegramVerified);

            // ─────────────────────────────────────────────────────────────
            // Test 21b: Cancellation & inactive/locked account checks
            // ─────────────────────────────────────────────────────────────
            var reg5 = await InvokeControllerAsync(spyTelegram, c => c.Register(new RegisterViewModel
            {
                FullName = "Reg Sec Five",
                UniversityId = studentUid5,
                Password = testPassword,
                ConfirmPassword = testPassword
            }));
            var user5 = await userManager.Users.AsNoTracking().FirstAsync(u => u.UniversityId == studentUid5);
            createdUserIds.Add(user5.Id);
            var pendingCookie5 = ExtractSetCookiePair(reg5.Headers, "AUB_PendingVerification");
            var (_, token5, _) = await realTelegram.GenerateLinkTokenAsync(user5.Id, "127.0.0.1");

            var cancelRes = await InvokeControllerAsync(
                spyTelegram,
                c => c.CancelVerification(),
                cookieHeader: pendingCookie5);
            var statusAfterCancel = await InvokeControllerAsync(
                spyTelegram,
                c => c.CheckTelegramLinkStatus(user5.Id),
                cookieHeader: pendingCookie5);
            var linkAfterCancel = await realTelegram.ProcessLinkTokenAsync(token5, baseChatId + 55, "cancelled_user");

            Assert(
                "21b. CancelVerification clears pending cookie, revokes session replay, and invalidates unused link token",
                HasSetCookieDeletion(cancelRes.Headers, "AUB_PendingVerification") &&
                GetStatusCode(statusAfterCancel.Result) == StatusCodes.Status401Unauthorized &&
                !linkAfterCancel.Success);

            // ─────────────────────────────────────────────────────────────
            // Test 22: Existing users untouched + CSRF/HTTP verb metadata check
            // ─────────────────────────────────────────────────────────────
            // Attempt duplicate registration against an existing seeded user
            var dupSeedReg = await InvokeControllerAsync(spyTelegram, c => c.Register(new RegisterViewModel
            {
                FullName = "Overwrite Attempt",
                UniversityId = "STU001",
                Password = "NewBadPassword@123",
                ConfirmPassword = "NewBadPassword@123"
            }));

            bool allExistingUnchanged = true;
            foreach (var snap in existingSnapshots)
            {
                var current = await userManager.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == snap.Id);
                if (current == null)
                {
                    allExistingUnchanged = false;
                    break;
                }

                var currentRoles = string.Join(",", (await userManager.GetRolesAsync(current)).OrderBy(r => r, StringComparer.Ordinal));
                if (current.PasswordHash != snap.PasswordHash ||
                    current.IsTelegramVerified != snap.IsTelegramVerified ||
                    current.TelegramChatId != snap.TelegramChatId ||
                    current.IsActive != snap.IsActive ||
                    currentRoles != snap.RolesCsv)
                {
                    allExistingUnchanged = false;
                    break;
                }
            }

            var accountType = typeof(AccountController);
            bool registerPostCsrf = accountType.GetMethod(nameof(AccountController.Register), new[] { typeof(RegisterViewModel) })!
                .GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any();
            bool verifyPostCsrf = accountType.GetMethod(nameof(AccountController.VerifyTelegram), new[] { typeof(VerifyTelegramViewModel) })!
                .GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any();
            bool resendPostCsrf = accountType.GetMethod(nameof(AccountController.ResendTelegramCode), new[] { typeof(string) })!
                .GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any();
            bool cancelPostCsrf = accountType.GetMethod(nameof(AccountController.CancelVerification))!
                .GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any();
            bool checkStatusIsGet = accountType.GetMethod(nameof(AccountController.CheckTelegramLinkStatus))!
                .GetCustomAttributes<HttpGetAttribute>().Any() &&
                !accountType.GetMethod(nameof(AccountController.CheckTelegramLinkStatus))!
                .GetCustomAttributes<HttpPostAttribute>().Any();

            Assert(
                "22. Existing users (PasswordHash, roles, Telegram links) remain unchanged and CSRF/HTTP methods are enforced",
                !dupSeedReg.ModelState.IsValid &&
                allExistingUnchanged &&
                registerPostCsrf &&
                verifyPostCsrf &&
                resendPostCsrf &&
                cancelPostCsrf &&
                checkStatusIsGet);
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
