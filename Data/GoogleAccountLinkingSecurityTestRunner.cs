using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using AUBDigitalAcademy.Controllers;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Implementations;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AUBDigitalAcademy.Data;

public static class GoogleAccountLinkingSecurityTestRunner
{
    private sealed record DispatchedCode(string Kind, string OperationLabel, string UniversityId, string Name, string Code, string? ChatId);

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

            Dispatched.Add(new DispatchedCode("Login", "Login", universityId, studentName, verificationCode, customChatId));
            return Task.FromResult(true);
        }

        public Task<bool> SendGoogleSecurityOtpAsync(string universityId, string fullName, string verificationCode, string operationLabel, string customChatId)
        {
            if (!ShouldSucceed || string.IsNullOrWhiteSpace(customChatId))
            {
                return Task.FromResult(false);
            }

            Dispatched.Add(new DispatchedCode("GoogleSecurity", operationLabel, universityId, fullName, verificationCode, customChatId));
            return Task.FromResult(true);
        }

        public Task<bool> SendPasswordResetCodeAsync(string universityId, string fullName, string verificationCode, string customChatId)
        {
            if (!ShouldSucceed || string.IsNullOrWhiteSpace(customChatId))
            {
                return Task.FromResult(false);
            }

            Dispatched.Add(new DispatchedCode("Reset", "Reset", universityId, fullName, verificationCode, customChatId));
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

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public StubUrlHelper(ActionContext actionContext) => ActionContext = actionContext;
        public ActionContext ActionContext { get; }
        public string? Action(UrlActionContext actionContext) => $"/{actionContext.Controller ?? "Settings"}/{actionContext.Action ?? "LinkGoogleCallback"}";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//");
        public string? Link(string? routeName, object? values) => "/";
        public string? RouteUrl(UrlRouteContext routeContext) => "/";
    }

    private sealed class TestGoogleConfiguredSignInManager : SignInManager<User>
    {
        private readonly bool _googleEnabled;

        public TestGoogleConfiguredSignInManager(
            UserManager<User> userManager,
            IHttpContextAccessor contextAccessor,
            IUserClaimsPrincipalFactory<User> claimsFactory,
            IOptions<IdentityOptions> optionsAccessor,
            ILogger<SignInManager<User>> logger,
            IAuthenticationSchemeProvider schemes,
            IUserConfirmation<User> confirmation,
            bool googleEnabled = true)
            : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
        {
            _googleEnabled = googleEnabled;
        }

        public override Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemesAsync()
        {
            if (!_googleEnabled)
            {
                return Task.FromResult<IEnumerable<AuthenticationScheme>>(Array.Empty<AuthenticationScheme>());
            }

            return Task.FromResult<IEnumerable<AuthenticationScheme>>(new[]
            {
                new AuthenticationScheme("Google", "Google", typeof(IAuthenticationHandler))
            });
        }
    }

    public static async Task RunTestsAsync(IServiceProvider rootServices)
    {
        Console.WriteLine("======================================================================");
        Console.WriteLine("TASK-P1-02: Google Account Linking & Profile Security Integration Tests");
        Console.WriteLine("======================================================================");

        var passed = 0;
        var failed = 0;
        var createdUserIds = new List<string>();
        var spyTelegram = new SpyTelegramService();

        void Assert(bool condition, string name, string? detail = null)
        {
            if (condition)
            {
                passed++;
                Console.WriteLine($"[PASS] {name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"[FAIL] {name}" + (string.IsNullOrWhiteSpace(detail) ? "" : $" -- {detail}"));
            }
        }

        string? ExtractCookieValue(IHeaderDictionary headers, string cookieName)
        {
            var prefix = cookieName + "=";
            string? matched = null;
            foreach (var raw in headers.SetCookie)
            {
                if (string.IsNullOrEmpty(raw) || !raw.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (raw.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pair = raw.Split(';', 2)[0];
                var val = pair[prefix.Length..];
                if (!string.IsNullOrEmpty(val))
                {
                    matched = val;
                }
            }

            return matched;
        }

        bool HasClearedCookie(IHeaderDictionary headers, string cookieName)
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

        using var scope = rootServices.CreateScope();
        var sp = scope.ServiceProvider;
        var userManager = sp.GetRequiredService<UserManager<User>>();
        var signInManager = sp.GetRequiredService<SignInManager<User>>();
        var dataProtection = sp.GetRequiredService<IDataProtectionProvider>();
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var config = sp.GetRequiredService<IConfiguration>();
        var extCookieOptions = sp.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ExternalScheme);
        var extCookieName = extCookieOptions.Cookie.Name ?? ".AspNetCore.Identity.External";

        var testJwtConfig = new ConfigurationBuilder()
            .AddConfiguration(config)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "IntegrationTestJwtSecretKey_MustBeAtLeast32Bytes!",
                ["Jwt:Issuer"] = "AUBDigitalAcademy.Test",
                ["Jwt:Audience"] = "AUBDigitalAcademy.Test"
            })
            .Build();

        IAuthService BuildAuthService(ApplicationDbContext ctx, UserManager<User> um)
            => new AuthService(um, ctx, spyTelegram, dataProtection, testJwtConfig, NullLogger<AuthService>.Instance);

        IGoogleAuthService BuildGoogleAuthService(ApplicationDbContext ctx, UserManager<User> um, IAuthService auth)
            => new GoogleAuthService(um, ctx, spyTelegram, auth, NullLogger<GoogleAuthService>.Instance);

        SettingsController BuildSettingsController(User? principalUser, DefaultHttpContext? existingCtx = null, bool googleEnabled = true)
        {
            var reqScope = rootServices.CreateScope();
            var rsp = reqScope.ServiceProvider;
            var reqDb = rsp.GetRequiredService<ApplicationDbContext>();
            var reqUm = rsp.GetRequiredService<UserManager<User>>();
            var reqDp = rsp.GetRequiredService<IDataProtectionProvider>();
            var reqAuth = BuildAuthService(reqDb, reqUm);
            var reqGoogle = BuildGoogleAuthService(reqDb, reqUm, reqAuth);

            var httpCtx = existingCtx ?? new DefaultHttpContext();
            httpCtx.RequestServices = rsp;
            httpCtx.Request.Scheme = "https";
            httpCtx.Request.Host = new HostString("localhost");
            httpCtx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.55");
            if (rsp.GetService<IHttpContextAccessor>() is { } accessor)
            {
                accessor.HttpContext = httpCtx;
            }
            if (principalUser != null)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, principalUser.Id),
                    new(ClaimTypes.Name, principalUser.UserName ?? principalUser.Id)
                };
                httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme));
            }
            else
            {
                httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity());
            }

            var testSignInManager = new TestGoogleConfiguredSignInManager(
                reqUm,
                new HttpContextAccessor { HttpContext = httpCtx },
                rsp.GetRequiredService<IUserClaimsPrincipalFactory<User>>(),
                rsp.GetRequiredService<IOptions<IdentityOptions>>(),
                NullLogger<SignInManager<User>>.Instance,
                rsp.GetRequiredService<IAuthenticationSchemeProvider>(),
                rsp.GetRequiredService<IUserConfirmation<User>>(),
                googleEnabled);

            var ctrl = new SettingsController(reqUm, testSignInManager, reqGoogle, reqDp, NullLogger<SettingsController>.Instance);

            var actionCtx = new ActionContext(httpCtx, new RouteData(), new ControllerActionDescriptor());
            ctrl.ControllerContext = new ControllerContext(actionCtx);
            ctrl.TempData = new TempDataDictionary(httpCtx, new InMemoryTempDataProvider());
            ctrl.Url = new StubUrlHelper(actionCtx);
            return ctrl;
        }

        DefaultHttpContext CreateContextWithCookies(params (string Name, string Value)[] cookies)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("localhost");
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.55");
            if (cookies.Length > 0)
            {
                ctx.Request.Headers.Cookie = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value ?? string.Empty}"));
            }
            return ctx;
        }

        async Task<string> IssueExternalCookieAsync(string provider, string providerKey, string? email, string? name, string xsrfUserId, string? nonce, DateTimeOffset? expiresUtc = null)
        {
            using var reqScope = rootServices.CreateScope();
            var rsp = reqScope.ServiceProvider;
            var reqSm = rsp.GetRequiredService<SignInManager<User>>();
            var ctx = new DefaultHttpContext { RequestServices = rsp };
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("localhost");

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, providerKey) };
            if (!string.IsNullOrEmpty(email)) claims.Add(new(ClaimTypes.Email, email));
            if (!string.IsNullOrEmpty(name)) claims.Add(new(ClaimTypes.Name, name));

            var identity = new ClaimsIdentity(claims, provider);
            var principal = new ClaimsPrincipal(identity);
            var props = reqSm.ConfigureExternalAuthenticationProperties(provider, "/Settings/LinkGoogleCallback", xsrfUserId);
            if (nonce != null)
            {
                props.Items["AubExternalNonce"] = nonce;
            }
            if (expiresUtc.HasValue)
            {
                props.ExpiresUtc = expiresUtc;
            }

            await ctx.SignInAsync(IdentityConstants.ExternalScheme, principal, props);
            return ExtractCookieValue(ctx.Response.Headers, extCookieName)
                   ?? throw new InvalidOperationException($"Failed to issue {extCookieName} cookie.");
        }

        async Task<User?> ReloadFreshAsync(string userId)
        {
            using var s = rootServices.CreateScope();
            return await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        }

        async Task<IList<UserLoginInfo>> GetLoginsFreshAsync(string userId)
        {
            using var s = rootServices.CreateScope();
            var um = s.ServiceProvider.GetRequiredService<UserManager<User>>();
            var u = await um.FindByIdAsync(userId);
            return u == null ? Array.Empty<UserLoginInfo>() : await um.GetLoginsAsync(u);
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "GoogleLinkTest@2026!";

        async Task<User> CreateUserAsync(
            string tag,
            bool isActive = true,
            bool telegramVerified = true,
            long? chatId = null,
            bool lockedOut = false,
            bool hasPassword = true)
        {
            var user = new User
            {
                UserName = $"glink_{tag}_{suffix}@aub.test",
                Email = $"glink_{tag}_{suffix}@aub.test",
                FullName = $"Link Security {tag}",
                UniversityId = $"GL{suffix}{createdUserIds.Count + 1}",
                IsActive = isActive,
                IsTelegramVerified = telegramVerified,
                TelegramChatId = chatId,
                EmailConfirmed = true,
                LockoutEnabled = true,
                LockoutEnd = lockedOut ? DateTimeOffset.UtcNow.AddMinutes(30) : null
            };

            var res = hasPassword
                ? await userManager.CreateAsync(user, password)
                : await userManager.CreateAsync(user);
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
            var authSvc = BuildAuthService(db, userManager);
            var googleAuthSvc = BuildGoogleAuthService(db, userManager, authSvc);

            // 1. Metadata: [Authorize] on SettingsController + [ValidateAntiForgeryToken] on all POSTs + NoStore on GETs
            var ctrlType = typeof(SettingsController);
            var hasAuthorize = ctrlType.GetCustomAttribute<AuthorizeAttribute>() != null;
            var postMethods = ctrlType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
                .ToList();
            var allPostsHaveCsrf = postMethods.Count == 6 && postMethods.All(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null);
            var getProfileNoStore = ctrlType.GetMethod(nameof(SettingsController.Profile))?.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore == true;
            var getCallbackNoStore = ctrlType.GetMethod(nameof(SettingsController.LinkGoogleCallback))?.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore == true;
            var externalCallbackNoStore = typeof(AccountController).GetMethod(nameof(AccountController.ExternalLoginCallback))?.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore == true;

            Assert(hasAuthorize, "S1a. SettingsController enforces [Authorize]");
            Assert(allPostsHaveCsrf, "S2. All 6 SettingsController POST actions enforce [ValidateAntiForgeryToken]");
            Assert(getProfileNoStore && getCallbackNoStore && externalCallbackNoStore, "S1b. Sensitive Profile & OAuth callback GET actions enforce ResponseCache NoStore");

            var anonCtrl = BuildSettingsController(null);
            var anonProfileResult = await anonCtrl.Profile();
            Assert(anonProfileResult is ChallengeResult, "S1c. Unauthenticated SettingsController.Profile returns ChallengeResult");

            // Create isolated test users
            var alice = await CreateUserAsync("alice", chatId: 7100001L + createdUserIds.Count);
            var bob = await CreateUserAsync("bob", chatId: 7200001L + createdUserIds.Count);
            var charlieNoTg = await CreateUserAsync("charlie_notg", telegramVerified: false, chatId: null);
            var daveInactive = await CreateUserAsync("dave_inactive", isActive: false, chatId: 7300001L);
            var eveLocked = await CreateUserAsync("eve_locked", lockedOut: true, chatId: 7400001L);
            var frankNoPwd = await CreateUserAsync("frank_nopwd", hasPassword: false, chatId: 7500001L);

            // 6. Fail-closed TelegramService checks (real TelegramService, NOT spy)
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var emptyBotConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:BotToken"] = "", ["Telegram:ChatId"] = "999999999" })
                .Build();
            var unconfiguredTg = new TelegramService(new HttpClient(new FailingHttpMessageHandler()), emptyBotConfig, scopeFactory, NullLogger<TelegramService>.Instance);
            var unconfiguredSent = await unconfiguredTg.SendGoogleSecurityOtpAsync("U1", "Test", "123456", "Google Account Linking", "7100001");
            Assert(!unconfiguredSent, "S6a. Real TelegramService fails closed when BotToken is missing (no simulation mode / no fallback)");

            var configuredFailConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:BotToken"] = "123456:FAKE_TOKEN_FOR_TEST" })
                .Build();
            var failingHttpTg = new TelegramService(new HttpClient(new FailingHttpMessageHandler()), configuredFailConfig, scopeFactory, NullLogger<TelegramService>.Instance);
            var httpFailSent = await failingHttpTg.SendGoogleSecurityOtpAsync("U1", "Test", "123456", "Google Account Linking", "7100001");
            Assert(!httpFailSent, "S6b. Real TelegramService returns false on Telegram HTTP 500 error");

            spyTelegram.ShouldSucceed = false;
            var dispatchFail = await googleAuthSvc.RequestStepUpOtpAsync(alice.Id, GoogleSecurityAction.Link);
            var activeAfterFail = await db.TelegramVerificationRequests.AsNoTracking().CountAsync(r => r.UserId == alice.Id && !r.IsUsed);
            var aliceStillVerified = (await ReloadFreshAsync(alice.Id))?.IsTelegramVerified == true;
            Assert(dispatchFail.Status == GoogleStepUpDispatchStatus.DeliveryFailed && activeAfterFail == 0 && aliceStillVerified,
                "S6c. Failed Telegram dispatch marks OTP invalid (fail-closed) and preserves IsTelegramVerified");
            spyTelegram.ShouldSucceed = true;

            // 3 & 4. Step-up OTP security checks (start without OTP, wrong OTP, expired, reused, 5-attempt lockout, cross-action isolation, target ChatId)
            var noPendingLinkCtrl = BuildSettingsController(alice);
            var bypassLinkRes = await noPendingLinkCtrl.LinkGoogle("123456");
            Assert(bypassLinkRes is RedirectToActionResult && !db.UserLogins.Any(l => l.UserId == alice.Id),
                "S3a. LinkGoogle without active step-up pending cookie is rejected");

            var aliceReq = await googleAuthSvc.RequestStepUpOtpAsync(alice.Id, GoogleSecurityAction.Link);
            var sentCodeMsg = spyTelegram.Dispatched.LastOrDefault();
            Assert(aliceReq.Status == GoogleStepUpDispatchStatus.Sent &&
                   sentCodeMsg != null &&
                   sentCodeMsg.ChatId == alice.TelegramChatId!.Value.ToString() &&
                   sentCodeMsg.OperationLabel == "Google Account Linking",
                "S5. Google Link OTP dispatches strictly to authenticated user's personal verified TelegramChatId");

            var realLinkCode = sentCodeMsg!.Code;

            // Cross-flow isolation: GLK code cannot verify Login 2FA or Unlink!
            var login2FaWithGlk = await authSvc.VerifyWebTwoFactorAsync(alice.Id, realLinkCode);
            var unlinkWithGlk = await googleAuthSvc.VerifyStepUpOtpAsync(alice.Id, GoogleSecurityAction.Unlink, realLinkCode);
            Assert(login2FaWithGlk.Status != TwoFactorVerifyStatus.Verified &&
                   unlinkWithGlk.Status != GoogleStepUpVerifyStatus.Verified,
                "S4a. GLK Google Link OTP cannot be used for Login 2FA or Google Unlink");

            // Wrong OTP + 5-attempt lockout
            for (var i = 1; i <= 4; i++)
            {
                var wrongRes = await googleAuthSvc.VerifyStepUpOtpAsync(alice.Id, GoogleSecurityAction.Link, "000000");
                if (wrongRes.Status != GoogleStepUpVerifyStatus.InvalidCode)
                {
                    Assert(false, $"S4b. Wrong GLK OTP attempt #{i} should return InvalidCode");
                }
            }
            var lockRes = await googleAuthSvc.VerifyStepUpOtpAsync(alice.Id, GoogleSecurityAction.Link, "000000");
            var evenRightAfterLock = await googleAuthSvc.VerifyStepUpOtpAsync(alice.Id, GoogleSecurityAction.Link, realLinkCode);
            Assert(lockRes.Status == GoogleStepUpVerifyStatus.LockedTooManyAttempts && evenRightAfterLock.Status != GoogleStepUpVerifyStatus.Verified,
                "S4c. Five wrong GLK OTP attempts lock out challenge and invalidate valid code");

            // Expired GLK OTP check (bypass cooldown by inserting expired challenge directly after marking previous used)
            await db.TelegramVerificationRequests.Where(r => r.UserId == alice.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsUsed, true));
            db.TelegramVerificationRequests.Add(new TelegramVerificationRequest
            {
                UserId = alice.Id,
                UniversityId = alice.UniversityId!,
                Code = "GLK:654321",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                IsUsed = false,
                AttemptsCount = 0,
                MaxAttempts = 5
            });
            await db.SaveChangesAsync();
            var expVerify = await googleAuthSvc.VerifyStepUpOtpAsync(alice.Id, GoogleSecurityAction.Link, "654321");
            Assert(expVerify.Status == GoogleStepUpVerifyStatus.Expired, "S4d. Expired GLK OTP is rejected");

            // Expire cooldown timestamp on prior request so we can issue a fresh code cleanly
            await db.TelegramVerificationRequests.Where(r => r.UserId == alice.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, DateTime.UtcNow.AddMinutes(-5)));

            // Full controller flow: StartGoogleLink -> forge LinkGoogleCallback prematurely ("link-otp" stage only!) -> must fail
            var aliceStartCtx = CreateContextWithCookies();
            var aliceStartCtrl = BuildSettingsController(alice, aliceStartCtx);
            await aliceStartCtrl.StartGoogleLink();
            var linkOtpCookie = ExtractCookieValue(aliceStartCtx.Response.Headers, "AUB_GoogleSecurityPending");
            var otpForAliceLink = spyTelegram.Dispatched.Last().Code;
            Assert(!string.IsNullOrEmpty(linkOtpCookie), "S3b. StartGoogleLink sets DataProtection-protected pending cookie");

            var extKeyAlice = $"google-sub-alice-{suffix}";
            var prematureExtCookie = await IssueExternalCookieAsync("Google", extKeyAlice, "other-google-email@gmail.com", "Alice Google", alice.Id, $"link:{Guid.NewGuid():N}");
            var prematureCtx = CreateContextWithCookies(("AUB_GoogleSecurityPending", linkOtpCookie!), (extCookieName, prematureExtCookie));
            var prematureCtrl = BuildSettingsController(alice, prematureCtx);
            await prematureCtrl.LinkGoogleCallback();
            Assert(!(await GetLoginsFreshAsync(alice.Id)).Any(),
                "S3c. LinkGoogleCallback rejects 'link-otp' state when user has not verified OTP via POST LinkGoogle");

            // Issue a fresh StartGoogleLink pending cookie after S3c revoked the previous pending session nonce
            await db.TelegramVerificationRequests.Where(r => r.UserId == alice.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, DateTime.UtcNow.AddMinutes(-5)));
            aliceStartCtx = CreateContextWithCookies();
            await BuildSettingsController(alice, aliceStartCtx).StartGoogleLink();
            linkOtpCookie = ExtractCookieValue(aliceStartCtx.Response.Headers, "AUB_GoogleSecurityPending");
            otpForAliceLink = spyTelegram.Dispatched.Last().Code;

            // Valid step-up confirmation via POST LinkGoogle -> emits ChallengeResult + "link-oauth" pending cookie
            // Re-issue external cookie later with the EXACT nonce generated by LinkGoogle!
            var verifyOtpCtx = CreateContextWithCookies(("AUB_GoogleSecurityPending", linkOtpCookie!));
            var verifyOtpCtrl = BuildSettingsController(alice, verifyOtpCtx);
            var challengeResult = await verifyOtpCtrl.LinkGoogle(otpForAliceLink) as ChallengeResult;
            var linkOauthCookie = ExtractCookieValue(verifyOtpCtx.Response.Headers, "AUB_GoogleSecurityPending");
            var issuedLinkNonce = challengeResult?.Properties?.Items.TryGetValue("AubExternalNonce", out var ln) == true ? ln : null;
            Assert(challengeResult != null &&
                   !string.IsNullOrEmpty(linkOauthCookie) &&
                   !string.IsNullOrEmpty(issuedLinkNonce) &&
                   issuedLinkNonce.StartsWith("link:", StringComparison.Ordinal),
                "S7a. Valid Telegram OTP upgrades state to link-oauth and issues Google Challenge with link: nonce");

            // Reusing the same GLK OTP must fail
            var reuseOtpCtx = CreateContextWithCookies(("AUB_GoogleSecurityPending", linkOtpCookie!));
            var reuseOtpCtrl = BuildSettingsController(alice, reuseOtpCtx);
            var reuseRes = await reuseOtpCtrl.LinkGoogle(otpForAliceLink);
            Assert(reuseRes is RedirectToActionResult, "S4e. Consumed GLK OTP cannot be reused");

            // 8. XsrfId mismatch: external cookie issued for Bob, callback run by Alice -> REJECTED!
            var wrongXsrfExtCookie = await IssueExternalCookieAsync("Google", extKeyAlice, "alice-alt@gmail.com", "Alice", bob.Id, issuedLinkNonce);
            var wrongXsrfCtx = CreateContextWithCookies(("AUB_GoogleSecurityPending", linkOauthCookie!), (extCookieName, wrongXsrfExtCookie));
            // Wait: make sure we don't consume linkOauthCookie yet? Wait — finally block revokes linkOauthCookie! Good — test that rejection AND verify new challenge works!
            var wrongXsrfCtrl = BuildSettingsController(alice, wrongXsrfCtx);
            await wrongXsrfCtrl.LinkGoogleCallback();
            Assert(!(await GetLoginsFreshAsync(alice.Id)).Any() &&
                   HasClearedCookie(wrongXsrfCtx.Response.Headers, "AUB_GoogleSecurityPending") &&
                   HasClearedCookie(wrongXsrfCtx.Response.Headers, extCookieName),
                "S8. XsrfId mismatch rejects link and clears both pending and external cookies",
                $"logins={(await GetLoginsFreshAsync(alice.Id)).Count}, pendingCleared={HasClearedCookie(wrongXsrfCtx.Response.Headers, "AUB_GoogleSecurityPending")}, extCleared={HasClearedCookie(wrongXsrfCtx.Response.Headers, extCookieName)}, setCookies=[{string.Join(" | ", wrongXsrfCtx.Response.Headers.SetCookie.ToArray())}]");

            // Try replaying that revoked linkOauthCookie with valid XsrfId -> rejected!
            var validExtCookieRevokedReplay = await IssueExternalCookieAsync("Google", extKeyAlice, "alice-alt@gmail.com", "Alice", alice.Id, issuedLinkNonce);
            var revokedReplayCtx = CreateContextWithCookies(("AUB_GoogleSecurityPending", linkOauthCookie!), (extCookieName, validExtCookieRevokedReplay));
            await BuildSettingsController(alice, revokedReplayCtx).LinkGoogleCallback();
            Assert(!(await GetLoginsFreshAsync(alice.Id)).Any(), "S9a. Revoked pending security cookie cannot be replayed after failed callback");

            // 9 & 10. Direct nonce validation checks (missing nonce + IssuedUtc fallback check, expired, malformed, cross-purpose)
            var propsMissingNonce = new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow };
            var infoMissingLogin = new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "k1") }, "Google")), "Google", "k1", "Google")
            { AuthenticationProperties = propsMissingNonce };
            Assert(!authSvc.TryConsumeExternalLoginNonce(infoMissingLogin, "login"), "S9b. Missing AubExternalNonce rejected for login even with IssuedUtc");
            Assert(!authSvc.TryConsumeExternalLoginNonce(infoMissingLogin, "link", "link:1234567890abcdef"), "S9c. Missing AubExternalNonce rejected for link even with IssuedUtc");

            var expiredProps = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
            expiredProps.Items["AubExternalNonce"] = "link:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var infoExpired = new ExternalLoginInfo(infoMissingLogin.Principal, "Google", "k2", "Google") { AuthenticationProperties = expiredProps };
            Assert(!authSvc.TryConsumeExternalLoginNonce(infoExpired, "link", expiredProps.Items["AubExternalNonce"]), "S9d. Expired AubExternalNonce rejected");

            var loginNonceProps = new AuthenticationProperties();
            loginNonceProps.Items["AubExternalNonce"] = "login:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var infoLoginNonceForLink = new ExternalLoginInfo(infoMissingLogin.Principal, "Google", "k3", "Google") { AuthenticationProperties = loginNonceProps };
            Assert(!authSvc.TryConsumeExternalLoginNonce(infoLoginNonceForLink, "link", loginNonceProps.Items["AubExternalNonce"]), "S10a. Login nonce rejected for Google linking");

            var linkNonceProps = new AuthenticationProperties();
            linkNonceProps.Items["AubExternalNonce"] = "link:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var infoLinkNonceForLogin = new ExternalLoginInfo(infoMissingLogin.Principal, "Google", "k4", "Google") { AuthenticationProperties = linkNonceProps };
            var extLoginWithLinkNonce = await authSvc.InitiateExternalTwoFactorLoginAsync(infoLinkNonceForLogin);
            Assert(extLoginWithLinkNonce.Status == ExternalLoginStatus.Replayed, "S10b. Link nonce rejected for Google login");

            // 7 & 14 & 20. Full happy-path link for Alice (Google email differs from Alice's university email -> allowed without auto-linking!)
            await db.TelegramVerificationRequests.Where(r => r.UserId == alice.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, DateTime.UtcNow.AddMinutes(-5)));
            var aliceBeforeStamp = (await ReloadFreshAsync(alice.Id))!.SecurityStamp;
            var aliceJwtBeforeLink = authSvc.GenerateJwtToken(alice, "Student")?.Token ?? throw new InvalidOperationException("Failed to generate JWT.");

            var sCtx2 = CreateContextWithCookies();
            await BuildSettingsController(alice, sCtx2).StartGoogleLink();
            var otpCookie2 = ExtractCookieValue(sCtx2.Response.Headers, "AUB_GoogleSecurityPending")!;
            var code2 = spyTelegram.Dispatched.Last().Code;

            var vCtx2 = CreateContextWithCookies(("AUB_GoogleSecurityPending", otpCookie2));
            var ch2 = await BuildSettingsController(alice, vCtx2).LinkGoogle(code2) as ChallengeResult;
            var oauthCookie2 = ExtractCookieValue(vCtx2.Response.Headers, "AUB_GoogleSecurityPending")!;
            var linkNonce2 = ch2!.Properties!.Items["AubExternalNonce"]!;

            // Issue External cookie with a DIFFERENT email than alice.Email (bob.Email!) to verify S14 (no email mismatch block, no auto-link to Bob)
            var goodExtCookie = await IssueExternalCookieAsync("Google", extKeyAlice, bob.Email, "Alice Gmail", alice.Id, linkNonce2);
            var cbCtx = CreateContextWithCookies(("AUB_GoogleSecurityPending", oauthCookie2), (extCookieName, goodExtCookie));
            var cbCtrl = BuildSettingsController(alice, cbCtx);
            await cbCtrl.LinkGoogleCallback();

            var aliceLoginsAfter = await GetLoginsFreshAsync(alice.Id);
            var bobLoginsAfter = await GetLoginsFreshAsync(bob.Id);
            var aliceAfterLink = await ReloadFreshAsync(alice.Id);
            var oldJwtAfterLink = await authSvc.ValidateApiBearerTokenAsync(aliceJwtBeforeLink);

            Assert(aliceLoginsAfter.Count == 1 &&
                   aliceLoginsAfter[0].LoginProvider == "Google" &&
                   aliceLoginsAfter[0].ProviderKey == extKeyAlice &&
                   bobLoginsAfter.Count == 0,
                "S7b & S14. Valid step-up + callback links Google ProviderKey to Alice only (ignores email match with Bob)",
                $"aliceLogins={aliceLoginsAfter.Count}, bobLogins={bobLoginsAfter.Count}, err={cbCtrl.TempData["ErrorMessage"]}");
            Assert(aliceAfterLink != null && aliceAfterLink.SecurityStamp != aliceBeforeStamp && oldJwtAfterLink == null,
                "S20a. Linking Google rotates SecurityStamp and invalidates previously issued JWT");
            Assert(HasClearedCookie(cbCtx.Response.Headers, "AUB_GoogleSecurityPending") && HasClearedCookie(cbCtx.Response.Headers, extCookieName),
                "S7c. LinkGoogleCallback clears pending and external cookies after completion",
                $"pendingCleared={HasClearedCookie(cbCtx.Response.Headers, "AUB_GoogleSecurityPending")}, extCleared={HasClearedCookie(cbCtx.Response.Headers, extCookieName)}, setCookies=[{string.Join(" | ", cbCtx.Response.Headers.SetCookie.ToArray())}]");

            // Replay exact same linkNonce2 -> rejected!
            Assert(!authSvc.TryConsumeExternalLoginNonce(
                new ExternalLoginInfo(infoMissingLogin.Principal, "Google", extKeyAlice, "Google")
                {
                    AuthenticationProperties = new AuthenticationProperties { Items = { ["AubExternalNonce"] = linkNonce2 } }
                },
                "link",
                linkNonce2),
                "S9e. Replaying consumed link: nonce is rejected");

            // 11. Bob tries to link Alice's Google ProviderKey -> rejected without disclosure
            var freshNonceBob = "link:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var bobCollisionProps = new AuthenticationProperties();
            bobCollisionProps.Items["AubExternalNonce"] = freshNonceBob;
            var bobCollisionInfo = new ExternalLoginInfo(
                new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, extKeyAlice) }, "Google")),
                "Google",
                extKeyAlice,
                "Google")
            { AuthenticationProperties = bobCollisionProps };
            var collisionRes = await googleAuthSvc.CompleteGoogleLinkAsync(bob.Id, bobCollisionInfo, freshNonceBob);
            Assert(collisionRes.Status == GoogleLinkStatus.GoogleAlreadyLinkedToAnotherUser && (await GetLoginsFreshAsync(bob.Id)).Count == 0,
                "S11. Linking a Google ProviderKey already owned by Alice to Bob fails closed");

            // 12. Alice tries to link a second Google account -> rejected
            var aliceSecondDispatch = await googleAuthSvc.RequestStepUpOtpAsync(alice.Id, GoogleSecurityAction.Link);
            var aliceSecondNonce = "link:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var aliceSecondProps = new AuthenticationProperties();
            aliceSecondProps.Items["AubExternalNonce"] = aliceSecondNonce;
            var aliceSecondInfo = new ExternalLoginInfo(
                new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, $"second-{suffix}") }, "Google")),
                "Google",
                $"second-{suffix}",
                "Google")
            { AuthenticationProperties = aliceSecondProps };
            var aliceSecondComplete = await googleAuthSvc.CompleteGoogleLinkAsync(alice.Id, aliceSecondInfo, aliceSecondNonce);
            Assert(aliceSecondDispatch.Status == GoogleStepUpDispatchStatus.AlreadyLinked &&
                   aliceSecondComplete.Status == GoogleLinkStatus.AlreadyHasGoogle &&
                   (await GetLoginsFreshAsync(alice.Id)).Count == 1,
                "S12. User who already has a linked Google account cannot link a second Google account");

            // 13. Concurrent linking race check: User B & User Charlie? Create 2 fresh eligible users competing for one Google ProviderKey
            var racer1 = await CreateUserAsync("racer1", chatId: 7600001L);
            var racer2 = await CreateUserAsync("racer2", chatId: 7600002L);
            var raceKey = $"race-google-key-{suffix}";
            var raceNonce1 = "link:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var raceNonce2 = "link:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            Task<GoogleLinkCompleteResult> RunRaceAttemptAsync(string uid, string nonce)
            {
                return Task.Run(async () =>
                {
                    using var rs = rootServices.CreateScope();
                    var rsp = rs.ServiceProvider;
                    var rDb = rsp.GetRequiredService<ApplicationDbContext>();
                    var rUm = rsp.GetRequiredService<UserManager<User>>();
                    var rAuth = BuildAuthService(rDb, rUm);
                    var rGoogle = BuildGoogleAuthService(rDb, rUm, rAuth);
                    var props = new AuthenticationProperties();
                    props.Items["AubExternalNonce"] = nonce;
                    var info = new ExternalLoginInfo(
                        new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, raceKey) }, "Google")),
                        "Google",
                        raceKey,
                        "Google")
                    { AuthenticationProperties = props };
                    return await rGoogle.CompleteGoogleLinkAsync(uid, info, nonce);
                });
            }

            var raceResults = await Task.WhenAll(RunRaceAttemptAsync(racer1.Id, raceNonce1), RunRaceAttemptAsync(racer2.Id, raceNonce2));
            var raceWinners = raceResults.Count(r => r.Status == GoogleLinkStatus.Linked);
            var raceOwnersInDb = await db.UserLogins.AsNoTracking().CountAsync(l => l.LoginProvider == "Google" && l.ProviderKey == raceKey);
            Assert(raceWinners == 1 && raceOwnersInDb == 1,
                "S13. Concurrent linking for the same Google ProviderKey allows at most one winner");

            // 15. Inactive, locked-out, and unverified-Telegram users blocked
            var noTgLink = await googleAuthSvc.RequestStepUpOtpAsync(charlieNoTg.Id, GoogleSecurityAction.Link);
            var inactiveLink = await googleAuthSvc.RequestStepUpOtpAsync(daveInactive.Id, GoogleSecurityAction.Link);
            var lockedLink = await googleAuthSvc.RequestStepUpOtpAsync(eveLocked.Id, GoogleSecurityAction.Link);
            Assert(noTgLink.Status == GoogleStepUpDispatchStatus.TelegramNotVerified &&
                   inactiveLink.Status == GoogleStepUpDispatchStatus.UserUnavailable &&
                   lockedLink.Status == GoogleStepUpDispatchStatus.LockedOut,
                "S15. Unverified Telegram, inactive, and locked-out users cannot initiate Google linking");

            // 16, 17, 18. Unlink security checks (no OTP, wrong/cross-action OTP, no local password)
            await userManager.AddLoginAsync(frankNoPwd, new UserLoginInfo("Google", $"frank-key-{suffix}", "Google"));
            var frankNoPwdDispatch = await googleAuthSvc.RequestStepUpOtpAsync(frankNoPwd.Id, GoogleSecurityAction.Unlink);
            var frankNoPwdDirect = await googleAuthSvc.CompleteGoogleUnlinkAsync(frankNoPwd.Id, "123456");
            Assert(frankNoPwdDispatch.Status == GoogleStepUpDispatchStatus.LocalPasswordRequired &&
                   frankNoPwdDirect.Status == GoogleUnlinkStatus.LocalPasswordRequired,
                "S18. Unlink is blocked when user has no usable local password");

            var aliceUnlinkNoOtp = await BuildSettingsController(alice).UnlinkGoogle("123456");
            Assert(aliceUnlinkNoOtp is RedirectToActionResult && (await GetLoginsFreshAsync(alice.Id)).Count == 1,
                "S16. UnlinkGoogle without active StartGoogleUnlink pending state fails closed");

            // Add another non-Google provider login to Alice to prove only Google is removed!
            using (var msScope = rootServices.CreateScope())
            {
                var msUm = msScope.ServiceProvider.GetRequiredService<UserManager<User>>();
                var freshAlice = await msUm.FindByIdAsync(alice.Id);
                var addMsRes = await msUm.AddLoginAsync(freshAlice!, new UserLoginInfo("Microsoft", $"ms-{suffix}", "Microsoft"));
                if (!addMsRes.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to add Microsoft login: {string.Join("; ", addMsRes.Errors.Select(e => e.Description))}");
                }
            }

            // Also verify Login with linked Google STILL requires Telegram 2FA (S21)
            var loginCheckNonce = "login:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var loginProps = new AuthenticationProperties();
            loginProps.Items["AubExternalNonce"] = loginNonceProps.Items["AubExternalNonce"]; // Wait: use fresh loginCheckNonce!
            loginProps.Items["AubExternalNonce"] = loginCheckNonce;
            var loginInfoForAlice = new ExternalLoginInfo(
                new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, extKeyAlice) }, "Google")),
                "Google",
                extKeyAlice,
                "Google")
            { AuthenticationProperties = loginProps };
            using var loginScope = rootServices.CreateScope();
            var loginAuth = BuildAuthService(loginScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), loginScope.ServiceProvider.GetRequiredService<UserManager<User>>());
            var googleLogin2Fa = await loginAuth.InitiateExternalTwoFactorLoginAsync(loginInfoForAlice);
            Assert(googleLogin2Fa.Status == ExternalLoginStatus.TwoFactorPending && googleLogin2Fa.User?.Id == alice.Id,
                "S21. Linked Google sign-in still enforces Telegram 2FA (no direct sign-in)");

            // Start Unlink for Alice
            var unlinkStartCtx = CreateContextWithCookies();
            await BuildSettingsController(alice, unlinkStartCtx).StartGoogleUnlink();
            var unlinkPendingCookie = ExtractCookieValue(unlinkStartCtx.Response.Headers, "AUB_GoogleSecurityPending")!;
            var aliceUnlinkOtp = spyTelegram.Dispatched.Last().Code;

            // Cross-action OTP test: create active GLK code for Bob? Already verified GLK rejected by Login; also test wrong GUL OTP rejected:
            using var wrongUnlinkScope = rootServices.CreateScope();
            var wrongUnlinkGoogle = BuildGoogleAuthService(
                wrongUnlinkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                wrongUnlinkScope.ServiceProvider.GetRequiredService<UserManager<User>>(),
                BuildAuthService(wrongUnlinkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), wrongUnlinkScope.ServiceProvider.GetRequiredService<UserManager<User>>()));
            var wrongUnlinkRes = await wrongUnlinkGoogle.CompleteGoogleUnlinkAsync(alice.Id, "000000");
            Assert(wrongUnlinkRes.Status == GoogleUnlinkStatus.InvalidCode &&
                   (await GetLoginsFreshAsync(alice.Id)).Any(l => l.LoginProvider == "Google"),
                "S17. Wrong GUL OTP rejects Google unlink and preserves Google login");

            // 19 & 20b. Successful unlink via SettingsController rotates SecurityStamp, invalidates JWT, preserves Microsoft login
            aliceAfterLink = await ReloadFreshAsync(alice.Id);
            var aliceJwtBeforeUnlink = authSvc.GenerateJwtToken(aliceAfterLink!, "Student")?.Token ?? throw new InvalidOperationException("Failed to generate JWT.");
            var stampBeforeUnlink = aliceAfterLink!.SecurityStamp;

            var unlinkExecCtx = CreateContextWithCookies(("AUB_GoogleSecurityPending", unlinkPendingCookie));
            var unlinkCtrl = BuildSettingsController(aliceAfterLink, unlinkExecCtx);
            await unlinkCtrl.UnlinkGoogle(aliceUnlinkOtp);

            var aliceLoginsPostUnlink = await GetLoginsFreshAsync(alice.Id);
            var alicePostUnlink = await ReloadFreshAsync(alice.Id);
            var jwtAfterUnlink = await authSvc.ValidateApiBearerTokenAsync(aliceJwtBeforeUnlink);

            Assert(aliceLoginsPostUnlink.Count == 1 && aliceLoginsPostUnlink[0].LoginProvider == "Microsoft",
                "S19. Valid Telegram GUL OTP unlinks only Google while preserving other external providers",
                $"count={aliceLoginsPostUnlink.Count}, providers=[{string.Join(", ", aliceLoginsPostUnlink.Select(l => l.LoginProvider))}], err={unlinkCtrl.TempData["ErrorMessage"]}");
            Assert(alicePostUnlink != null && alicePostUnlink.SecurityStamp != stampBeforeUnlink && jwtAfterUnlink == null,
                "S20b. Unlinking Google rotates SecurityStamp and invalidates previously issued JWT");

            // Reuse consumed GUL OTP -> rejected
            var replayUnlinkCtx = CreateContextWithCookies(("AUB_GoogleSecurityPending", unlinkPendingCookie));
            await BuildSettingsController(alicePostUnlink, replayUnlinkCtx).UnlinkGoogle(aliceUnlinkOtp);
            using var unlinkReplayScope = rootServices.CreateScope();
            var unlinkReplayGoogleAuth = BuildGoogleAuthService(
                unlinkReplayScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                unlinkReplayScope.ServiceProvider.GetRequiredService<UserManager<User>>(),
                BuildAuthService(unlinkReplayScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), unlinkReplayScope.ServiceProvider.GetRequiredService<UserManager<User>>()));
            var directReplayUnlink = await unlinkReplayGoogleAuth.CompleteGoogleUnlinkAsync(alice.Id, aliceUnlinkOtp);
            Assert(directReplayUnlink.Status == GoogleUnlinkStatus.NotLinked || directReplayUnlink.Status == GoogleUnlinkStatus.NoActiveChallenge,
                "S17b. Consumed GUL OTP cannot be reused to unlink again",
                $"status={directReplayUnlink.Status}");

            // 22. Google consent cancellation / remoteError cleans up pending and external cookies
            await db.TelegramVerificationRequests.Where(r => r.UserId == bob.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, DateTime.UtcNow.AddMinutes(-5)));
            var bobStartCtx = CreateContextWithCookies();
            await BuildSettingsController(bob, bobStartCtx).StartGoogleLink();
            var bobOtpCookie = ExtractCookieValue(bobStartCtx.Response.Headers, "AUB_GoogleSecurityPending")!;
            var bobOtp = spyTelegram.Dispatched.Last().Code;
            var bobVerifyCtx = CreateContextWithCookies(("AUB_GoogleSecurityPending", bobOtpCookie));
            var bobCh = await BuildSettingsController(bob, bobVerifyCtx).LinkGoogle(bobOtp) as ChallengeResult;
            var bobOauthCookie = ExtractCookieValue(bobVerifyCtx.Response.Headers, "AUB_GoogleSecurityPending")!;
            var bobNonceToPass = bobCh?.Properties?.Items.TryGetValue("AubExternalNonce", out var bn) == true
                ? bn
                : "link:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var bobDummyExt = await IssueExternalCookieAsync("Google", $"cancel-{suffix}", null, null, bob.Id, bobNonceToPass);

            var cancelCbCtx = CreateContextWithCookies(("AUB_GoogleSecurityPending", bobOauthCookie), (extCookieName, bobDummyExt));
            await BuildSettingsController(bob, cancelCbCtx).LinkGoogleCallback("access_denied");
            Assert((await GetLoginsFreshAsync(bob.Id)).Count == 0 &&
                   HasClearedCookie(cancelCbCtx.Response.Headers, "AUB_GoogleSecurityPending") &&
                   HasClearedCookie(cancelCbCtx.Response.Headers, extCookieName),
                "S22. Google consent cancellation/remoteError clears cookies and does not link account");

            // 23. Profile UI model contains safe fields only (no ProviderKey, tokens, User.Id GUID, ChatId, or OTP)
            var profileCtx = CreateContextWithCookies();
            var profileRes = await BuildSettingsController(racer1, profileCtx).Profile() as ViewResult;
            var profileVm = profileRes?.Model as ProfileSecurityViewModel;
            Assert(profileVm != null &&
                   profileVm.HasLinkedGoogle &&
                   profileVm.IsTelegramVerified &&
                   !string.IsNullOrWhiteSpace(profileVm.FullName) &&
                   typeof(ProfileSecurityViewModel).GetProperty("ProviderKey") == null &&
                   typeof(ProfileSecurityViewModel).GetProperty("TelegramChatId") == null &&
                   typeof(ProfileSecurityViewModel).GetProperty("UserId") == null,
                "S23. ProfileSecurityViewModel exposes status flags only without ProviderKey, ChatId, OAuth tokens, or UserId GUID");

            // 24. Rate limit attribute metadata check on StartGoogleLink / LinkGoogle / StartGoogleUnlink / UnlinkGoogle
            var stepUpLimitedMethods = new[]
            {
                nameof(SettingsController.StartGoogleLink),
                nameof(SettingsController.ResendGoogleStepUpCode),
                nameof(SettingsController.LinkGoogle),
                nameof(SettingsController.StartGoogleUnlink),
                nameof(SettingsController.UnlinkGoogle)
            };
            var hasRateLimitAttr = stepUpLimitedMethods.All(name =>
                ctrlType.GetMethod(name)?.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName == "TelegramStepUpPolicy");
            Assert(hasRateLimitAttr, "S24a. Step-up OTP controller actions declare TelegramStepUpPolicy rate limiting attribute");

            var unconfiguredCtrl = BuildSettingsController(bob, CreateContextWithCookies(), googleEnabled: false);
            var unconfiguredProfileRes = await unconfiguredCtrl.Profile() as ViewResult;
            var unconfiguredVm = unconfiguredProfileRes?.Model as ProfileSecurityViewModel;
            await unconfiguredCtrl.StartGoogleLink();
            var unconfiguredPending = ExtractCookieValue(unconfiguredCtrl.HttpContext.Response.Headers, "AUB_GoogleSecurityPending");
            Assert(unconfiguredVm != null && !unconfiguredVm.IsGoogleConfigured && string.IsNullOrEmpty(unconfiguredPending),
                "S24b. When Google OAuth scheme is unconfigured, Profile marks Google disabled and StartGoogleLink fails closed");
        }
        finally
        {
            foreach (var uid in createdUserIds)
            {
                await db.TelegramVerificationRequests.Where(r => r.UserId == uid).ExecuteDeleteAsync();
                await db.UserLogins.Where(l => l.UserId == uid).ExecuteDeleteAsync();
                await db.UserRoles.Where(r => r.UserId == uid).ExecuteDeleteAsync();
                await db.Users.Where(u => u.Id == uid).ExecuteDeleteAsync();
            }
        }

        Console.WriteLine("----------------------------------------------------------------------");
        Console.WriteLine($"TASK-P1-02 Google Linking Security Suite Summary: Passed={passed}, Failed={failed}");
        Console.WriteLine("----------------------------------------------------------------------");
        if (failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }
}
