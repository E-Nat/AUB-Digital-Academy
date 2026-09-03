using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AUBDigitalAcademy.Controllers;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class ModuleUiIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("🖥️  AUB DIGITAL ACADEMY - CURRICULUM UI TO BACKEND INTEGRATION AUDIT");
        Console.WriteLine("=================================================================\n");

        int testsPassed = 0;
        int totalTests = 0;

        void Pass(string name)
        {
            testsPassed++;
            totalTests++;
            Console.WriteLine($"  [PASS] Test {totalTests}: {name}");
        }

        void Fail(string name, string error)
        {
            totalTests++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] Test {totalTests}: {name} - {error}");
            Console.ResetColor();
            Environment.Exit(1);
        }

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var courseService = scope.ServiceProvider.GetRequiredService<ICourseService>();
        var curriculumService = scope.ServiceProvider.GetRequiredService<ICurriculumService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var controllerLogger = loggerFactory.CreateLogger<AcademicController>();

        var teacher = (await userManager.GetUsersInRoleAsync("Teacher")).FirstOrDefault(u => u.IsActive)
                      ?? await context.Users.FirstOrDefaultAsync(u => u.IsActive);
        var major = await context.Majors.FirstOrDefaultAsync();
        var semester = await context.Semesters.FirstOrDefaultAsync();
        var student = await context.Users.FirstOrDefaultAsync(u => u.Email == "student@aub.edu.kh")
                      ?? (await userManager.GetUsersInRoleAsync("Student")).FirstOrDefault();

        if (teacher == null || major == null || semester == null || student == null)
        {
            Fail("Seed Dependencies", "Missing active teacher, major, semester, or student.");
            return;
        }

        // Clean up previous test courses
        var prevCourses = await context.Courses.Where(c => c.Code.StartsWith("TEST-UI-")).ToListAsync();
        if (prevCourses.Any())
        {
            context.Courses.RemoveRange(prevCourses);
            await context.SaveChangesAsync();
        }

        // Helper to instantiate AcademicController with simulated context
        AcademicController CreateController(bool isAjax = false)
        {
            var httpContext = new DefaultHttpContext();
            if (isAjax)
            {
                httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
            }
            var tempDataProvider = new TestTempDataProvider();
            var tempData = new TempDataDictionary(httpContext, tempDataProvider);
            var actionContext = new ActionContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
                ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
            };

            return new AcademicController(courseService, context, userManager, controllerLogger, curriculumService)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = tempData,
                Url = new DummyUrlHelper(actionContext)
            };
        }

        // Create Course 1 (will have modules) and Course 2 (empty state)
        var testCourse = new Course
        {
            Code = "TEST-UI-CURR",
            Title = "Full-Stack Web Development",
            Description = "Comprehensive full stack bootcamp",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Published,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var emptyCourse = new Course
        {
            Code = "TEST-UI-EMPTY",
            Title = "Brand New Course Without Modules",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.AddRange(testCourse, emptyCourse);
        await context.SaveChangesAsync();
        int courseId = testCourse.Id;
        int emptyCourseId = emptyCourse.Id;

        Pass($"Created test courses in SQL Server: ID {courseId} & ID {emptyCourseId}");

        // =========================================================================
        // TEST E — EMPTY STATE (Course with 0 modules)
        // =========================================================================
        var emptyCtrl = CreateController(isAjax: false);
        var emptyResult = await emptyCtrl.Curriculum(emptyCourseId);
        if (emptyResult is not ViewResult emptyView || emptyView.Model is not CourseCurriculumViewModel emptyVm)
        {
            Fail("TEST E (Empty State)", "Curriculum action did not return ViewResult with CourseCurriculumViewModel.");
            return;
        }
        if (emptyVm.TotalModules != 0 || emptyVm.Modules.Count != 0)
        {
            Fail("TEST E (Empty Count)", $"Expected 0 modules in empty course, got {emptyVm.TotalModules}");
            return;
        }
        if (emptyVm.CourseTitle != "Brand New Course Without Modules" || emptyVm.CourseId != emptyCourseId)
        {
            Fail("TEST E (Course Info)", "Course context properties mismatched on empty curriculum view model.");
            return;
        }
        Pass("TEST E (Empty State): Curriculum page returns valid ViewModel with 0 modules and correct Course details for empty state rendering");

        // =========================================================================
        // TEST B — ADD MODULE (Via AJAX & Form POST)
        // =========================================================================
        // 1. AJAX Add Module
        var ajaxCtrl = CreateController(isAjax: true);
        var createModel = new ModuleCreateViewModel
        {
            CourseId = courseId,
            Title = "Module 1: HTML & Web Structure",
            Position = 1
        };
        var createAjaxResult = await ajaxCtrl.CreateModule(createModel);
        if (createAjaxResult is not JsonResult createJson || createJson.Value == null)
        {
            Fail("TEST B (AJAX Create Module)", "CreateModule action did not return JsonResult for AJAX request.");
            return;
        }
        var cData = createJson.Value;
        var cSuccess = (bool?)cData.GetType().GetProperty("success")?.GetValue(cData);
        var cModId = (int?)cData.GetType().GetProperty("moduleId")?.GetValue(cData);
        if (cSuccess != true || cModId == null || cModId <= 0)
        {
            Fail("TEST B (AJAX Payload)", "CreateModule JSON response indicates failure.");
            return;
        }
        int module1Id = cModId.Value;

        // Verify in SQL Server
        var m1Db = await context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == module1Id);
        if (m1Db == null || m1Db.CourseId != courseId || m1Db.Title != "Module 1: HTML & Web Structure")
        {
            Fail("TEST B (SQL Server Verification)", "Module 1 not persisted correctly in SQL Server.");
            return;
        }
        Pass("TEST B1 (AJAX Add Module): Module created via AJAX endpoint, returned JSON success, and persisted to SQL Server");

        // 2. Form POST Add Module (with auto-position)
        var formCtrl = CreateController(isAjax: false);
        var createModel2 = new ModuleCreateViewModel
        {
            CourseId = courseId,
            Title = "Module 2: CSS & Responsive Layouts",
            Position = null
        };
        var createFormResult = await formCtrl.CreateModule(createModel2);
        if (createFormResult is not RedirectToActionResult redirCreate || redirCreate.ActionName != "Curriculum")
        {
            Fail("TEST B2 (Form POST Create Module)", "Expected RedirectToAction to Curriculum.");
            return;
        }
        if (formCtrl.TempData["SuccessMessage"]?.ToString() != "Module created successfully.")
        {
            Fail("TEST B2 (TempData)", "Expected 'Module created successfully.' in TempData.");
            return;
        }

        var m2Db = await context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.CourseId == courseId && ch.Title == "Module 2: CSS & Responsive Layouts");
        if (m2Db == null || m2Db.Position != 2)
        {
            Fail("TEST B2 (SQL Server Check)", $"Module 2 missing or position incorrect in SQL Server: {m2Db?.Position}");
            return;
        }
        int module2Id = m2Db.Id;
        Pass("TEST B2 (Form POST Add Module): Auto-positioned Module 2 saved to SQL Server and redirected back to Curriculum with TempData");

        // =========================================================================
        // TEST A — OPEN CURRICULUM (With real modules loaded)
        // =========================================================================
        var currCtrl = CreateController(isAjax: false);
        var currResult = await currCtrl.Curriculum(courseId);
        if (currResult is not ViewResult currView || currView.Model is not CourseCurriculumViewModel currVm)
        {
            Fail("TEST A (Open Curriculum)", "Curriculum action did not return ViewResult with CourseCurriculumViewModel.");
            return;
        }
        if (currVm.TotalModules != 2 || currVm.Modules.Count != 2)
        {
            Fail("TEST A (Modules Count)", $"Expected 2 modules loaded, got {currVm.TotalModules}");
            return;
        }
        if (currVm.Modules[0].Title != "Module 1: HTML & Web Structure" || currVm.Modules[1].Title != "Module 2: CSS & Responsive Layouts")
        {
            Fail("TEST A (Module Order/Titles)", "Modules titles or ordering incorrect on loaded Curriculum view model.");
            return;
        }
        if (currVm.TeacherName != teacher.FullName || currVm.CourseCode != "TEST-UI-CURR")
        {
            Fail("TEST A (Course Metadata)", "Course header metadata missing or incorrect on ViewModel.");
            return;
        }
        Pass("TEST A (Open Curriculum): Real Course metadata and all its real Modules loaded accurately into ViewModel from SQL Server");

        // =========================================================================
        // TEST C — EDIT MODULE (Get JSON + Save Update)
        // =========================================================================
        // 1. GetModuleJson pre-populates edit modal
        var getModCtrl = CreateController(isAjax: true);
        var getModResult = await getModCtrl.GetModuleJson(module1Id);
        if (getModResult is not JsonResult getModJson || getModJson.Value == null)
        {
            Fail("TEST C1 (GetModuleJson)", "GetModuleJson did not return JsonResult.");
            return;
        }
        var gmData = getModJson.Value;
        var gmMod = gmData.GetType().GetProperty("module")?.GetValue(gmData);
        var gmTitle = gmMod?.GetType().GetProperty("title")?.GetValue(gmMod)?.ToString();
        if (gmTitle != "Module 1: HTML & Web Structure")
        {
            Fail("TEST C1 (Pre-population)", $"Expected 'Module 1: HTML & Web Structure', got '{gmTitle}'");
            return;
        }
        Pass("TEST C1 (Pre-populate Edit Modal): GetModuleJson returned accurate module data for client-side modal population");

        // 2. Submit Edit Module via AJAX
        var editCtrl = CreateController(isAjax: true);
        var editModel = new ModuleEditViewModel
        {
            Id = module1Id,
            CourseId = courseId,
            Title = "Module 1: Modern HTML5 & Semantic Web",
            Position = 1
        };
        var editAjaxResult = await editCtrl.EditModule(editModel);
        if (editAjaxResult is not JsonResult editJson || editJson.Value == null)
        {
            Fail("TEST C2 (EditModule AJAX)", "EditModule did not return JsonResult.");
            return;
        }

        // Verify update persisted in SQL Server
        var m1UpdatedDb = await context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == module1Id);
        if (m1UpdatedDb == null || m1UpdatedDb.Title != "Module 1: Modern HTML5 & Semantic Web")
        {
            Fail("TEST C2 (SQL Server Update)", $"Title not updated in SQL Server: {m1UpdatedDb?.Title}");
            return;
        }
        Pass("TEST C2 (Edit Module): Module updated in SQL Server and confirmed via JSON response");

        // =========================================================================
        // TEST D — DELETE MODULE (Safe Deletion)
        // =========================================================================
        var delCtrl = CreateController(isAjax: true);
        var delResult = await delCtrl.DeleteModule(module2Id, courseId);
        if (delResult is not JsonResult delJson || delJson.Value == null)
        {
            Fail("TEST D (DeleteModule)", "DeleteModule did not return JsonResult.");
            return;
        }
        var delData = delJson.Value;
        var delSuccess = (bool?)delData.GetType().GetProperty("success")?.GetValue(delData);
        if (delSuccess != true)
        {
            Fail("TEST D (Delete Success)", "DeleteModule returned false for success.");
            return;
        }

        // Verify deleted from SQL Server
        var m2Deleted = await context.Chapters.AnyAsync(ch => ch.Id == module2Id);
        if (m2Deleted)
        {
            Fail("TEST D (SQL Server Check)", "Module 2 still exists in SQL Server after deletion!");
            return;
        }

        // Verify parent course is intact
        var parentCourseStillExists = await context.Courses.AnyAsync(c => c.Id == courseId);
        if (!parentCourseStillExists)
        {
            Fail("TEST D (Parent Preservation)", "Deleting module deleted parent course!");
            return;
        }
        Pass("TEST D (Delete Module): Module 2 safely deleted from SQL Server; parent Course remained completely intact");

        // =========================================================================
        // TEST F — BLOCKED DELETE (With Active Student Progress)
        // =========================================================================
        // Add CourseItem + Student Progress to Module 1
        var item = new CourseItem
        {
            ChapterId = module1Id,
            Title = "Protected Lesson",
            Position = 1,
            Type = ItemType.Lesson
        };
        context.CourseItems.Add(item);
        await context.SaveChangesAsync();

        var prog = new CourseProgress
        {
            CourseItemId = item.Id,
            StudentId = student.Id,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        };
        context.CourseProgresses.Add(prog);
        await context.SaveChangesAsync();

        // Attempt deletion of Module 1 with student data
        var blockCtrl = CreateController(isAjax: true);
        var blockResult = await blockCtrl.DeleteModule(module1Id, courseId);
        if (blockResult is not JsonResult blockJson || blockJson.Value == null)
        {
            Fail("TEST F (Blocked Delete)", "DeleteModule did not return JsonResult for blocked deletion.");
            return;
        }
        var bData = blockJson.Value;
        var bBlocked = (bool?)bData.GetType().GetProperty("blocked")?.GetValue(bData);
        var bSuccess = (bool?)bData.GetType().GetProperty("success")?.GetValue(bData);
        var bMsg = bData.GetType().GetProperty("message")?.GetValue(bData)?.ToString();

        if (bBlocked != true || bSuccess != false || string.IsNullOrEmpty(bMsg) || !bMsg.Contains("student learning records"))
        {
            Fail("TEST F (Blocked Payload)", $"Expected blocked=true with clear message, got: {bMsg}");
            return;
        }

        // Verify in SQL Server that module and student records STILL exist
        var mod1StillInDb = await context.Chapters.AnyAsync(ch => ch.Id == module1Id);
        var progStillInDb = await context.CourseProgresses.AnyAsync(cp => cp.Id == prog.Id);
        if (!mod1StillInDb || !progStillInDb)
        {
            Fail("TEST F (Data Preservation)", "Module or student progress deleted despite blocking!");
            return;
        }
        Pass("TEST F (Dependency Safety Guard): Deletion blocked with clean user message; Module and student progress preserved in SQL Server");

        // Clean up test courses
        context.Courses.Remove(testCourse);
        context.Courses.Remove(emptyCourse);
        await context.SaveChangesAsync();

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"🎉  ALL {totalTests} UI INTEGRATION TESTS PASSED SUCCESSFULLY (100%)!");
        Console.WriteLine("=================================================================\n");
    }

    private class TestTempDataProvider : ITempDataProvider
    {
        private readonly System.Collections.Generic.Dictionary<string, object?> _store = new();

        public System.Collections.Generic.IDictionary<string, object> LoadTempData(HttpContext context)
        {
            return _store.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!);
        }

        public void SaveTempData(HttpContext context, System.Collections.Generic.IDictionary<string, object> values)
        {
            foreach (var kv in values)
            {
                _store[kv.Key] = kv.Value;
            }
        }
    }

    private class DummyUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; }

        public DummyUrlHelper(ActionContext actionContext)
        {
            ActionContext = actionContext;
        }

        public string? Action(UrlActionContext actionContext) => $"/Academic/{actionContext.Action}";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? RouteUrl(UrlRouteContext routeContext) => "/";
        public string? Link(string? routeName, object? values) => "/";
    }
}
