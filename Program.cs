using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;

LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

// Configure Data Protection to persist keys locally
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
Directory.CreateDirectory(keysFolder);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("AUBDigitalAcademy");

bool isInitTestDbOnly = args.Contains("--init-test-db");
bool isEnrollmentTestMode = isInitTestDbOnly ||
                            args.Contains("--test-enrollment-ui") ||
                            args.Contains("--test-enrollment-backend") ||
                            args.Contains("--test-student-progress") ||
                            args.Contains("--test-quiz") ||
                            args.Contains("--test-teacher-portal") ||
                            args.Contains("--test-phase4") ||
                            args.Contains("--test-navigation-phase1") ||
                            args.Contains("--test-navigation-phase2") ||
                            args.Contains("--test-academic-lookups") ||
                            args.Contains("--test-video-tracking");

// Add services to the container.
string connectionString;
if (isEnrollmentTestMode)
{
    var testConnectionString = builder.Configuration.GetConnectionString("TestConnection");
    if (string.IsNullOrWhiteSpace(testConnectionString))
    {
        testConnectionString = builder.Configuration["TEST_DB_CONNECTION"];
    }

    var guardResult = TestDatabaseGuard.EnsureSafeTestConnection(testConnectionString, builder.Environment);
    if (!guardResult.IsValid)
    {
        Console.Error.WriteLine($"[TEST GUARD BLOCKED] {guardResult.DiagnosticMessage}");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine($"[TEST GUARD] {guardResult.DiagnosticMessage}");
    connectionString = testConnectionString!;
}
else
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Configure Identity
builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    // Allow student registration without email (Student ID is primary identifier)
    options.User.RequireUniqueEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromMinutes(15);
});

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("PasswordResetPolicy", httpContext =>
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"pwd-reset:{remoteIp}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
    options.AddPolicy("TelegramStepUpPolicy", httpContext =>
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"tg-stepup:{remoteIp}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

// Configure cookie authentication
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);      // idle session window
    options.Cookie.HttpOnly = true;                       // JS cannot read the cookie
    options.Cookie.SameSite = SameSiteMode.Lax;          // CSRF protection
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTPS in prod
    // RememberMe extends to 14 days via SignInManager's isPersistent flag.
});

// Configure Centralized Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireTeacher", policy => policy.RequireRole("Teacher"));
    options.AddPolicy("RequireAssistance", policy => policy.RequireRole("Assistance"));
    options.AddPolicy("RequireStudent", policy => policy.RequireRole("Student"));
    options.AddPolicy("RequireFaculty", policy => policy.RequireRole("Teacher", "Assistance"));
    options.AddPolicy("RequireCourseStaff", policy => policy.RequireRole("Admin", "Teacher", "Assistance"));
});

// Add Google OAuth (optional - configure in appsettings)
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            options.SaveTokens = false;
            options.Events.OnRemoteFailure = context =>
            {
                var redirectUri = context.Properties?.RedirectUri;
                var isGoogleLinkFlow =
                    (!string.IsNullOrEmpty(redirectUri) &&
                     redirectUri.Contains("/Settings/LinkGoogleCallback", StringComparison.OrdinalIgnoreCase)) ||
                    (context.HttpContext.User.Identity?.IsAuthenticated == true &&
                     context.Request.Cookies.ContainsKey("AUB_GoogleSecurityPending"));

                context.Response.Redirect(isGoogleLinkFlow
                    ? "/Settings/LinkGoogleCallback?remoteError=external_auth_failed"
                    : "/Account/ExternalLoginCallback?remoteError=external_auth_failed");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
}

builder.Services.AddControllersWithViews();

// Configure CORS for frontend SPA (e.g. Live Server on port 5500)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://127.0.0.1:5500",
                "http://localhost:5500",
                "http://127.0.0.1:5000",
                "http://localhost:5000",
                "http://127.0.0.1:5236",
                "http://localhost:5236")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Application Services
builder.Services.AddTransient<AUBDigitalAcademy.Services.Implementations.TelegramHttpClientLogger>();
builder.Services.AddHttpClient<AUBDigitalAcademy.Services.Interfaces.ITelegramService, AUBDigitalAcademy.Services.Implementations.TelegramService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
})
.RemoveAllLoggers()
.AddLogger<AUBDigitalAcademy.Services.Implementations.TelegramHttpClientLogger>(wrapHandlersPipeline: true);
builder.Services.AddSingleton<AUBDigitalAcademy.Services.Interfaces.IHtmlSanitizationService, AUBDigitalAcademy.Services.Implementations.HtmlSanitizationService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.ICourseService, AUBDigitalAcademy.Services.Implementations.CourseService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.ICurriculumService, AUBDigitalAcademy.Services.Implementations.CurriculumService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.IEnrollmentService, AUBDigitalAcademy.Services.Implementations.EnrollmentService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.IUserService, AUBDigitalAcademy.Services.Implementations.UserService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.ITeacherService, AUBDigitalAcademy.Services.Implementations.TeacherService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.IAssignmentStorageService, AUBDigitalAcademy.Services.Implementations.AssignmentStorageService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.IVideoStorageService, AUBDigitalAcademy.Services.Implementations.VideoStorageService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.IAuthService, AUBDigitalAcademy.Services.Implementations.AuthService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.IGoogleAuthService, AUBDigitalAcademy.Services.Implementations.GoogleAuthService>();
if (!isEnrollmentTestMode)
{
    builder.Services.AddHostedService<AUBDigitalAcademy.Services.Implementations.TelegramPollingService>();
}

await using var app = builder.Build();

if (isEnrollmentTestMode)
{
    var initResult = await TestDatabaseInitializer.InitializeAsync(connectionString, app.Services);
    if (!initResult.IsSuccess)
    {
        Console.Error.WriteLine($"[TEST INIT FAILED] {initResult.DiagnosticMessage}");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine($"[TEST INIT] {initResult.DiagnosticMessage}");
    if (isInitTestDbOnly)
    {
        Environment.ExitCode = 0;
        return;
    }
}
else
{
    // Seed roles and default users
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            bool isDevOrTest = app.Environment.IsDevelopment() ||
                               args.Any(a => a.StartsWith("--test-", StringComparison.OrdinalIgnoreCase));
            await SeedData.InitializeAsync(services, allowDevTaGrading: isDevOrTest);
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while seeding the database.");
        }
    }
}

// Integration Test CLI Hooks
if (args.Contains("--test-course-integration"))
{
    await CourseIntegrationTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-enrollment-ui"))
{
    Environment.ExitCode = await EnrollmentUiIntegrationTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-enrollment-backend"))
{
    Environment.ExitCode = await EnrollmentBackendTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-academic-lookups"))
{
    await AcademicLookupTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-video-tracking"))
{
    await VideoWatchTrackingTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-student-progress"))
{
    await StudentProgressIntegrationTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-quiz"))
{
    await QuizIntegrationTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-user-management"))
{
    await UserManagementTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-teacher-portal"))
{
    await TeacherPortalTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-navigation-phase1"))
{
    await NavigationPhase1TestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-navigation-phase2"))
{
    await NavigationPhase2TestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-auth"))
{
    await AuthIntegrationTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-api-auth"))
{
    await ApiAuth2FaIntegrationTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-assistance-role"))
{
    await AssistanceIntegrationTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-phase4"))
{
    await StudentLearningAssessmentTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-registration-telegram"))
{
    await RegistrationTelegramIntegrationTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-registration-telegram-security"))
{
    await RegistrationTelegramSecurityTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-password-reset-security"))
{
    await PasswordResetSecurityTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-telegram-log-redaction"))
{
    await TelegramLogRedactionTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-stored-xss"))
{
    await StoredXssIntegrationTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-google-oauth-2fa"))
{
    await GoogleOAuth2FaIntegrationTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-google-linking-security"))
{
    await GoogleAccountLinkingSecurityTestRunner.RunTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-telegram-bot"))
{
    using var scope = app.Services.CreateScope();
    var telegram = scope.ServiceProvider.GetRequiredService<AUBDigitalAcademy.Services.Interfaces.ITelegramService>();
    var (success, botUsername, error) = await telegram.TestBotConnectivityAsync();
    if (success)
    {
        Console.WriteLine($"[PASS] Telegram Bot connected successfully. Bot username: @{botUsername}");
    }
    else
    {
        var safeError = AUBDigitalAcademy.Services.Implementations.TelegramLogSanitizer.SanitizeMessage(error);
        Console.WriteLine($"[FAIL] Telegram Bot connection test failed: {safeError}");
    }
    return;
}

if (args.Contains("--test-telegram-polling"))
{
    using var scope = app.Services.CreateScope();
    var telegram = scope.ServiceProvider.GetRequiredService<AUBDigitalAcademy.Services.Interfaces.ITelegramService>();
    Console.WriteLine("[INFO] Testing Telegram updates retrieval (timeout: 5s)...");
    var updates = await telegram.GetUpdatesAsync(null, timeoutSeconds: 5);
    Console.WriteLine($"[PASS] GetUpdatesAsync executed successfully. Retrieved {updates.Count} pending update(s).");
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// CORS must be between UseRouting and UseAuthentication/UseAuthorization
app.UseCors("AllowFrontend");
app.UseRateLimiter();

// Enforce server-side SecurityStamp validation for Bearer JWTs across all protected /api/* routes
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isAnonymousAuthEndpoint =
            path.Equals("/api/Auth/login", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/Auth/verify-2fa", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/Auth/forgot-password", StringComparison.OrdinalIgnoreCase);

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!isAnonymousAuthEndpoint &&
            !string.IsNullOrEmpty(authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var bearerToken = authHeader["Bearer ".Length..].Trim();
            var authService = context.RequestServices.GetRequiredService<AUBDigitalAcademy.Services.Interfaces.IAuthService>();
            var validated = await authService.ValidateApiBearerTokenAsync(bearerToken);
            if (validated == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { success = false, message = "Invalid or expired token." });
                return;
            }

            var claims = new List<System.Security.Claims.Claim>
            {
                new(System.Security.Claims.ClaimTypes.NameIdentifier, validated.Value.User.Id),
                new(System.Security.Claims.ClaimTypes.Name, validated.Value.User.UserName ?? validated.Value.User.Id)
            };
            foreach (var role in validated.Value.Roles)
            {
                claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role));
            }

            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(claims, "Bearer"));
        }
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();

// Auth compatibility route alias (maps /Auth/* requests to AccountController)
app.MapControllerRoute(
    name: "auth_alias",
    pattern: "Auth/{action=Login}/{id?}",
    defaults: new { controller = "Account" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

static void LoadDotEnv()
{
    var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
    if (!File.Exists(envPath))
    {
        return;
    }

    foreach (var rawLine in File.ReadAllLines(envPath))
    {
        var line = rawLine.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
        {
            continue;
        }

        if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
        {
            line = line[7..].TrimStart();
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();

        // Strip matching surrounding quotes if present
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            value = value[1..^1];
        }

        // Preserve values already provided by Docker or the OS shell
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
