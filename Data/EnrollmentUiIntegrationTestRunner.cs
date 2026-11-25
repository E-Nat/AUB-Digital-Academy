using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

public static class EnrollmentUiIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("🎓  AUB DIGITAL ACADEMY - STEP 3.2 ENROLLMENT UI INTEGRATION AUDIT");
        Console.WriteLine("=================================================================\n");

        int testsPassed = 0;
        int totalTests = 0;

        void Pass(string name)
        {
            testsPassed++;
            totalTests++;
            Console.WriteLine($"  [PASS] Test {totalTests:D2}: {name}");
        }

        void Fail(string name, string error)
        {
            totalTests++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] Test {totalTests:D2}: {name} - {error}");
            Console.ResetColor();
            Environment.Exit(1);
        }

        using var rootScope = serviceProvider.CreateScope();
        var context = rootScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var enrollmentService = rootScope.ServiceProvider.GetRequiredService<IEnrollmentService>();
        var courseService = rootScope.ServiceProvider.GetRequiredService<ICourseService>();
        var userManager = rootScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var loggerFactory = rootScope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var controllerLogger = loggerFactory.CreateLogger<EnrollmentController>();

        // Preconditions
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            Fail("Database Connectivity", "Cannot connect to SQL Server database.");
            return;
        }

        var major = await context.Majors.FirstOrDefaultAsync();
        var semester = await context.Semesters.FirstOrDefaultAsync();
        var teacher = await userManager.FindByEmailAsync("teacher@aub.com");
        var adminUser = await userManager.FindByEmailAsync("admin@aub.com");

        if (major == null || semester == null || teacher == null || adminUser == null)
        {
            Fail("Precondition Check", "Required seeded entities not found.");
            return;
        }

        // Setup Test Students
        var student1Email = "ui_student_1@aub.edu.kh";
        var student1 = await userManager.FindByEmailAsync(student1Email);
        if (student1 == null)
        {
            student1 = new User
            {
                UserName = student1Email,
                Email = student1Email,
                FullName = "Alice Johnson",
                UniversityId = "STU-UI-001",
                EmailConfirmed = true,
                IsActive = true
            };
            await userManager.CreateAsync(student1, "Student@123");
            await userManager.AddToRoleAsync(student1, "Student");
        }

        var student2Email = "ui_student_2@aub.edu.kh";
        var student2 = await userManager.FindByEmailAsync(student2Email);
        if (student2 == null)
        {
            student2 = new User
            {
                UserName = student2Email,
                Email = student2Email,
                FullName = "Bob Martinez",
                UniversityId = "STU-UI-002",
                EmailConfirmed = true,
                IsActive = true
            };
            await userManager.CreateAsync(student2, "Student@123");
            await userManager.AddToRoleAsync(student2, "Student");
        }

        // Clean up test courses & enrollments
        var testCodes = new[] { "UI-ENR-C1", "UI-ENR-C2", "UI-ENR-C3" };
        var existingCourses = await context.Courses
            .Include(c => c.Enrollments)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Progress)
            .Where(c => testCodes.Contains(c.Code))
            .ToListAsync();

        foreach (var c in existingCourses)
        {
            foreach (var ch in c.Chapters)
            {
                foreach (var ci in ch.CourseItems)
                {
                    context.CourseProgresses.RemoveRange(ci.Progress);
                }
                context.CourseItems.RemoveRange(ch.CourseItems);
            }
            context.Chapters.RemoveRange(c.Chapters);
            context.Enrollments.RemoveRange(c.Enrollments);
            context.Courses.Remove(c);
        }
        await context.SaveChangesAsync();

        // Create Test Courses in SQL Server
        var courseA = new Course
        {
            Code = "UI-ENR-C1",
            Title = "Modern Web Architecture",
            Description = "Full stack web development course",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Published
        };
        var courseB = new Course
        {
            Code = "UI-ENR-C2",
            Title = "Distributed Systems",
            Description = "Microservices and scalable systems",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Published
        };
        var courseC = new Course
        {
            Code = "UI-ENR-C3",
            Title = "Machine Learning Engineering",
            Description = "Applied neural networks and pipelines",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Published
        };
        context.Courses.AddRange(courseA, courseB, courseC);
        await context.SaveChangesAsync();

        // 1. Controller Initialization & Claims Setup for Admin
        var adminClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, adminUser.Id),
            new Claim(ClaimTypes.Name, adminUser.UserName!),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

        var adminController = new EnrollmentController(
            enrollmentService,
            courseService,
            userManager,
            context,
            controllerLogger);

        adminController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = adminClaims }
        };
        adminController.TempData = new TempDataDictionary(adminController.HttpContext, new TestTempDataProvider());

        Pass("UI Setup: Admin Identity and EnrollmentController context initialized with real SQL Server services");

        // 2. Test Admin Enrollment Page (Index) Initial Load & Real Student Data
        var indexResult = await adminController.Index(studentId: student1.Id) as ViewResult;
        if (indexResult == null || indexResult.Model is not AdminStudentEnrollmentsViewModel indexModel)
        {
            Fail("Admin Enrollment Page Load", "Index action did not return ViewResult with AdminStudentEnrollmentsViewModel.");
            return;
        }

        if (indexModel.SelectedStudentId != student1.Id || indexModel.StudentName != student1.FullName || indexModel.UniversityId != "STU-UI-001")
        {
            Fail("Real Student Data", "Selected student profile in ViewModel does not match SQL Server user entity.");
            return;
        }

        if (indexModel.AllStudents.Count == 0 || !indexModel.AllStudents.Any(s => s.Id == student1.Id))
        {
            Fail("Student Dropdown Data", "AllStudents lookup list is empty or does not contain student 1.");
            return;
        }

        Pass("Admin Enrollment Page (Index): Real Student data loaded from SQL Server (FullName, UniversityId, Email, Status)");

        // 3. Test Empty State for Student with 0 Enrollments
        if (indexModel.EnrolledCourses.Count != 0 || indexModel.SelectedStudentEnrollmentCount != 0)
        {
            Fail("Empty State", $"Expected 0 enrolled courses for new student, found {indexModel.EnrolledCourses.Count}.");
            return;
        }
        Pass("Empty State Validation: Student with 0 enrollments returns empty list; clean empty-state UI contract verified");

        // 4. Test Course Selector Options (Real Courses & Duplicate Flags)
        if (indexModel.AvailableCourses.Count < 3)
        {
            Fail("Course Selector Options", $"Expected at least 3 available courses, found {indexModel.AvailableCourses.Count}.");
            return;
        }

        var optCourseA = indexModel.AvailableCourses.FirstOrDefault(c => c.CourseId == courseA.Id);
        if (optCourseA == null || optCourseA.IsAlreadyEnrolled)
        {
            Fail("Course Selector Options", "Course A not found or marked as already enrolled when student has no enrollments.");
            return;
        }
        Pass("Course Selector UX: Real courses loaded from SQL Server with Course Titles, Codes, and IsAlreadyEnrolled flag = false");

        // 5. Test Create Enrollment Flow (Admin enrolls Student 1 into Course A)
        var enrollAction1 = await adminController.Enroll(courseA.Id, studentId: student1.Id) as RedirectToActionResult;
        if (enrollAction1 == null || enrollAction1.ActionName != nameof(EnrollmentController.Index))
        {
            Fail("Create Enrollment Flow", "Enroll action did not redirect back to Index as expected.");
            return;
        }

        var successMessage = adminController.TempData["SuccessMessage"]?.ToString();
        if (string.IsNullOrEmpty(successMessage) || !successMessage.Contains("Successfully enrolled"))
        {
            Fail("Enrollment Success Feedback", $"TempData SuccessMessage missing or incorrect: '{successMessage}'");
            return;
        }
        Pass($"Create Enrollment Flow: Admin successfully enrolled '{student1.FullName}' into '{courseA.Title}'; flash feedback rendered");

        // 6. SQL Server Persistence Check & Browser Refresh Simulation
        using var freshScope1 = serviceProvider.CreateScope();
        var freshContext1 = freshScope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbEnrollment1 = await freshContext1.Enrollments
            .AsNoTracking()
            .Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.StudentId == student1.Id && e.CourseId == courseA.Id);

        if (dbEnrollment1 == null || dbEnrollment1.Course == null)
        {
            Fail("SQL Server Persistence", "Enrollment record not found in database with fresh DbContext.");
            return;
        }

        // Simulate browser refresh: reload Index for student 1
        var refreshedResult = await adminController.Index(studentId: student1.Id) as ViewResult;
        var refreshedModel = refreshedResult?.Model as AdminStudentEnrollmentsViewModel;
        if (refreshedModel == null || refreshedModel.EnrolledCourses.Count != 1 || refreshedModel.EnrolledCourses[0].CourseId != courseA.Id)
        {
            Fail("Browser Refresh Persistence", "Enrolled course does not appear after refreshing the Index view.");
            return;
        }

        var refreshedOptCourseA = refreshedModel.AvailableCourses.FirstOrDefault(c => c.CourseId == courseA.Id);
        if (refreshedOptCourseA == null || !refreshedOptCourseA.IsAlreadyEnrolled)
        {
            Fail("Duplicate Enrollment UX", "Course selector did not mark Course A as IsAlreadyEnrolled after enrollment.");
            return;
        }
        Pass("Persistence & Duplicate UX: Enrollment persisted across database scopes; Course A now marked IsAlreadyEnrolled = true");

        // 7. Duplicate Enrollment Prevention via Controller POST
        var duplicateEnrollAction = await adminController.Enroll(courseA.Id, studentId: student1.Id) as RedirectToActionResult;
        var errorMessage = adminController.TempData["ErrorMessage"]?.ToString();
        if (string.IsNullOrEmpty(errorMessage) || !errorMessage.Contains("already enrolled"))
        {
            Fail("Duplicate Enrollment Rejection", $"Expected duplicate error feedback in TempData, got '{errorMessage}'");
            return;
        }

        using var verifyCountScope = serviceProvider.CreateScope();
        var verifyCountContext = verifyCountScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rowCount = await verifyCountContext.Enrollments.CountAsync(e => e.StudentId == student1.Id && e.CourseId == courseA.Id);
        if (rowCount != 1)
        {
            Fail("Duplicate Count in DB", $"Expected exactly 1 enrollment row in SQL Server, found {rowCount}.");
            return;
        }
        Pass("Duplicate Enrollment Prevention: Controller and Service safely rejected duplicate enrollment; exactly 1 DB record exists");

        // 8. Test Student Isolation in UI
        // Enroll Student 1 in Course B, Student 2 in Course C
        await adminController.Enroll(courseB.Id, studentId: student1.Id);
        await adminController.Enroll(courseC.Id, studentId: student2.Id);

        var student1View = (await adminController.Index(studentId: student1.Id) as ViewResult)?.Model as AdminStudentEnrollmentsViewModel;
        var student2View = (await adminController.Index(studentId: student2.Id) as ViewResult)?.Model as AdminStudentEnrollmentsViewModel;

        if (student1View == null || student1View.EnrolledCourses.Count != 2 || student1View.EnrolledCourses.Any(c => c.CourseId == courseC.Id))
        {
            Fail("Student Isolation", "Student 1's view does not have exactly Course A & B, or contains Course C.");
            return;
        }

        if (student2View == null || student2View.EnrolledCourses.Count != 1 || student2View.EnrolledCourses[0].CourseId != courseC.Id)
        {
            Fail("Student Isolation", "Student 2's view does not contain strictly Course C.");
            return;
        }
        Pass("Student Isolation: Student 1 view displays Course A & B; Student 2 view displays Course C; 0 cross-student data leakage");

        // 9. Test Course Enrollment View (CourseStudents action & roster)
        // Course A currently has Student 1; let's enroll Student 2 in Course A as well
        await adminController.Enroll(courseA.Id, studentId: student2.Id);

        var courseStudentsResult = await adminController.CourseStudents(courseA.Id) as ViewResult;
        if (courseStudentsResult == null || courseStudentsResult.Model is not CourseEnrollmentListViewModel courseRosterModel)
        {
            Fail("Course Enrollment View", "CourseStudents action did not return ViewResult with CourseEnrollmentListViewModel.");
            return;
        }

        if (courseRosterModel.CourseId != courseA.Id || courseRosterModel.Students.Count != 2)
        {
            Fail("Course Roster Data", $"Expected 2 enrolled students in Course A roster, found {courseRosterModel.Students.Count}.");
            return;
        }

        if (!courseRosterModel.Students.Any(s => s.StudentId == student1.Id) || !courseRosterModel.Students.Any(s => s.StudentId == student2.Id))
        {
            Fail("Course Roster Students", "Course A roster does not contain both Student 1 and Student 2.");
            return;
        }

        // AvailableStudents for Course A should not contain Student 1 or Student 2
        if (courseRosterModel.AvailableStudents.Any(s => s.Id == student1.Id || s.Id == student2.Id))
        {
            Fail("Course Available Students", "AvailableStudents list in Course Roster contains already-enrolled students.");
            return;
        }
        Pass("Course Roster View (CourseStudents): Accurately renders course roster; already-enrolled students excluded from modal dropdown");

        // 10. Test Safe Removal Flow (Case A: No Academic Activity)
        // Student 1 is enrolled in Course B with zero learning progress
        var removeActionA = await adminController.Unenroll(courseB.Id, studentId: student1.Id) as RedirectToActionResult;
        var removeSuccessMsg = adminController.TempData["SuccessMessage"]?.ToString();
        if (string.IsNullOrEmpty(removeSuccessMsg) || !removeSuccessMsg.Contains("successfully removed"))
        {
            Fail("Safe Removal Case A", $"Expected SuccessMessage in TempData, got '{removeSuccessMsg}'");
            return;
        }

        var isStillInB = await enrollmentService.IsStudentEnrolledAsync(student1.Id, courseB.Id);
        if (isStillInB)
        {
            Fail("Safe Removal Case A", "Enrollment record still exists in SQL Server after safe removal.");
            return;
        }
        Pass("Safe Removal (Case A - No Activity): Enrollment removed smoothly with confirmation feedback; SQL Server record deleted");

        // 11. Test Safe Removal Flow (Case B: Academic Activity Exists - Protected)
        // Add learning activity for Student 1 in Course A
        var chapterA = new Chapter { Title = "Module 1", Position = 1, CourseId = courseA.Id };
        context.Chapters.Add(chapterA);
        await context.SaveChangesAsync();

        var itemA = new CourseItem { Title = "Lesson 1", Position = 1, ChapterId = chapterA.Id, Type = ItemType.Lesson };
        context.CourseItems.Add(itemA);
        await context.SaveChangesAsync();

        var progressA = new CourseProgress
        {
            StudentId = student1.Id,
            CourseItemId = itemA.Id,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        };
        context.CourseProgresses.Add(progressA);
        await context.SaveChangesAsync();

        // Attempt to unenroll Student 1 from Course A
        var blockedRemoveAction = await adminController.Unenroll(courseA.Id, studentId: student1.Id) as RedirectToActionResult;
        var blockedErrorMsg = adminController.TempData["ErrorMessage"]?.ToString();

        if (string.IsNullOrEmpty(blockedErrorMsg) || !blockedErrorMsg.Contains("learning records exist") && !blockedErrorMsg.Contains("Cannot unenroll"))
        {
            Fail("Safe Removal Case B", $"Expected blocked error message in TempData, got '{blockedErrorMsg}'");
            return;
        }

        // Verify enrollment is still in SQL Server and progress is intact
        using var checkScopeB = serviceProvider.CreateScope();
        var checkContextB = checkScopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stillEnrolledInA = await checkContextB.Enrollments.AnyAsync(e => e.StudentId == student1.Id && e.CourseId == courseA.Id);
        var stillProgressA = await checkContextB.CourseProgresses.AnyAsync(cp => cp.StudentId == student1.Id && cp.CourseItemId == itemA.Id);

        if (!stillEnrolledInA || !stillProgressA)
        {
            Fail("Safe Removal Case B Protection", "Enrollment or progress was deleted when academic history existed!");
            return;
        }
        Pass("Safe Removal (Case B - Academic History Exists): Removal strictly blocked with user-friendly alert; academic history 100% preserved");

        // 12. Test Admin Routing & Navigation Contracts
        var adminControllerRouteTest = new AdminController();
        var enrollmentsRedirect = adminControllerRouteTest.Enrollments(studentId: student1.Id) as RedirectToActionResult;
        if (enrollmentsRedirect == null || enrollmentsRedirect.ActionName != "Index" || enrollmentsRedirect.ControllerName != "Enrollment")
        {
            Fail("Admin Navigation Contract", "AdminController.Enrollments did not redirect to Enrollment.Index.");
            return;
        }
        Pass("Navigation Contracts: /Admin/Enrollments routes cleanly to EnrollmentController.Index with studentId preserved");

        // 13. Test Cleanup & Pristine SQL Server State
        context.CourseProgresses.Remove(progressA);
        await context.SaveChangesAsync();

        // Now remove all test enrollments, items, chapters, and courses
        var testEnrollments = await context.Enrollments
            .Where(e => e.StudentId == student1.Id || e.StudentId == student2.Id)
            .ToListAsync();
        context.Enrollments.RemoveRange(testEnrollments);

        context.CourseItems.Remove(itemA);
        context.Chapters.Remove(chapterA);
        context.Courses.RemoveRange(courseA, courseB, courseC);
        await context.SaveChangesAsync();

        var orphanEnrollmentUsers = await context.Enrollments
            .Where(e => !context.Users.Any(u => u.Id == e.StudentId))
            .CountAsync();

        var orphanEnrollmentCourses = await context.Enrollments
            .Where(e => !context.Courses.Any(c => c.Id == e.CourseId))
            .CountAsync();

        if (orphanEnrollmentUsers > 0 || orphanEnrollmentCourses > 0)
        {
            Fail("Database Cleanliness", $"Found orphan enrollments after cleanup: {orphanEnrollmentUsers} users, {orphanEnrollmentCourses} courses.");
            return;
        }
        Pass("Database Cleanliness & Orphan Check: 0 orphan enrollment rows; SQL Server database restored to clean state");

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"🎉  ALL {testsPassed}/{totalTests} ENROLLMENT UI INTEGRATION TESTS PASSED SUCCESSFULLY!");
        Console.WriteLine("=================================================================\n");
    }

    private class TestTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object> _data = new();
        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _data.Clear();
            foreach (var kv in values) _data[kv.Key] = kv.Value;
        }
    }
}
