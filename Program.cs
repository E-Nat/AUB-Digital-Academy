using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Configure Identity
builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

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
        });
}

builder.Services.AddControllersWithViews();

// Application Services
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.ICourseService, AUBDigitalAcademy.Services.Implementations.CourseService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.ICurriculumService, AUBDigitalAcademy.Services.Implementations.CurriculumService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.IEnrollmentService, AUBDigitalAcademy.Services.Implementations.EnrollmentService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.IUserService, AUBDigitalAcademy.Services.Implementations.UserService>();
builder.Services.AddScoped<AUBDigitalAcademy.Services.Interfaces.ITeacherService, AUBDigitalAcademy.Services.Implementations.TeacherService>();

var app = builder.Build();

// Seed roles and default users
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await SeedData.InitializeAsync(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
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
    await EnrollmentUiIntegrationTestRunner.RunTestsAsync(app.Services);
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

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// Auth compatibility route alias (maps /Auth/* requests to AccountController)
app.MapControllerRoute(
    name: "auth_alias",
    pattern: "Auth/{action=Login}/{id?}",
    defaults: new { controller = "Account" });

// Area routing (must come before default route)
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
