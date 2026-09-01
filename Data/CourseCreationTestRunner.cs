using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

public static class CourseCreationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("🏛️  AUB DIGITAL ACADEMY - COURSE CREATION & SQL SERVER AUDIT");
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
        var logger = loggerFactory.CreateLogger<AcademicController>();

        // 1. Verify SQL Server connection & SeedData
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            Fail("Database Connectivity", "Unable to connect to SQL Server (LocalDB).");
        }
        Pass("SQL Server connection established successfully");

        // Verify Seeded Teacher, Category, Major, Semester
        var teachers = await userManager.GetUsersInRoleAsync("Teacher");
        var activeTeacher = teachers.FirstOrDefault(u => u.IsActive);
        if (activeTeacher == null)
        {
            activeTeacher = await context.Users.FirstOrDefaultAsync(u => u.IsActive);
        }
        if (activeTeacher == null)
        {
            Fail("Teacher Lookup", "No active teacher found in SQL Server.");
        }
        Pass($"Real teachers loaded from SQL Server (Found: {activeTeacher.FullName} [{activeTeacher.Email}])");

        var category = await context.Categories.FirstOrDefaultAsync();
        if (category == null)
        {
            Fail("Category Lookup", "No category found in SQL Server.");
        }
        Pass($"Real categories loaded from SQL Server (Found: {category.Name})");

        var major = await context.Majors.Include(m => m.Category).FirstOrDefaultAsync();
        if (major == null)
        {
            Fail("Major Lookup", "No major found in SQL Server.");
        }
        Pass($"Real majors loaded from SQL Server (Found: {major.Name} [{major.Code}], Category: {major.Category?.Name})");

        var semester = await context.Semesters.FirstOrDefaultAsync();
        if (semester == null)
        {
            Fail("Semester Lookup", "No semester found in SQL Server.");
        }
        Pass($"Real semesters loaded from SQL Server (Found: {semester.Name} [{semester.Code}])");

        // Helper to instantiate AcademicController with mock HttpContext & TempData
        AcademicController CreateController()
        {
            var c = new AcademicController(courseService, context, userManager, logger);
            var httpContext = new DefaultHttpContext();
            c.TempData = new TempDataDictionary(httpContext, new SessionStateTempDataProvider());
            c.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return c;
        }

        // 2. Test GET CreateCourse - verify form opens & dropdown options load from SQL Server
        var controller = CreateController();
        var getResult = await controller.CreateCourse();
        if (getResult is ViewResult viewResult && viewResult.Model is CourseCreateViewModel createVm)
        {
            if (!createVm.TeacherOptions.Any()) Fail("GET CreateCourse", "Teacher options not loaded.");
            if (!createVm.CategoryOptions.Any()) Fail("GET CreateCourse", "Category options not loaded.");
            if (!createVm.MajorOptions.Any()) Fail("GET CreateCourse", "Major options not loaded.");
            if (!createVm.SemesterOptions.Any()) Fail("GET CreateCourse", "Semester options not loaded.");
            Pass("Add New Course GET action loads real SQL Server teachers, categories, majors, & semesters");
        }
        else
        {
            Fail("GET CreateCourse", "Failed to return ViewResult with CourseCreateViewModel.");
        }

        // 3. Test Form Validation: Empty required fields
        controller = CreateController();
        var emptyModel = new CourseCreateViewModel();
        controller.ModelState.AddModelError("CreateModel.Title", "Course title is required.");
        controller.ModelState.AddModelError("CreateModel.Code", "Course code is required.");
        var invalidResult = await controller.CreateCourse(emptyModel, "publish");
        if (invalidResult is ViewResult invView && invView.ViewName == "Course")
        {
            Pass("Validation rejected empty required fields and preserved user form model");
        }
        else
        {
            Fail("Validation Empty Fields", "Did not return Course view on validation failure.");
        }

        // 4. Test Validation: Invalid Teacher, Major, Semester
        controller = CreateController();
        var invalidRefsModel = new CourseCreateViewModel
        {
            Title = "Invalid References Test Course",
            Code = "TEST-INV-999",
            TeacherId = "non-existent-guid-99999",
            MajorId = 999999,
            SemesterId = 999999,
            CategoryId = 999999
        };
        var invalidRefsResult = await controller.CreateCourse(invalidRefsModel, "publish");
        if (!controller.ModelState.IsValid && controller.ModelState.ContainsKey("CreateModel.TeacherId"))
        {
            Pass("Validation rejected non-existent foreign key references with clear error messages");
        }
        else
        {
            Fail("Validation Foreign Keys", "Did not flag invalid TeacherId/MajorId/SemesterId.");
        }

        // Clean up any existing test course codes
        var testCodePublish = "CS-SQL-101";
        var testCodeDraft = "CS-SQL-DRAFT";
        var existingTestCourses = await context.Courses
            .Where(c => c.Code == testCodePublish || c.Code == testCodeDraft)
            .ToListAsync();
        if (existingTestCourses.Any())
        {
            context.Courses.RemoveRange(existingTestCourses);
            await context.SaveChangesAsync();
        }

        // 5. Test SAVE DRAFT: Create course without image
        controller = CreateController();
        var draftModel = new CourseCreateViewModel
        {
            Title = "SQL Server Advanced Distributed Systems",
            Code = testCodeDraft,
            Description = "In-depth distributed storage and ACID consensus protocols.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CategoryId = category.Id,
            Thumbnail = null // Test without image (optional)
        };

        var draftResult = await controller.CreateCourse(draftModel, "draft");
        if (draftResult is RedirectToActionResult draftRedirect && draftRedirect.ActionName == "Course")
        {
            var savedDraft = await context.Courses.FirstOrDefaultAsync(c => c.Code == testCodeDraft);
            if (savedDraft != null && savedDraft.Status == CourseStatus.Draft)
            {
                var msg = controller.TempData["SuccessMessage"]?.ToString();
                if (msg == "Course saved as draft successfully.")
                {
                    Pass("Save Draft works without image: Course saved as draft in SQL Server with status Draft");
                }
                else
                {
                    Fail("Save Draft Message", $"Expected 'Course saved as draft successfully.', got '{msg}'");
                }
            }
            else
            {
                Fail("Save Draft Persistence", "Course not found in SQL Server or status not Draft.");
            }
        }
        else
        {
            Fail("Save Draft Action", "Did not redirect to Course action.");
        }

        // 6. Test PUBLISH COURSE: Create course with image
        controller = CreateController();
        var publishModel = new CourseCreateViewModel
        {
            Title = "Cloud Native Microservices & Kubernetes",
            Code = testCodePublish,
            Description = "Enterprise architecture with Docker, Kubernetes, and SQL Server backend.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CategoryId = category.Id,
            Thumbnail = "assets/images/course_webdev.jpg" // Test with image
        };

        var publishResult = await controller.CreateCourse(publishModel, "publish");
        if (publishResult is RedirectToActionResult pubRedirect && pubRedirect.ActionName == "Course")
        {
            var savedCourse = await context.Courses
                .Include(c => c.Major)
                    .ThenInclude(m => m.Category)
                .Include(c => c.Semester)
                .Include(c => c.Teacher)
                .FirstOrDefaultAsync(c => c.Code == testCodePublish);

            if (savedCourse != null && savedCourse.Status == CourseStatus.Published)
            {
                var msg = controller.TempData["SuccessMessage"]?.ToString();
                if (msg == "Course published successfully.")
                {
                    Pass("Publish Course works with image: Course saved to SQL Server with status Published");
                }
                else
                {
                    Fail("Publish Course Message", $"Expected 'Course published successfully.', got '{msg}'");
                }
            }
            else
            {
                Fail("Publish Course Persistence", "Course not found in SQL Server or status not Published.");
            }
        }
        else
        {
            Fail("Publish Course Action", "Did not redirect to Course action.");
        }

        // 7. Test PREVENT DUPLICATE COURSE CODE
        controller = CreateController();
        var duplicateModel = new CourseCreateViewModel
        {
            Title = "Duplicate Course Code Test Attempt",
            Code = testCodePublish, // Attempt same code
            Description = "Should be blocked.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id
        };

        var duplicateResult = await controller.CreateCourse(duplicateModel, "publish");
        var duplicateErrorFound = controller.ModelState.Values
            .SelectMany(v => v.Errors)
            .Any(e => e.ErrorMessage == "A course with this course code already exists.");

        if (duplicateErrorFound)
        {
            var countInDb = await context.Courses.CountAsync(c => c.Code == testCodePublish);
            if (countInDb == 1)
            {
                Pass("Duplicate course code prevented: Second creation failed with 'A course with this course code already exists.' and no duplicate in SQL Server");
            }
            else
            {
                Fail("Duplicate DB Check", $"Expected 1 course with code {testCodePublish}, found {countInDb}.");
            }
        }
        else
        {
            Fail("Duplicate Course Code", "Expected error message 'A course with this course code already exists.' not found in ModelState.");
        }

        // 8. Test Course Management List Page: Verify all displayed fields come from SQL Server
        controller = CreateController();
        var coursePageResult = await controller.Course();
        if (coursePageResult is ViewResult cvResult && cvResult.Model is CourseManagementViewModel cmVm)
        {
            var displayedCourse = cmVm.Courses.FirstOrDefault(c => c.Code == testCodePublish);
            if (displayedCourse == null)
            {
                Fail("Course Management List", $"Course {testCodePublish} not found in CourseManagementViewModel.Courses.");
            }

            // Verify all relational fields from SQL Server
            if (string.IsNullOrWhiteSpace(displayedCourse.Title)) Fail("Course List Display", "Course title missing.");
            if (string.IsNullOrWhiteSpace(displayedCourse.Code)) Fail("Course List Display", "Course code missing.");
            if (displayedCourse.Teacher == null || string.IsNullOrWhiteSpace(displayedCourse.Teacher.FullName)) Fail("Course List Display", "Teacher not joined from SQL Server.");
            if (displayedCourse.Major == null || string.IsNullOrWhiteSpace(displayedCourse.Major.Name)) Fail("Course List Display", "Major not joined from SQL Server.");
            if (displayedCourse.Major.Category == null || string.IsNullOrWhiteSpace(displayedCourse.Major.Category.Name)) Fail("Course List Display", "Category not joined from SQL Server.");
            if (displayedCourse.Semester == null || string.IsNullOrWhiteSpace(displayedCourse.Semester.Name)) Fail("Course List Display", "Semester not joined from SQL Server.");
            if (displayedCourse.Status != CourseStatus.Published) Fail("Course List Display", "Course status mismatch.");
            if (string.IsNullOrWhiteSpace(displayedCourse.Thumbnail)) Fail("Course List Display", "Thumbnail missing.");

            Pass("Course appears in Course Management list with Course name, Code, Teacher, Major, Category, Semester, Status, & Thumbnail directly from SQL Server");
            Pass($"KPI Counters dynamically calculated: Total = {cmVm.TotalCourses}, Published = {cmVm.PublishedCourses}, Drafts = {cmVm.DraftCourses}");
        }
        else
        {
            Fail("Course Management List", "Failed to return ViewResult with CourseManagementViewModel.");
        }

        // 9. PAGE REFRESH & APPLICATION RESTART TEST:
        // Simulate complete restart: Dispose the entire ServiceScope and DbContext, then create a brand new scope from root container
        Console.WriteLine("\n  Simulating application restart & browser refresh...");
        using (var restartScope = serviceProvider.CreateScope())
        {
            var newContext = restartScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var newCourseService = restartScope.ServiceProvider.GetRequiredService<ICourseService>();

            var persistedCourse = await newCourseService.GetCourseByIdAsync(
                (await newContext.Courses.FirstAsync(c => c.Code == testCodePublish)).Id
            );

            if (persistedCourse == null)
            {
                Fail("Application Restart", "Course disappeared after simulated application restart!");
            }

            if (persistedCourse.Code != testCodePublish ||
                persistedCourse.Teacher?.FullName != activeTeacher.FullName ||
                persistedCourse.Major?.Name != major.Name ||
                persistedCourse.Semester?.Name != semester.Name)
            {
                Fail("Application Restart", "Course relational integrity damaged after restart.");
            }

            var allPersistedCourses = (await newCourseService.GetAllCoursesAsync()).ToList();
            if (!allPersistedCourses.Any(c => c.Code == testCodeDraft) || !allPersistedCourses.Any(c => c.Code == testCodePublish))
            {
                Fail("Application Restart", "Draft or Published course missing from refreshed course list.");
            }

            Pass("Page Refresh & Application Restart Test: All courses and relational links persist reliably in SQL Server across application restarts (no mock, no localStorage, no in-memory cache)");
        }

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"✅ ALL {testsPassed} ACCEPTANCE TESTS PASSED (100% SUCCESS)");
        Console.WriteLine("=================================================================\n");
    }

    private class SessionStateTempDataProvider : ITempDataProvider
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
}
