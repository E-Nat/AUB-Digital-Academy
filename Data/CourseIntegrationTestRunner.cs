using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

public static class CourseIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("🚀 AUB DIGITAL ACADEMY - STEP 1.7 COMPLETE COURSE CRUD INTEGRATION TEST");
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
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var courseService = scope.ServiceProvider.GetRequiredService<ICourseService>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var controllerLogger = loggerFactory.CreateLogger<AcademicController>();

        // 0. Database connectivity check
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            Fail("Database Connectivity", "Cannot connect to SQL Server.");
            return;
        }
        Pass("SQL Server connected and healthy");

        // Clean up previous test artifacts
        await CleanupTestCoursesAsync(context);

        // Load reference entities
        var teachers = await userManager.GetUsersInRoleAsync("Teacher");
        var activeTeacher = teachers.FirstOrDefault(u => u.IsActive) ?? await context.Users.FirstOrDefaultAsync(u => u.IsActive);
        if (activeTeacher == null)
        {
            Fail("Teacher Lookup", "No active teacher found in SQL Server.");
            return;
        }

        var major = await context.Majors.Include(m => m.Category).FirstOrDefaultAsync();
        if (major == null)
        {
            Fail("Major Lookup", "No major found in SQL Server.");
            return;
        }

        var semester = await context.Semesters.FirstOrDefaultAsync();
        if (semester == null)
        {
            Fail("Semester Lookup", "No semester found in SQL Server.");
            return;
        }

        var student = await context.Users.FirstOrDefaultAsync(u => u.Email == "student@aub.com")
            ?? (await userManager.GetUsersInRoleAsync("Student")).FirstOrDefault();
        if (student == null)
        {
            Fail("Student Lookup", "No student found in SQL Server.");
            return;
        }

        // Helper to create controller with Admin claims
        AcademicController CreateAdminController(bool isAjax = false)
        {
            var httpContext = new DefaultHttpContext();
            if (isAjax)
            {
                httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
            }
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "ADMIN001"),
                new(ClaimTypes.Name, "admin@aub.com"),
                new(ClaimTypes.Role, "Admin")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            httpContext.User = new ClaimsPrincipal(identity);

            var tempDataProvider = new TestTempDataProvider();
            var tempData = new TempDataDictionary(httpContext, tempDataProvider);
            var actionContext = new ActionContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
                ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
            };

            return new AcademicController(courseService, context, userManager, controllerLogger)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = tempData,
                Url = new DummyUrlHelper(actionContext)
            };
        }

        // =================================================================
        // SCENARIO TEST 1: Admin views Course Management (READ)
        // =================================================================
        Console.WriteLine("\n--- SCENARIO TEST 1: READ / COURSE LIST ---");
        var controller = CreateAdminController();
        var indexResult = await controller.Course();
        if (indexResult is ViewResult vr && vr.Model is CourseManagementViewModel vm)
        {
            Pass($"Admin opened Course Management: {vm.TotalCourses} real courses loaded from SQL Server (Published: {vm.PublishedCourses}, Drafts: {vm.DraftCourses})");
            Pass($"Real Dropdown Data Pre-populated: {vm.CreateModel.TeacherOptions.Count} Teachers, {vm.CreateModel.MajorOptions.Count} Majors, {vm.CreateModel.SemesterOptions.Count} Semesters, {vm.CreateModel.CategoryOptions.Count} Categories");
        }
        else
        {
            Fail("Scenario 1", "Did not return CourseManagementViewModel view result.");
            return;
        }

        // =================================================================
        // SCENARIO TEST 2: Admin creates a course (CREATE)
        // =================================================================
        Console.WriteLine("\n--- SCENARIO TEST 2: CREATE COURSE (Draft & Published) ---");
        string testCodeDraft = "TEST-INT-DRAFT-" + DateTime.UtcNow.Ticks.ToString()[^5..];
        var draftCreate = new CourseCreateViewModel
        {
            Title = "Integration Test Draft Course",
            Code = testCodeDraft,
            Description = "Automated integration testing for draft status persistence.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CategoryId = major.CategoryId,
            Thumbnail = null
        };

        controller = CreateAdminController();
        var draftResult = await controller.CreateCourse(draftCreate, "draft");
        if (draftResult is RedirectToActionResult draftRedirect && draftRedirect.ActionName == "Course")
        {
            var savedDraft = await context.Courses.FirstOrDefaultAsync(c => c.Code == testCodeDraft);
            if (savedDraft != null && savedDraft.Status == CourseStatus.Draft)
            {
                Pass($"Created Draft Course: Saved to SQL Server with ID={savedDraft.Id}, Status=Draft, Code={testCodeDraft}");
            }
            else
            {
                Fail("Scenario 2 Draft", "Draft course was not saved with status Draft in SQL Server.");
                return;
            }
        }
        else
        {
            Fail("Scenario 2 Draft", "CreateCourse draft did not redirect to Course action.");
            return;
        }

        // Create published course
        string testCodePub = "TEST-INT-PUB-" + DateTime.UtcNow.Ticks.ToString()[^5..];
        var pubCreate = new CourseCreateViewModel
        {
            Title = "Integration Test Published Course",
            Code = testCodePub,
            Description = "Automated integration testing for published status persistence.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CategoryId = major.CategoryId,
            Thumbnail = "assets/images/course_test.jpg"
        };

        controller = CreateAdminController(isAjax: true);
        var pubResult = await controller.CreateCourse(pubCreate, "publish");
        int publishedCourseId = 0;
        if (pubResult is JsonResult jrPub)
        {
            var jsonText = System.Text.Json.JsonSerializer.Serialize(jrPub.Value);
            var parsed = System.Text.Json.JsonDocument.Parse(jsonText);
            if (parsed.RootElement.GetProperty("success").GetBoolean())
            {
                publishedCourseId = parsed.RootElement.GetProperty("courseId").GetInt32();
                var savedPub = await context.Courses.FindAsync(publishedCourseId);
                if (savedPub != null && savedPub.Status == CourseStatus.Published && savedPub.Thumbnail == "assets/images/course_test.jpg")
                {
                    Pass($"Created Published Course via AJAX: Saved to SQL Server with ID={publishedCourseId}, Status=Published, Code={testCodePub}");
                }
                else
                {
                    Fail("Scenario 2 Publish", "Published course not found or status not Published in SQL Server.");
                    return;
                }
            }
            else
            {
                Fail("Scenario 2 Publish", "JSON response indicated failure.");
                return;
            }
        }

        // Duplicate code validation check
        controller = CreateAdminController();
        var dupResult = await controller.CreateCourse(new CourseCreateViewModel
        {
            Title = "Duplicate Code Course",
            Code = testCodePub, // duplicate!
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id
        }, "publish");
        if (!controller.ModelState.IsValid)
        {
            Pass("Duplicate Course Code validation: Blocked creation of course with existing code.");
        }
        else
        {
            Fail("Validation Check", "Duplicate course code was erroneously allowed.");
            return;
        }

        // =================================================================
        // SCENARIO TEST 3: Admin edits the course (EDIT)
        // =================================================================
        Console.WriteLine("\n--- SCENARIO TEST 3: EDIT COURSE ---");
        // Fetch fresh course via GetCourseJson
        controller = CreateAdminController();
        var jsonGet = await controller.GetCourseJson(publishedCourseId);
        if (jsonGet is JsonResult jrGet)
        {
            var jsonText = System.Text.Json.JsonSerializer.Serialize(jrGet.Value);
            var parsed = System.Text.Json.JsonDocument.Parse(jsonText);
            if (parsed.RootElement.GetProperty("success").GetBoolean())
            {
                var courseObj = parsed.RootElement.GetProperty("course");
                Pass($"GetCourseJson({publishedCourseId}) loaded existing course data from SQL Server: '{courseObj.GetProperty("title").GetString()}'");
            }
            else
            {
                Fail("Scenario 3 Load", "GetCourseJson returned success=false.");
                return;
            }
        }

        // Submit edit
        string updatedTitle = "Integration Test Published Course - EDITED";
        string updatedDesc = "Updated curriculum overview and description.";
        controller = CreateAdminController();
        var editModel = new CourseEditViewModel
        {
            Id = publishedCourseId,
            Title = updatedTitle,
            Code = testCodePub,
            Description = updatedDesc,
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CategoryId = major.CategoryId,
            Thumbnail = "assets/images/course_test_updated.jpg",
            Status = CourseStatus.Published
        };
        var editResult = await controller.EditCourse(editModel);
        if (editResult is RedirectToActionResult editRedirect && editRedirect.ActionName == "Course")
        {
            // Verify changes saved to SQL Server
            var updatedCourse = await context.Courses.FindAsync(publishedCourseId);
            if (updatedCourse != null && updatedCourse.Title == updatedTitle && updatedCourse.Description == updatedDesc)
            {
                Pass($"Course edited and updated in SQL Server: Title='{updatedCourse.Title}', Description='{updatedCourse.Description}'");
            }
            else
            {
                Fail("Scenario 3 Save", "Course changes were not saved to SQL Server.");
                return;
            }
        }
        else
        {
            Fail("Scenario 3 Save", "EditCourse did not redirect to Course action.");
            return;
        }

        // Simulate browser refresh & restart: read from new DbContext scope
        using (var refreshScope = serviceProvider.CreateScope())
        {
            var freshDb = refreshScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var refreshedCourse = await freshDb.Courses.FindAsync(publishedCourseId);
            if (refreshedCourse != null && refreshedCourse.Title == updatedTitle)
            {
                Pass("Refresh & Restart verification: Edited course data persists correctly in SQL Server across scopes");
            }
            else
            {
                Fail("Scenario 3 Refresh", "Edited data did not persist across DbContext scopes.");
                return;
            }
        }

        // =================================================================
        // SCENARIO TEST 4: Temporary empty course deletion (DELETE)
        // =================================================================
        Console.WriteLine("\n--- SCENARIO TEST 4: SAFE DELETE EMPTY COURSE ---");
        string tempCode = "TEST-INT-TMP-" + DateTime.UtcNow.Ticks.ToString()[^5..];
        var tempCourse = new Course
        {
            Title = "Temporary Disposable Course",
            Code = tempCode,
            Description = "To be deleted safely.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            Status = CourseStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(tempCourse);
        await context.SaveChangesAsync();
        int tempCourseId = tempCourse.Id;
        Pass($"Created temporary empty course with ID={tempCourseId}");

        // Delete via AJAX (as performed by the modal in Course.cshtml)
        controller = CreateAdminController(isAjax: true);
        var deleteResult = await controller.DeleteCourse(tempCourseId);
        if (deleteResult is JsonResult jrDel)
        {
            var jsonText = System.Text.Json.JsonSerializer.Serialize(jrDel.Value);
            var parsed = System.Text.Json.JsonDocument.Parse(jsonText);
            if (parsed.RootElement.GetProperty("success").GetBoolean())
            {
                // Verify deleted from SQL Server
                var checkDb = await context.Courses.FindAsync(tempCourseId);
                if (checkDb == null)
                {
                    Pass($"Course ID={tempCourseId} deleted safely from SQL Server via AJAX DeleteCourse");
                }
                else
                {
                    Fail("Scenario 4 Delete", "Course still exists in SQL Server after deletion!");
                    return;
                }
            }
            else
            {
                Fail("Scenario 4 Delete", "DeleteCourse AJAX returned success=false.");
                return;
            }
        }

        // Confirm across fresh scope
        using (var freshScope2 = serviceProvider.CreateScope())
        {
            var freshDb2 = freshScope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var exists = await freshDb2.Courses.AnyAsync(c => c.Id == tempCourseId);
            if (!exists)
            {
                Pass("Refresh verification: Deleted course remains deleted in SQL Server");
            }
            else
            {
                Fail("Scenario 4 Refresh", "Deleted course reappeared in SQL Server!");
                return;
            }
        }

        // =================================================================
        // SCENARIO TEST 5: Student learning data protection (SAFE DELETE BLOCK)
        // =================================================================
        Console.WriteLine("\n--- SCENARIO TEST 5: STUDENT DATA PROTECTION ---");
        // Enroll student in the published course
        var enrollment = new Enrollment
        {
            CourseId = publishedCourseId,
            StudentId = student.Id,
            EnrolledAt = DateTime.UtcNow
        };
        context.Enrollments.Add(enrollment);
        await context.SaveChangesAsync();
        Pass($"Student '{student.FullName}' enrolled in course ID={publishedCourseId}");

        // Attempt deletion
        controller = CreateAdminController(isAjax: true);
        var blockedResult = await controller.DeleteCourse(publishedCourseId);
        if (blockedResult is JsonResult jrBlock)
        {
            var jsonText = System.Text.Json.JsonSerializer.Serialize(jrBlock.Value);
            var parsed = System.Text.Json.JsonDocument.Parse(jsonText);
            bool success = parsed.RootElement.GetProperty("success").GetBoolean();
            bool blocked = parsed.RootElement.GetProperty("blocked").GetBoolean();
            string msg = parsed.RootElement.GetProperty("message").GetString() ?? "";

            if (!success && blocked && msg.Contains("student enrollment or learning records"))
            {
                Pass($"Safe Delete BLOCKED: {msg}");

                // Confirm course and enrollment remain completely intact in SQL Server
                var courseStillExists = await context.Courses.AnyAsync(c => c.Id == publishedCourseId);
                var enrollmentStillExists = await context.Enrollments.AnyAsync(e => e.CourseId == publishedCourseId && e.StudentId == student.Id);
                if (courseStillExists && enrollmentStillExists)
                {
                    Pass("Student records protected: Course and Enrollment persist safely in SQL Server");
                }
                else
                {
                    Fail("Scenario 5 Protection", "Course or enrollment was lost!");
                    return;
                }
            }
            else
            {
                Fail("Scenario 5 Block", "Deletion was not blocked as expected.");
                return;
            }
        }

        // Clean up test records
        await CleanupTestCoursesAsync(context);
        Pass("Cleaned up temporary integration test data from SQL Server");

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"🎉 ALL {testsPassed} INTEGRATION TESTS PASSED (100% SUCCESS)");
        Console.WriteLine("=================================================================\n");
    }

    private static async Task CleanupTestCoursesAsync(ApplicationDbContext db)
    {
        var testCourses = await db.Courses
            .Where(c => c.Code.StartsWith("TEST-INT-"))
            .Select(c => c.Id)
            .ToListAsync();

        if (testCourses.Any())
        {
            var enrolls = await db.Enrollments
                .Where(e => testCourses.Contains(e.CourseId))
                .ToListAsync();
            db.Enrollments.RemoveRange(enrolls);

            var courses = await db.Courses
                .Where(c => testCourses.Contains(c.Id))
                .ToListAsync();
            db.Courses.RemoveRange(courses);

            await db.SaveChangesAsync();
        }
    }

    private class TestTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object?> _store = new();

        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            return _store.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!);
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
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
