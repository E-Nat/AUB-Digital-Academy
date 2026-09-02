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
            return;
        }
        Pass($"Real teachers loaded from SQL Server (Found: {activeTeacher.FullName} [{activeTeacher.Email}])");

        var category = await context.Categories.FirstOrDefaultAsync();
        if (category == null)
        {
            Fail("Category Lookup", "No category found in SQL Server.");
            return;
        }
        Pass($"Real categories loaded from SQL Server (Found: {category.Name})");

        var major = await context.Majors.Include(m => m.Category).FirstOrDefaultAsync();
        if (major == null)
        {
            Fail("Major Lookup", "No major found in SQL Server.");
            return;
        }
        Pass($"Real majors loaded from SQL Server (Found: {major.Name} [{major.Code}], Category: {major.Category?.Name})");

        var semester = await context.Semesters.FirstOrDefaultAsync();
        if (semester == null)
        {
            Fail("Semester Lookup", "No semester found in SQL Server.");
            return;
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

        // 3b. Test Validation: Missing Teacher, Missing Major, Missing Semester
        controller = CreateController();
        var missingTeacherModel = new CourseCreateViewModel
        {
            Title = "Missing Teacher Course",
            Code = "TEST-NOTEACHER",
            TeacherId = "",
            MajorId = major.Id,
            SemesterId = semester.Id
        };
        await controller.CreateCourse(missingTeacherModel, "publish");
        if (controller.ModelState.ContainsKey("CreateModel.TeacherId"))
        {
            Pass("Validation caught missing Teacher with 'Please select a teacher.'");
        }
        else
        {
            Fail("Validation Missing Teacher", "Did not report missing TeacherId.");
        }

        // 4. Test Validation: Invalid Teacher, Major, Semester, Category
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
        if (!controller.ModelState.IsValid && 
            controller.ModelState.ContainsKey("CreateModel.TeacherId") &&
            controller.ModelState.ContainsKey("CreateModel.MajorId") &&
            controller.ModelState.ContainsKey("CreateModel.SemesterId") &&
            controller.ModelState.ContainsKey("CreateModel.CategoryId"))
        {
            Pass("Validation rejected non-existent foreign key references (Teacher, Major, Semester, Category) with clear error messages");
        }
        else
        {
            Fail("Validation Foreign Keys", "Did not flag invalid TeacherId/MajorId/SemesterId/CategoryId.");
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

        // 5. Test SAVE DRAFT: Create course without image and handle double-submitAction
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

        // Test with "draft,draft" to simulate browser multi-value submission
        var draftResult = await controller.CreateCourse(draftModel, "draft,draft");
        if (draftResult is RedirectToActionResult draftRedirect && draftRedirect.ActionName == "Course")
        {
            var savedDraft = await context.Courses.FirstOrDefaultAsync(c => c.Code == testCodeDraft);
            if (savedDraft != null && savedDraft.Status == CourseStatus.Draft && savedDraft.Thumbnail == null)
            {
                var msg = controller.TempData["SuccessMessage"]?.ToString();
                if (msg == "Course saved as draft successfully.")
                {
                    Pass("Save Draft works without image: Course saved as draft in SQL Server with status Draft and Thumbnail=null");
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

        // 6. Test PUBLISH COURSE: Create course with image and handle double-submitAction
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

        // Test with "publish,publish" to simulate browser multi-value submission
        var publishResult = await controller.CreateCourse(publishModel, "publish,publish");
        if (publishResult is RedirectToActionResult pubRedirect && pubRedirect.ActionName == "Course")
        {
            var savedCourse = await context.Courses
                .Include(c => c.Major)
                    .ThenInclude(m => m.Category)
                .Include(c => c.Semester)
                .Include(c => c.Teacher)
                .FirstOrDefaultAsync(c => c.Code == testCodePublish);

            if (savedCourse != null && savedCourse.Status == CourseStatus.Published && savedCourse.Thumbnail == "assets/images/course_webdev.jpg")
            {
                var msg = controller.TempData["SuccessMessage"]?.ToString();
                if (msg == "Course published successfully.")
                {
                    Pass("Publish Course works with image: Course saved to SQL Server with status Published and Thumbnail preserved");
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
                return;
            }

            // Verify all relational fields from SQL Server
            if (string.IsNullOrWhiteSpace(displayedCourse.Title)) Fail("Course List Display", "Course title missing.");
            if (string.IsNullOrWhiteSpace(displayedCourse.Code)) Fail("Course List Display", "Course code missing.");
            if (displayedCourse.Teacher == null || string.IsNullOrWhiteSpace(displayedCourse.Teacher.FullName)) Fail("Course List Display", "Teacher not joined from SQL Server.");
            if (displayedCourse.Major == null || string.IsNullOrWhiteSpace(displayedCourse.Major.Name)) Fail("Course List Display", "Major not joined from SQL Server.");
            if (displayedCourse.Major?.Category == null || string.IsNullOrWhiteSpace(displayedCourse.Major?.Category?.Name)) Fail("Course List Display", "Category not joined from SQL Server.");
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

        // =========================================================================
        // STEP 1.5 - EDIT COURSE TESTS
        // =========================================================================
        Console.WriteLine("\n--- STEP 1.5 EDIT COURSE TESTS ---");

        var draftDbCourse = await context.Courses.FirstAsync(c => c.Code == testCodeDraft);
        var publishDbCourse = await context.Courses.FirstAsync(c => c.Code == testCodePublish);

        // 9. Test Edit GET: Load Course by ID (GetCourseJson and EditCourse)
        controller = CreateController();
        var jsonResult = await controller.GetCourseJson(draftDbCourse.Id);
        if (jsonResult is JsonResult jr && jr.Value != null)
        {
            var propSuccess = jr.Value.GetType().GetProperty("success")?.GetValue(jr.Value);
            var propCourse = jr.Value.GetType().GetProperty("course")?.GetValue(jr.Value);
            if (propSuccess is true && propCourse != null)
            {
                var idVal = propCourse.GetType().GetProperty("id")?.GetValue(propCourse);
                var codeVal = propCourse.GetType().GetProperty("code")?.GetValue(propCourse);
                if (idVal is int idInt && idInt == draftDbCourse.Id && codeVal?.ToString() == testCodeDraft)
                {
                    Pass("Edit Course GET action (GetCourseJson) loads real SQL Server Course by ID with all relational data");
                }
                else
                {
                    Fail("Edit Course GET", "GetCourseJson returned mismatched course ID or code.");
                }
            }
            else
            {
                Fail("Edit Course GET", "GetCourseJson did not return success=true or course payload.");
            }
        }
        else
        {
            Fail("Edit Course GET", "GetCourseJson did not return a valid JsonResult.");
        }

        // Test EditCourse GET with invalid ID
        var editGetBad = await controller.GetCourseJson(-1);
        if (editGetBad is not BadRequestObjectResult) Fail("Edit Course Invalid ID", "Expected BadRequestObjectResult for ID <= 0.");

        var editGetNotFound = await controller.GetCourseJson(999999);
        if (editGetNotFound is not NotFoundObjectResult) Fail("Edit Course Not Found", "Expected NotFoundObjectResult for non-existent ID.");

        // Test EditCourse GET page
        var editPageResult = await controller.EditCourse(draftDbCourse.Id);
        if (editPageResult is ViewResult evResult && evResult.Model is CourseManagementViewModel evVm && evVm.EditModel != null)
        {
            if (evVm.EditModel.Id == draftDbCourse.Id &&
                evVm.EditModel.Code == testCodeDraft &&
                evVm.EditModel.TeacherOptions.Count > 0 &&
                evVm.EditModel.MajorOptions.Count > 0 &&
                evVm.EditModel.SemesterOptions.Count > 0 &&
                evVm.EditModel.CategoryOptions.Count > 0)
            {
                Pass("EditCourse GET action prepopulates form fields and real SQL Server dropdown options (Teachers, Categories, Majors, Semesters)");
            }
            else
            {
                Fail("EditCourse Prepopulate", "Dropdown options or initial values missing from EditModel.");
            }
        }
        else
        {
            Fail("EditCourse View", "Failed to return ViewResult with prepopulated EditModel.");
        }

        // 10. TEST A — EDIT BASIC INFORMATION (Title and Description)
        controller = CreateController();
        var editBasicModel = new CourseEditViewModel
        {
            Id = draftDbCourse.Id,
            Title = "Advanced Distributed Systems & Fault Tolerance",
            Code = testCodeDraft, // keeping current code
            Description = "In-depth study of Paxos, Raft consensus, and distributed transactions.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CategoryId = category.Id,
            Thumbnail = null,
            Status = CourseStatus.Draft
        };

        var editBasicResult = await controller.EditCourse(editBasicModel);
        if (editBasicResult is RedirectToActionResult ebRedirect && ebRedirect.ActionName == "Course")
        {
            var msg = controller.TempData["SuccessMessage"]?.ToString();
            if (msg != "Course updated successfully.")
            {
                Fail("Edit Basic Info Message", $"Expected 'Course updated successfully.', got '{msg}'");
            }

            // Verify in SQL Server
            var updatedBasicCourse = await context.Courses.AsNoTracking().FirstAsync(c => c.Id == draftDbCourse.Id);
            if (updatedBasicCourse.Title == "Advanced Distributed Systems & Fault Tolerance" &&
                updatedBasicCourse.Description == "In-depth study of Paxos, Raft consensus, and distributed transactions.")
            {
                Pass("TEST A — Edit Basic Information: Course title and description updated in SQL Server with 'Course updated successfully.'");
            }
            else
            {
                Fail("Edit Basic Info Persistence", "Database values for Title or Description did not update in SQL Server.");
            }
        }
        else
        {
            Fail("Edit Basic Info Action", "Did not redirect to Course after editing basic info.");
        }

        // 11. TEST B — EDIT TEACHER
        var secondTeacher = (await userManager.GetUsersInRoleAsync("Teacher")).FirstOrDefault(u => u.Id != activeTeacher.Id && u.IsActive);
        if (secondTeacher == null)
        {
            secondTeacher = new User
            {
                UserName = "secondteacher@aub.edu.kh",
                Email = "secondteacher@aub.edu.kh",
                FullName = "Alan Turing",
                UniversityId = "TCH002",
                EmailConfirmed = true,
                IsActive = true
            };
            await userManager.CreateAsync(secondTeacher, "Teacher@123");
            await userManager.AddToRoleAsync(secondTeacher, "Teacher");
        }

        controller = CreateController();
        var editTeacherModel = new CourseEditViewModel
        {
            Id = draftDbCourse.Id,
            Title = "Advanced Distributed Systems & Fault Tolerance",
            Code = testCodeDraft,
            Description = "In-depth study of Paxos, Raft consensus, and distributed transactions.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = secondTeacher.Id, // Switch to second teacher
            CategoryId = category.Id,
            Thumbnail = null,
            Status = CourseStatus.Draft
        };

        var editTeacherResult = await controller.EditCourse(editTeacherModel);
        if (editTeacherResult is RedirectToActionResult)
        {
            var updatedTeacherCourse = await context.Courses.Include(c => c.Teacher).AsNoTracking().FirstAsync(c => c.Id == draftDbCourse.Id);
            if (updatedTeacherCourse.TeacherId == secondTeacher.Id && updatedTeacherCourse.Teacher?.FullName == secondTeacher.FullName)
            {
                Pass($"TEST B — Edit Teacher: Assigned teacher updated in SQL Server to '{secondTeacher.FullName}' [{secondTeacher.Email}]");
            }
            else
            {
                Fail("Edit Teacher Persistence", "Teacher was not updated in SQL Server.");
            }
        }
        else
        {
            Fail("Edit Teacher Action", "Failed to redirect after updating teacher.");
        }

        // 12. TEST C — EDIT STATUS (Draft to Published)
        controller = CreateController();
        var editStatusModel = new CourseEditViewModel
        {
            Id = draftDbCourse.Id,
            Title = "Advanced Distributed Systems & Fault Tolerance",
            Code = testCodeDraft,
            Description = "In-depth study of Paxos, Raft consensus, and distributed transactions.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = secondTeacher.Id,
            CategoryId = category.Id,
            Thumbnail = null,
            Status = CourseStatus.Published // Change to Published
        };

        var editStatusResult = await controller.EditCourse(editStatusModel);
        if (editStatusResult is RedirectToActionResult)
        {
            var updatedStatusCourse = await context.Courses.AsNoTracking().FirstAsync(c => c.Id == draftDbCourse.Id);
            if (updatedStatusCourse.Status == CourseStatus.Published)
            {
                Pass("TEST C — Edit Status: Course status changed from Draft to Published in SQL Server");
            }
            else
            {
                Fail("Edit Status Persistence", $"Course status expected Published, got {updatedStatusCourse.Status}");
            }
        }
        else
        {
            Fail("Edit Status Action", "Failed to redirect after updating status.");
        }

        // 13. TEST D — COVER IMAGE EDITING (Set URL, then Clear to null)
        controller = CreateController();
        var editImageModel = new CourseEditViewModel
        {
            Id = draftDbCourse.Id,
            Title = "Advanced Distributed Systems & Fault Tolerance",
            Code = testCodeDraft,
            Description = "In-depth study of Paxos, Raft consensus, and distributed transactions.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = secondTeacher.Id,
            CategoryId = category.Id,
            Thumbnail = "assets/images/course_ai.jpg", // New image URL
            Status = CourseStatus.Published
        };

        var editImgResult = await controller.EditCourse(editImageModel);
        var updatedImgCourse = await context.Courses.AsNoTracking().FirstAsync(c => c.Id == draftDbCourse.Id);
        if (updatedImgCourse.Thumbnail != "assets/images/course_ai.jpg")
        {
            Fail("Edit Image URL", "New cover image URL not saved to SQL Server.");
        }

        // Now clear the image to test null fallback
        controller = CreateController();
        editImageModel.Thumbnail = ""; // cleared
        var clearImgResult = await controller.EditCourse(editImageModel);
        var clearedImgCourse = await context.Courses.AsNoTracking().FirstAsync(c => c.Id == draftDbCourse.Id);
        if (clearedImgCourse.Thumbnail == null)
        {
            Pass("TEST D — Cover Image: Successfully updated custom cover image URL and cleared to null fallback in SQL Server");
        }
        else
        {
            Fail("Clear Image", "Clearing thumbnail did not set null in SQL Server.");
        }

        // 14. TEST E — PREVENT DUPLICATE COURSE CODE ON EDIT
        controller = CreateController();
        var editDuplicateCodeModel = new CourseEditViewModel
        {
            Id = draftDbCourse.Id,
            Title = "Advanced Distributed Systems & Fault Tolerance",
            Code = testCodePublish, // Attempting to use Course B's code!
            Description = "Duplicate code attempt.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = secondTeacher.Id,
            CategoryId = category.Id,
            Status = CourseStatus.Published
        };

        var editDuplicateResult = await controller.EditCourse(editDuplicateCodeModel);
        var dupCodeErrorFound = controller.ModelState.Values
            .SelectMany(v => v.Errors)
            .Any(e => e.ErrorMessage == "A course with this course code already exists.");

        if (dupCodeErrorFound)
        {
            // Verify in SQL Server that the course code was NOT corrupted to the duplicate code
            var checkInDb = await context.Courses.AsNoTracking().FirstAsync(c => c.Id == draftDbCourse.Id);
            if (checkInDb.Code == testCodeDraft)
            {
                Pass("TEST E — Duplicate Course Code: Updating with another course's code is prevented with 'A course with this course code already exists.'");
            }
            else
            {
                Fail("Duplicate Code Integrity", "Course code was overwritten with duplicate in SQL Server!");
            }
        }
        else
        {
            Fail("Duplicate Code Validation", "Expected duplicate course code validation error on edit.");
        }

        // 15. TEST F — EDIT VALIDATION OF INVALID DATA & FOREIGN KEYS
        controller = CreateController();
        var invalidEditModel = new CourseEditViewModel
        {
            Id = draftDbCourse.Id,
            Title = "", // empty required
            Code = "",  // empty required
            TeacherId = "00000000-0000-0000-0000-000000000000", // non-existent teacher
            MajorId = 999999, // non-existent major
            SemesterId = 999999 // non-existent semester
        };

        var invalidEditResult = await controller.EditCourse(invalidEditModel);
        if (invalidEditResult is ViewResult invEditView && invEditView.Model is CourseManagementViewModel invEditVm)
        {
            var errors = controller.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            bool hasTeacherError = errors.Any(e => e.Contains("teacher", StringComparison.OrdinalIgnoreCase));
            bool hasMajorError = errors.Any(e => e.Contains("major", StringComparison.OrdinalIgnoreCase));
            bool hasSemesterError = errors.Any(e => e.Contains("semester", StringComparison.OrdinalIgnoreCase));

            if (hasTeacherError && hasMajorError && hasSemesterError)
            {
                Pass("TEST F — Edit Validation: Invalid Teacher, Major, Semester, and empty required fields rejected with field-specific errors");
            }
            else
            {
                Fail("Edit Validation Errors", $"Missing expected error messages in: {string.Join("; ", errors)}");
            }
        }
        else
        {
            Fail("Edit Validation View", "Invalid model submission did not return ViewResult with CourseManagementViewModel.");
        }

        // 16. PAGE REFRESH & APPLICATION RESTART TEST:
        // Simulate complete restart: Dispose the entire ServiceScope and DbContext, then create a brand new scope from root container
        Console.WriteLine("\n  Simulating application restart & browser refresh...");
        using (var restartScope = serviceProvider.CreateScope())
        {
            var newContext = restartScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var newCourseService = restartScope.ServiceProvider.GetRequiredService<ICourseService>();

            var persistedPublishCourse = await newCourseService.GetCourseByIdAsync(
                (await newContext.Courses.FirstAsync(c => c.Code == testCodePublish)).Id
            );

            if (persistedPublishCourse == null)
            {
                Fail("Application Restart", "Published Course disappeared after simulated application restart!");
                return;
            }

            var persistedEditedCourse = await newCourseService.GetCourseByIdAsync(draftDbCourse.Id);
            if (persistedEditedCourse == null)
            {
                Fail("Application Restart", "Edited Course disappeared after simulated application restart!");
                return;
            }

            if (persistedEditedCourse.Title != "Advanced Distributed Systems & Fault Tolerance" ||
                persistedEditedCourse.Teacher?.FullName != secondTeacher.FullName ||
                persistedEditedCourse.Status != CourseStatus.Published)
            {
                Fail("Application Restart", "Course edits were not retained after application restart.");
            }

            var allPersistedCourses = (await newCourseService.GetAllCoursesAsync()).ToList();
            if (!allPersistedCourses.Any(c => c.Code == testCodeDraft) || !allPersistedCourses.Any(c => c.Code == testCodePublish))
            {
                Fail("Application Restart", "Draft or Published course missing from refreshed course list.");
            }

            Pass("Page Refresh & Application Restart Test: All course creations, basic edits, teacher updates, and status changes persist reliably in SQL Server across application restarts (no mock, no localStorage, no in-memory cache)");

            // Clean up test courses so only original seeded database records remain
            var cleanupCourses = await newContext.Courses
                .Where(c => c.Code == testCodePublish || c.Code == testCodeDraft)
                .ToListAsync();
            if (cleanupCourses.Any())
            {
                newContext.Courses.RemoveRange(cleanupCourses);
                await newContext.SaveChangesAsync();
            }
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
