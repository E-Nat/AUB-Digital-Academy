using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AUBDigitalAcademy.Controllers;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class AcademicLookupTestRunner
{
    private sealed class DummyTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object> _data = new();
        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _data.Clear();
            foreach (var kv in values) _data[kv.Key] = kv.Value;
        }
    }

    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("🏛️  AUB DIGITAL ACADEMY - TASK-P2-01 ACADEMIC LOOKUP CRUD AUDIT");
        Console.WriteLine("=================================================================\n");

        int passed = 0;
        int failed = 0;

        void Pass(string msg)
        {
            passed++;
            Console.WriteLine($"  [PASS] {msg}");
        }

        void Fail(string msg)
        {
            failed++;
            Console.WriteLine($"  [FAIL] {msg}");
        }

        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var courseService = sp.GetRequiredService<ICourseService>();
        var userManager = sp.GetRequiredService<UserManager<User>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<AcademicController>();

        var admin = await userManager.FindByEmailAsync("admin@aub.com")
            ?? throw new InvalidOperationException("Seeded admin@aub.com not found.");
        var teacher = await userManager.FindByEmailAsync("teacher@aub.com")
            ?? throw new InvalidOperationException("Seeded teacher@aub.com not found.");

        var ctrl = new AcademicController(courseService, db, userManager, logger);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, admin.Id),
                    new Claim(ClaimTypes.Name, admin.UserName!),
                    new Claim(ClaimTypes.Role, "Admin")
                }, "TestAuth"))
            }
        };
        ctrl.TempData = new TempDataDictionary(ctrl.ControllerContext.HttpContext, new DummyTempDataProvider());

        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        Category? cat = null;
        Major? major = null;
        Semester? sem = null;
        Course? course = null;

        try
        {
            // 1. Authorization check: all lookup actions require Admin-only
            var lookupActions = new[]
            {
                nameof(AcademicController.Category), nameof(AcademicController.CreateCategory),
                nameof(AcademicController.EditCategory), nameof(AcademicController.DeleteCategory),
                nameof(AcademicController.Major), nameof(AcademicController.CreateMajor),
                nameof(AcademicController.EditMajor), nameof(AcademicController.DeleteMajor),
                nameof(AcademicController.Semester), nameof(AcademicController.CreateSemester),
                nameof(AcademicController.EditSemester), nameof(AcademicController.SetCurrentSemester),
                nameof(AcademicController.DeleteSemester)
            };
            bool allAdminOnly = lookupActions.All(name =>
            {
                var method = typeof(AcademicController).GetMethod(name);
                var auth = method?.GetCustomAttributes(typeof(AuthorizeAttribute), false)
                    .OfType<AuthorizeAttribute>()
                    .FirstOrDefault();
                return auth != null && auth.Roles == "Admin";
            });
            if (allAdminOnly) Pass("Test 01: All 13 lookup actions enforce [Authorize(Roles = \"Admin\")]");
            else Fail("Test 01: Missing Admin-only [Authorize] on one or more lookup actions");

            // 2. Category Create, Duplicate Check, Edit
            var catName = $"TestCat_{suffix}";
            await ctrl.CreateCategory(catName);
            cat = await db.Categories.FirstOrDefaultAsync(c => c.Name == catName);
            await ctrl.CreateCategory(catName.ToLowerInvariant());
            int catCount = await db.Categories.CountAsync(c => c.Name.ToUpper() == catName.ToUpper());
            if (cat != null && catCount == 1) Pass("Test 02: Category create + duplicate name rejection verified");
            else Fail("Test 02: Category create or duplicate rejection failed");

            var updatedCatName = $"TestCatUpd_{suffix}";
            await ctrl.EditCategory(cat!.Id, updatedCatName);
            await db.Entry(cat).ReloadAsync();
            if (cat.Name == updatedCatName) Pass("Test 03: Category edit verified");
            else Fail("Test 03: Category edit failed");

            // 3. Major Create, Unique Code Check, Edit, Safe Delete (blocked when Category has Major)
            var majorCode = $"MJ{suffix}";
            await ctrl.CreateMajor($"Major {suffix}", majorCode, cat.Id);
            major = await db.Majors.FirstOrDefaultAsync(m => m.Code == majorCode);
            await ctrl.CreateMajor("Dup Major", majorCode.ToLowerInvariant(), cat.Id);
            int majorCount = await db.Majors.CountAsync(m => m.Code == majorCode);
            if (major != null && majorCount == 1) Pass("Test 04: Major create + unique code enforcement verified");
            else Fail("Test 04: Major create or unique code enforcement failed");

            await ctrl.DeleteCategory(cat.Id);
            bool catStillExists = await db.Categories.AnyAsync(c => c.Id == cat.Id);
            if (catStillExists) Pass("Test 05: DeleteCategory blocked when Majors reference it");
            else Fail("Test 05: DeleteCategory deleted a referenced category");

            // 4. Semester Create, EndDate > StartDate Check, Unique Code, SetCurrentSemester
            var semCode = $"SM{suffix}";
            await ctrl.CreateSemester("Invalid Sem", $"BAD{suffix}", DateTime.UtcNow, DateTime.UtcNow.AddDays(-1), false);
            bool invalidSemCreated = await db.Semesters.AnyAsync(s => s.Code == $"BAD{suffix}");

            var prevCurrent = await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.IsCurrent);
            await ctrl.CreateSemester($"Sem {suffix}", semCode, DateTime.UtcNow, DateTime.UtcNow.AddMonths(4), true);
            sem = await db.Semesters.FirstOrDefaultAsync(s => s.Code == semCode);
            int currentCount = await db.Semesters.CountAsync(s => s.IsCurrent);

            if (!invalidSemCreated && sem != null && sem.IsCurrent && currentCount == 1)
                Pass("Test 06: Semester create + date validation + single active IsCurrent verified");
            else
                Fail("Test 06: Semester create/validation/IsCurrent failed");

            if (prevCurrent != null && sem != null)
            {
                await ctrl.SetCurrentSemester(prevCurrent.Id);
                await db.Entry(sem).ReloadAsync();
                if (!sem.IsCurrent) Pass("Test 07: SetCurrentSemester atomically switched active semester");
                else Fail("Test 07: SetCurrentSemester failed to clear previous active semester");
            }

            // 5. Safe Delete Major & Semester when Course references them, then delete after Course removed
            course = new Course
            {
                Code = $"CSL{suffix}",
                Title = "Lookup Dependency Course",
                MajorId = major!.Id,
                SemesterId = sem!.Id,
                TeacherId = teacher.Id,
                Status = Models.Enums.CourseStatus.Draft
            };
            db.Courses.Add(course);
            await db.SaveChangesAsync();

            await ctrl.DeleteMajor(major.Id);
            await ctrl.DeleteSemester(sem.Id);
            if (await db.Majors.AnyAsync(m => m.Id == major.Id) && await db.Semesters.AnyAsync(s => s.Id == sem.Id))
                Pass("Test 08: DeleteMajor and DeleteSemester blocked when Courses reference them");
            else
                Fail("Test 08: Referenced Major or Semester was unexpectedly deleted");

            db.Courses.Remove(course);
            await db.SaveChangesAsync();
            course = null;

            await ctrl.DeleteSemester(sem.Id);
            await ctrl.DeleteMajor(major.Id);
            await ctrl.DeleteCategory(cat.Id);
            if (!await db.Semesters.AnyAsync(s => s.Id == sem.Id) &&
                !await db.Majors.AnyAsync(m => m.Id == major.Id) &&
                !await db.Categories.AnyAsync(c => c.Id == cat.Id))
            {
                Pass("Test 09: Unused Semester, Major, and Category deleted cleanly");
                sem = null;
                major = null;
                cat = null;
            }
            else
            {
                Fail("Test 09: Failed to delete unused Semester/Major/Category");
            }
        }
        finally
        {
            if (course != null) { db.Courses.Remove(course); await db.SaveChangesAsync(); }
            if (sem != null) { db.Semesters.Remove(sem); await db.SaveChangesAsync(); }
            if (major != null) { db.Majors.Remove(major); await db.SaveChangesAsync(); }
            if (cat != null) { db.Categories.Remove(cat); await db.SaveChangesAsync(); }
        }

        Console.WriteLine($"\nAcademic Lookup Summary: {passed} Passed, {failed} Failed\n");
        if (failed > 0) Environment.Exit(1);
    }
}
