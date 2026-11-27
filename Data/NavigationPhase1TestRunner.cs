using System.Reflection;
using AUBDigitalAcademy.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AUBDigitalAcademy.Data;

public static class NavigationPhase1TestRunner
{
    public static Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var actionProvider = scope.ServiceProvider.GetRequiredService<IActionDescriptorCollectionProvider>();

        Console.WriteLine("\n=================================================");
        Console.WriteLine("  TASK-P1-01 PHASE 1: NAVIGATION & ROUTE TESTS   ");
        Console.WriteLine("=================================================\n");

        int passed = 0;
        int failed = 0;

        void AssertTrue(bool condition, string name, string detail = "")
        {
            if (condition)
            {
                Console.WriteLine($"[PASS] {name}");
                passed++;
            }
            else
            {
                Console.WriteLine($"[FAIL] {name}{(string.IsNullOrEmpty(detail) ? "" : $" — {detail}")}");
                failed++;
            }
        }

        // 1. B-01: TeacherController.CourseDetails exposes /Teacher/Courses/Details/{id} and /Teacher/CourseDetails/{id?}
        var courseDetailsMethod = typeof(TeacherController).GetMethod(nameof(TeacherController.CourseDetails));
        var routeTemplates = courseDetailsMethod?
            .GetCustomAttributes<RouteAttribute>()
            .Select(r => r.Template)
            .ToList() ?? new List<string>();

        var registeredCourseDetailsRoutes = actionProvider.ActionDescriptors.Items
            .Where(a => a.RouteValues.TryGetValue("controller", out var ctrl)
                     && string.Equals(ctrl, "Teacher", StringComparison.OrdinalIgnoreCase)
                     && a.RouteValues.TryGetValue("action", out var act)
                     && string.Equals(act, "CourseDetails", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.AttributeRouteInfo?.Template ?? string.Empty)
            .ToList();

        AssertTrue(
            routeTemplates.Contains("Teacher/Courses/Details/{id:int}") &&
            registeredCourseDetailsRoutes.Contains("Teacher/Courses/Details/{id:int}"),
            "B-01: TeacherController.CourseDetails exposes Teacher/Courses/Details/{id:int}");

        AssertTrue(
            routeTemplates.Contains("Teacher/CourseDetails/{id:int?}") &&
            registeredCourseDetailsRoutes.Contains("Teacher/CourseDetails/{id:int?}"),
            "B-01: TeacherController.CourseDetails preserves Teacher/CourseDetails/{id:int?}");

        var teacherAuthAttr = typeof(TeacherController).GetCustomAttribute<AuthorizeAttribute>();
        AssertTrue(
            teacherAuthAttr != null && teacherAuthAttr.Roles == "Teacher,Assistance,Admin",
            "B-01: TeacherController preserves [Authorize(Roles = \"Teacher,Assistance,Admin\")]");

        // 2. B-02: Teacher/Assistance Sidebar Submissions link targets /Teacher/Assignments
        var layoutPath = Path.Combine(env.ContentRootPath, "Views", "Shared", "_Layout.cshtml");
        var layoutContent = File.ReadAllText(layoutPath);

        AssertTrue(
            !layoutContent.Contains("href=\"/Teacher/Submissions\"", StringComparison.OrdinalIgnoreCase) &&
            layoutContent.Contains("href=\"/Teacher/Assignments\"", StringComparison.OrdinalIgnoreCase),
            "B-02: _Layout.cshtml Teacher sidebar Submissions link points to /Teacher/Assignments");

        // 3. B-03: Assistance topbar breadcrumb uses @dashboardArea/Dashboard
        AssertTrue(
            layoutContent.Contains("var dashboardArea = (userRole == \"Teacher\" || userRole == \"Assistance\") ? \"Teacher\" : userRole;") &&
            layoutContent.Contains("href=\"/@dashboardArea/Dashboard\"") &&
            !layoutContent.Contains("href=\"/@userRole/Dashboard\""),
            "B-03: _Layout.cshtml topbar breadcrumb uses /@dashboardArea/Dashboard (Assistance -> /Teacher/Dashboard)");

        // 4. B-04: Login Activate Your Account link uses AccountController.Register tag helpers
        var loginPath = Path.Combine(env.ContentRootPath, "Views", "Account", "Login.cshtml");
        var loginContent = File.ReadAllText(loginPath);

        AssertTrue(
            !loginContent.Contains("activate-account.html", StringComparison.OrdinalIgnoreCase) &&
            loginContent.Contains("asp-controller=\"Account\"") &&
            loginContent.Contains("asp-action=\"Register\""),
            "B-04: Login.cshtml Activate Your Account uses asp-controller=\"Account\" asp-action=\"Register\"");

        // 5. A-01: Global search rendered only for Admin and safely guarded in site.js
        var searchInputIndex = layoutContent.IndexOf("id=\"globalSearchInput\"", StringComparison.Ordinal);
        var adminGuardIndex = layoutContent.IndexOf("@if (User.IsInRole(\"Admin\"))", StringComparison.Ordinal);
        AssertTrue(
            adminGuardIndex >= 0 && searchInputIndex > adminGuardIndex,
            "A-01: _Layout.cshtml renders #globalSearchInput only inside @if (User.IsInRole(\"Admin\"))");

        var siteJsPath = Path.Combine(env.ContentRootPath, "wwwroot", "js", "site.js");
        var siteJsContent = File.ReadAllText(siteJsPath);
        AssertTrue(
            siteJsContent.Contains("const searchInput = document.getElementById('globalSearchInput');") &&
            siteJsContent.Contains("if (searchInput)"),
            "A-01: wwwroot/js/site.js guards #globalSearchInput lookup before attaching /Admin/Users redirect");

        var adminAuthAttr = typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>();
        var userAuthAttr = typeof(UserController).GetCustomAttribute<AuthorizeAttribute>();
        AssertTrue(
            adminAuthAttr?.Roles == "Admin" && userAuthAttr?.Roles == "Admin",
            "A-01: AdminController and UserController preserve [Authorize(Roles = \"Admin\")]");

        Console.WriteLine($"\nNavigation Phase 1 Summary: {passed} Passed, {failed} Failed\n");
        if (failed > 0)
        {
            Environment.ExitCode = 1;
        }

        return Task.CompletedTask;
    }
}
