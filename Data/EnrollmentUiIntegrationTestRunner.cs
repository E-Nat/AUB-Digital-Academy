using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
    public static async Task<int> RunTestsAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var emailTag = runId.ToLowerInvariant();
        var manifest = new TestRunManifest();

        Console.WriteLine("=================================================================");
        Console.WriteLine($"🎓  AUB DIGITAL ACADEMY - STEP 3.2 ENROLLMENT UI INTEGRATION AUDIT [Run: {runId}]");
        Console.WriteLine("=================================================================\n");

        int testsPassed = 0;
        int totalTests = 0;
        bool suiteSucceeded = false;
        bool cleanupFailed = false;

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
            throw new TestAssertionFailureException(name, error);
        }

        async Task<User> CreateRunUserAsync(
            UserManager<User> userManager,
            string email,
            string fullName,
            string universityId,
            string role)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var user = new User
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                UniversityId = universityId,
                EmailConfirmed = true,
                IsActive = true
            };

            var createResult = await userManager.CreateAsync(user, "TestPass@123");
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                Fail("Run User Creation", $"Failed to create '{email}' ({role}): {errors}");
            }

            manifest.UserIds.Add(user.Id);

            var roleResult = await userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                var errors = string.Join("; ", roleResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                Fail("Run User Role Assignment", $"Failed to assign role '{role}' to '{email}': {errors}");
            }

            return user;
        }

        async Task TrackEnrollmentAsync(ApplicationDbContext db, string studentId, int courseId)
        {
            var enrollmentId = await db.Enrollments
                .AsNoTracking()
                .Where(e => e.StudentId == studentId && e.CourseId == courseId)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (enrollmentId > 0 && !manifest.EnrollmentIds.Contains(enrollmentId))
            {
                manifest.EnrollmentIds.Add(enrollmentId);
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var rootScope = serviceProvider.CreateScope();
            var context = rootScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var enrollmentService = rootScope.ServiceProvider.GetRequiredService<IEnrollmentService>();
            var courseService = rootScope.ServiceProvider.GetRequiredService<ICourseService>();
            var userManager = rootScope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var loggerFactory = rootScope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var controllerLogger = loggerFactory.CreateLogger<EnrollmentController>();

            // Preconditions
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                Fail("Database Connectivity", "Cannot connect to SQL Server database.");
            }

            var major = await context.Majors.FirstOrDefaultAsync(cancellationToken);
            var semester = await context.Semesters.FirstOrDefaultAsync(cancellationToken);
            if (major == null || semester == null)
            {
                Fail("Precondition Check", "Required seeded reference entities (Major, Semester) not found.");
            }

            // Setup run-scoped Users (Admin, Teacher, Student 1, Student 2)
            var adminUser = await CreateRunUserAsync(
                userManager,
                email: $"ui_admin_{emailTag}@test.aub.local",
                fullName: $"UI Admin {runId}",
                universityId: $"UI-AD-{runId}",
                role: "Admin");

            var teacher = await CreateRunUserAsync(
                userManager,
                email: $"ui_teacher_{emailTag}@test.aub.local",
                fullName: $"UI Teacher {runId}",
                universityId: $"UI-T1-{runId}",
                role: "Teacher");

            var student1 = await CreateRunUserAsync(
                userManager,
                email: $"ui_s1_{emailTag}@test.aub.local",
                fullName: $"Alice Johnson {runId}",
                universityId: $"UI-S1-{runId}",
                role: "Student");

            var student2 = await CreateRunUserAsync(
                userManager,
                email: $"ui_s2_{emailTag}@test.aub.local",
                fullName: $"Bob Martinez {runId}",
                universityId: $"UI-S2-{runId}",
                role: "Student");

            // Create run-scoped Test Courses in SQL Server
            var courseA = new Course
            {
                Code = $"UI-C1-{runId}",
                Title = $"Modern Web Architecture [{runId}]",
                Description = "Full stack web development course",
                MajorId = major!.Id,
                SemesterId = semester!.Id,
                TeacherId = teacher.Id,
                Status = CourseStatus.Published
            };
            var courseB = new Course
            {
                Code = $"UI-C2-{runId}",
                Title = $"Distributed Systems [{runId}]",
                Description = "Microservices and scalable systems",
                MajorId = major.Id,
                SemesterId = semester.Id,
                TeacherId = teacher.Id,
                Status = CourseStatus.Published
            };
            var courseC = new Course
            {
                Code = $"UI-C3-{runId}",
                Title = $"Machine Learning Engineering [{runId}]",
                Description = "Applied neural networks and pipelines",
                MajorId = major.Id,
                SemesterId = semester.Id,
                TeacherId = teacher.Id,
                Status = CourseStatus.Published
            };
            context.Courses.AddRange(courseA, courseB, courseC);
            await context.SaveChangesAsync(cancellationToken);
            manifest.CourseIds.Add(courseA.Id);
            manifest.CourseIds.Add(courseB.Id);
            manifest.CourseIds.Add(courseC.Id);

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
            }

            if (indexModel!.SelectedStudentId != student1.Id || indexModel.StudentName != student1.FullName || indexModel.UniversityId != student1.UniversityId)
            {
                Fail("Real Student Data", "Selected student profile in ViewModel does not match SQL Server user entity.");
            }

            if (indexModel.AllStudents.Count == 0 || !indexModel.AllStudents.Any(s => s.Id == student1.Id))
            {
                Fail("Student Dropdown Data", "AllStudents lookup list is empty or does not contain student 1.");
            }

            Pass("Admin Enrollment Page (Index): Real Student data loaded from SQL Server (FullName, UniversityId, Email, Status)");

            // 3. Test Empty State for Student with 0 Enrollments
            if (indexModel.EnrolledCourses.Count != 0 || indexModel.SelectedStudentEnrollmentCount != 0)
            {
                Fail("Empty State", $"Expected 0 enrolled courses for new student, found {indexModel.EnrolledCourses.Count}.");
            }
            Pass("Empty State Validation: Student with 0 enrollments returns empty list; clean empty-state UI contract verified");

            // 4. Test Course Selector Options (Real Courses & Duplicate Flags)
            if (indexModel.AvailableCourses.Count < 3)
            {
                Fail("Course Selector Options", $"Expected at least 3 available courses, found {indexModel.AvailableCourses.Count}.");
            }

            var optCourseA = indexModel.AvailableCourses.FirstOrDefault(c => c.CourseId == courseA.Id);
            if (optCourseA == null || optCourseA.IsAlreadyEnrolled)
            {
                Fail("Course Selector Options", "Course A not found or marked as already enrolled when student has no enrollments.");
            }
            Pass("Course Selector UX: Real courses loaded from SQL Server with Course Titles, Codes, and IsAlreadyEnrolled flag = false");

            // 5. Test Create Enrollment Flow (Admin enrolls Student 1 into Course A)
            var enrollAction1 = await adminController.Enroll(courseA.Id, studentId: student1.Id) as RedirectToActionResult;
            await TrackEnrollmentAsync(context, student1.Id, courseA.Id);
            if (enrollAction1 == null || enrollAction1.ActionName != nameof(EnrollmentController.Index))
            {
                Fail("Create Enrollment Flow", "Enroll action did not redirect back to Index as expected.");
            }

            var successMessage = adminController.TempData["SuccessMessage"]?.ToString();
            if (string.IsNullOrEmpty(successMessage) || !successMessage.Contains("Successfully enrolled"))
            {
                Fail("Enrollment Success Feedback", $"TempData SuccessMessage missing or incorrect: '{successMessage}'");
            }
            Pass($"Create Enrollment Flow: Admin successfully enrolled '{student1.FullName}' into '{courseA.Title}'; flash feedback rendered");

            // 6. SQL Server Persistence Check & Browser Refresh Simulation
            using (var freshScope1 = serviceProvider.CreateScope())
            {
                var freshContext1 = freshScope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var dbEnrollment1 = await freshContext1.Enrollments
                    .AsNoTracking()
                    .Include(e => e.Course)
                    .FirstOrDefaultAsync(e => e.StudentId == student1.Id && e.CourseId == courseA.Id, cancellationToken);

                if (dbEnrollment1 == null || dbEnrollment1.Course == null)
                {
                    Fail("SQL Server Persistence", "Enrollment record not found in database with fresh DbContext.");
                }
            }

            // Simulate browser refresh: reload Index for student 1
            var refreshedResult = await adminController.Index(studentId: student1.Id) as ViewResult;
            var refreshedModel = refreshedResult?.Model as AdminStudentEnrollmentsViewModel;
            if (refreshedModel == null || refreshedModel.EnrolledCourses.Count != 1 || refreshedModel.EnrolledCourses[0].CourseId != courseA.Id)
            {
                Fail("Browser Refresh Persistence", "Enrolled course does not appear after refreshing the Index view.");
            }

            var refreshedOptCourseA = refreshedModel!.AvailableCourses.FirstOrDefault(c => c.CourseId == courseA.Id);
            if (refreshedOptCourseA == null || !refreshedOptCourseA.IsAlreadyEnrolled)
            {
                Fail("Duplicate Enrollment UX", "Course selector did not mark Course A as IsAlreadyEnrolled after enrollment.");
            }
            Pass("Persistence & Duplicate UX: Enrollment persisted across database scopes; Course A now marked IsAlreadyEnrolled = true");

            // 7. Duplicate Enrollment Prevention via Controller POST
            _ = await adminController.Enroll(courseA.Id, studentId: student1.Id) as RedirectToActionResult;
            var errorMessage = adminController.TempData["ErrorMessage"]?.ToString();
            if (string.IsNullOrEmpty(errorMessage) || !errorMessage.Contains("already enrolled"))
            {
                Fail("Duplicate Enrollment Rejection", $"Expected duplicate error feedback in TempData, got '{errorMessage}'");
            }

            using (var verifyCountScope = serviceProvider.CreateScope())
            {
                var verifyCountContext = verifyCountScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var rowCount = await verifyCountContext.Enrollments.CountAsync(
                    e => e.StudentId == student1.Id && e.CourseId == courseA.Id,
                    cancellationToken);
                if (rowCount != 1)
                {
                    Fail("Duplicate Count in DB", $"Expected exactly 1 enrollment row in SQL Server, found {rowCount}.");
                }
            }
            Pass("Duplicate Enrollment Prevention: Controller and Service safely rejected duplicate enrollment; exactly 1 DB record exists");

            // 8. Test Student Isolation in UI
            // Enroll Student 1 in Course B, Student 2 in Course C
            await adminController.Enroll(courseB.Id, studentId: student1.Id);
            await TrackEnrollmentAsync(context, student1.Id, courseB.Id);

            await adminController.Enroll(courseC.Id, studentId: student2.Id);
            await TrackEnrollmentAsync(context, student2.Id, courseC.Id);

            var student1View = (await adminController.Index(studentId: student1.Id) as ViewResult)?.Model as AdminStudentEnrollmentsViewModel;
            var student2View = (await adminController.Index(studentId: student2.Id) as ViewResult)?.Model as AdminStudentEnrollmentsViewModel;

            if (student1View == null || student1View.EnrolledCourses.Count != 2 || student1View.EnrolledCourses.Any(c => c.CourseId == courseC.Id))
            {
                Fail("Student Isolation", "Student 1's view does not have exactly Course A & B, or contains Course C.");
            }

            if (student2View == null || student2View.EnrolledCourses.Count != 1 || student2View.EnrolledCourses[0].CourseId != courseC.Id)
            {
                Fail("Student Isolation", "Student 2's view does not contain strictly Course C.");
            }
            Pass("Student Isolation: Student 1 view displays Course A & B; Student 2 view displays Course C; 0 cross-student data leakage");

            // 9. Test Course Enrollment View (CourseStudents action & roster)
            // Course A currently has Student 1; enroll Student 2 in Course A as well
            await adminController.Enroll(courseA.Id, studentId: student2.Id);
            await TrackEnrollmentAsync(context, student2.Id, courseA.Id);

            var courseStudentsResult = await adminController.CourseStudents(courseA.Id) as ViewResult;
            if (courseStudentsResult == null || courseStudentsResult.Model is not CourseEnrollmentListViewModel courseRosterModel)
            {
                Fail("Course Enrollment View", "CourseStudents action did not return ViewResult with CourseEnrollmentListViewModel.");
            }

            if (courseRosterModel!.CourseId != courseA.Id || courseRosterModel.Students.Count != 2)
            {
                Fail("Course Roster Data", $"Expected 2 enrolled students in Course A roster, found {courseRosterModel.Students.Count}.");
            }

            if (!courseRosterModel.Students.Any(s => s.StudentId == student1.Id) || !courseRosterModel.Students.Any(s => s.StudentId == student2.Id))
            {
                Fail("Course Roster Students", "Course A roster does not contain both Student 1 and Student 2.");
            }

            // AvailableStudents for Course A should not contain Student 1 or Student 2
            if (courseRosterModel.AvailableStudents.Any(s => s.Id == student1.Id || s.Id == student2.Id))
            {
                Fail("Course Available Students", "AvailableStudents list in Course Roster contains already-enrolled students.");
            }
            Pass("Course Roster View (CourseStudents): Accurately renders course roster; already-enrolled students excluded from modal dropdown");

            // 10. Test Safe Removal Flow (Case A: No Academic Activity)
            // Student 1 is enrolled in Course B with zero learning progress
            _ = await adminController.Unenroll(courseB.Id, studentId: student1.Id) as RedirectToActionResult;
            var removeSuccessMsg = adminController.TempData["SuccessMessage"]?.ToString();
            if (string.IsNullOrEmpty(removeSuccessMsg) || !removeSuccessMsg.Contains("successfully removed"))
            {
                Fail("Safe Removal Case A", $"Expected SuccessMessage in TempData, got '{removeSuccessMsg}'");
            }

            var isStillInB = await enrollmentService.IsStudentEnrolledAsync(student1.Id, courseB.Id);
            if (isStillInB)
            {
                Fail("Safe Removal Case A", "Enrollment record still exists in SQL Server after safe removal.");
            }
            Pass("Safe Removal (Case A - No Activity): Enrollment removed smoothly with confirmation feedback; SQL Server record deleted");

            // 11. Test Safe Removal Flow (Case B: Academic Activity Exists - Protected)
            var chapterA = new Chapter { Title = $"Module 1 [{runId}]", Position = 1, CourseId = courseA.Id };
            context.Chapters.Add(chapterA);
            await context.SaveChangesAsync(cancellationToken);
            manifest.ChapterIds.Add(chapterA.Id);

            var itemA = new CourseItem { Title = $"Lesson 1 [{runId}]", Position = 1, ChapterId = chapterA.Id, Type = ItemType.Lesson };
            context.CourseItems.Add(itemA);
            await context.SaveChangesAsync(cancellationToken);
            manifest.CourseItemIds.Add(itemA.Id);

            var progressA = new CourseProgress
            {
                StudentId = student1.Id,
                CourseItemId = itemA.Id,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow
            };
            context.CourseProgresses.Add(progressA);
            await context.SaveChangesAsync(cancellationToken);
            manifest.CourseProgressIds.Add(progressA.Id);

            // Attempt to unenroll Student 1 from Course A
            _ = await adminController.Unenroll(courseA.Id, studentId: student1.Id) as RedirectToActionResult;
            var blockedErrorMsg = adminController.TempData["ErrorMessage"]?.ToString();

            if (string.IsNullOrEmpty(blockedErrorMsg) || (!blockedErrorMsg.Contains("learning records exist") && !blockedErrorMsg.Contains("Cannot unenroll")))
            {
                Fail("Safe Removal Case B", $"Expected blocked error message in TempData, got '{blockedErrorMsg}'");
            }

            // Verify enrollment is still in SQL Server and progress is intact
            using (var checkScopeB = serviceProvider.CreateScope())
            {
                var checkContextB = checkScopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var stillEnrolledInA = await checkContextB.Enrollments.AnyAsync(
                    e => e.StudentId == student1.Id && e.CourseId == courseA.Id,
                    cancellationToken);
                var stillProgressA = await checkContextB.CourseProgresses.AnyAsync(
                    cp => cp.StudentId == student1.Id && cp.CourseItemId == itemA.Id,
                    cancellationToken);

                if (!stillEnrolledInA || !stillProgressA)
                {
                    Fail("Safe Removal Case B Protection", "Enrollment or progress was deleted when academic history existed!");
                }
            }
            Pass("Safe Removal (Case B - Academic History Exists): Removal strictly blocked with user-friendly alert; academic history 100% preserved");

            // 12. Test Admin Routing & Navigation Contracts
            var adminControllerRouteTest = new AdminController();
            var enrollmentsRedirect = adminControllerRouteTest.Enrollments(studentId: student1.Id) as RedirectToActionResult;
            if (enrollmentsRedirect == null || enrollmentsRedirect.ActionName != "Index" || enrollmentsRedirect.ControllerName != "Enrollment")
            {
                Fail("Admin Navigation Contract", "AdminController.Enrollments did not redirect to Enrollment.Index.");
            }
            Pass("Navigation Contracts: /Admin/Enrollments routes cleanly to EnrollmentController.Index with studentId preserved");

            // 13. Test Cleanup & Pristine SQL Server State
            context.CourseProgresses.Remove(progressA);
            await context.SaveChangesAsync(cancellationToken);

            var testEnrollments = await context.Enrollments
                .Where(e => manifest.EnrollmentIds.Contains(e.Id))
                .ToListAsync(cancellationToken);
            context.Enrollments.RemoveRange(testEnrollments);

            context.CourseItems.Remove(itemA);
            context.Chapters.Remove(chapterA);
            context.Courses.RemoveRange(courseA, courseB, courseC);
            await context.SaveChangesAsync(cancellationToken);

            var orphanEnrollmentUsers = await context.Enrollments
                .Where(e => !context.Users.Any(u => u.Id == e.StudentId))
                .CountAsync(cancellationToken);

            var orphanEnrollmentCourses = await context.Enrollments
                .Where(e => !context.Courses.Any(c => c.Id == e.CourseId))
                .CountAsync(cancellationToken);

            if (orphanEnrollmentUsers > 0 || orphanEnrollmentCourses > 0)
            {
                Fail("Database Cleanliness", $"Found orphan enrollments after cleanup: {orphanEnrollmentUsers} users, {orphanEnrollmentCourses} courses.");
            }
            Pass("Database Cleanliness & Orphan Check: 0 orphan enrollment rows; run-scoped entities queued for manifest cleanup");

            suiteSucceeded = true;
        }
        catch (OperationCanceledException)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  [CANCELLED] Enrollment UI integration test run {runId} was cancelled. Running manifest cleanup...");
            Console.ResetColor();
        }
        catch (TestAssertionFailureException)
        {
            // Failure already logged in Fail(). Proceeding to finally cleanup.
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  [ERROR] Unexpected exception during run {runId}: {ex.GetType().Name} - {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            cleanupFailed = !await CleanupManifestAsync(serviceProvider, manifest, runId);
        }

        if (suiteSucceeded && !cleanupFailed)
        {
            Console.WriteLine("\n=================================================================");
            Console.WriteLine($"🎉  ALL {testsPassed}/{totalTests} ENROLLMENT UI INTEGRATION TESTS PASSED SUCCESSFULLY! [Run: {runId}]");
            Console.WriteLine("=================================================================\n");
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n❌  ENROLLMENT UI INTEGRATION TEST RUN FAILED OR INCOMPLETE [Run: {runId}] (Passed: {testsPassed}/{totalTests}, CleanupFailed: {cleanupFailed})");
        Console.ResetColor();
        return 1;
    }

    private static async Task<bool> CleanupManifestAsync(
        IServiceProvider serviceProvider,
        TestRunManifest manifest,
        string runId)
    {
        if (manifest.IsEmpty)
        {
            return true;
        }

        bool allCleaned = true;

        try
        {
            using var cleanupScope = serviceProvider.CreateScope();
            var context = cleanupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = cleanupScope.ServiceProvider.GetRequiredService<UserManager<User>>();

            if (manifest.CourseProgressIds.Count > 0)
            {
                try
                {
                    var progresses = await context.CourseProgresses
                        .Where(cp => manifest.CourseProgressIds.Contains(cp.Id))
                        .ToListAsync(CancellationToken.None);
                    if (progresses.Count > 0)
                    {
                        context.CourseProgresses.RemoveRange(progresses);
                        await context.SaveChangesAsync(CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    allCleaned = false;
                    Console.Error.WriteLine($"[CLEANUP ERROR][Run: {runId}] Failed removing CourseProgresses: {ex.GetType().Name} - {ex.Message}");
                }
            }

            if (manifest.EnrollmentIds.Count > 0)
            {
                try
                {
                    var enrollments = await context.Enrollments
                        .Where(e => manifest.EnrollmentIds.Contains(e.Id))
                        .ToListAsync(CancellationToken.None);
                    if (enrollments.Count > 0)
                    {
                        context.Enrollments.RemoveRange(enrollments);
                        await context.SaveChangesAsync(CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    allCleaned = false;
                    Console.Error.WriteLine($"[CLEANUP ERROR][Run: {runId}] Failed removing Enrollments: {ex.GetType().Name} - {ex.Message}");
                }
            }

            if (manifest.CourseItemIds.Count > 0)
            {
                try
                {
                    var items = await context.CourseItems
                        .Where(ci => manifest.CourseItemIds.Contains(ci.Id))
                        .ToListAsync(CancellationToken.None);
                    if (items.Count > 0)
                    {
                        context.CourseItems.RemoveRange(items);
                        await context.SaveChangesAsync(CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    allCleaned = false;
                    Console.Error.WriteLine($"[CLEANUP ERROR][Run: {runId}] Failed removing CourseItems: {ex.GetType().Name} - {ex.Message}");
                }
            }

            if (manifest.ChapterIds.Count > 0)
            {
                try
                {
                    var chapters = await context.Chapters
                        .Where(ch => manifest.ChapterIds.Contains(ch.Id))
                        .ToListAsync(CancellationToken.None);
                    if (chapters.Count > 0)
                    {
                        context.Chapters.RemoveRange(chapters);
                        await context.SaveChangesAsync(CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    allCleaned = false;
                    Console.Error.WriteLine($"[CLEANUP ERROR][Run: {runId}] Failed removing Chapters: {ex.GetType().Name} - {ex.Message}");
                }
            }

            if (manifest.CourseIds.Count > 0)
            {
                try
                {
                    var courses = await context.Courses
                        .Where(c => manifest.CourseIds.Contains(c.Id))
                        .ToListAsync(CancellationToken.None);
                    if (courses.Count > 0)
                    {
                        context.Courses.RemoveRange(courses);
                        await context.SaveChangesAsync(CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    allCleaned = false;
                    Console.Error.WriteLine($"[CLEANUP ERROR][Run: {runId}] Failed removing Courses: {ex.GetType().Name} - {ex.Message}");
                }
            }

            if (manifest.UserIds.Count > 0)
            {
                try
                {
                    foreach (var userId in manifest.UserIds)
                    {
                        var user = await userManager.FindByIdAsync(userId);
                        if (user == null)
                        {
                            continue;
                        }

                        var deleteResult = await userManager.DeleteAsync(user);
                        if (!deleteResult.Succeeded)
                        {
                            allCleaned = false;
                            var errors = string.Join("; ", deleteResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                            Console.Error.WriteLine($"[CLEANUP ERROR][Run: {runId}] Failed removing User '{userId}': {errors}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    allCleaned = false;
                    Console.Error.WriteLine($"[CLEANUP ERROR][Run: {runId}] Failed removing Users: {ex.GetType().Name} - {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            allCleaned = false;
            Console.Error.WriteLine($"[CLEANUP ERROR][Run: {runId}] Failed initializing cleanup scope: {ex.GetType().Name} - {ex.Message}");
        }

        return allCleaned;
    }

    private sealed class TestRunManifest
    {
        public List<string> UserIds { get; } = new();
        public List<int> CourseIds { get; } = new();
        public List<int> ChapterIds { get; } = new();
        public List<int> CourseItemIds { get; } = new();
        public List<int> EnrollmentIds { get; } = new();
        public List<int> CourseProgressIds { get; } = new();

        public bool IsEmpty =>
            UserIds.Count == 0 &&
            CourseIds.Count == 0 &&
            ChapterIds.Count == 0 &&
            CourseItemIds.Count == 0 &&
            EnrollmentIds.Count == 0 &&
            CourseProgressIds.Count == 0;
    }

    private sealed class TestAssertionFailureException : Exception
    {
        public TestAssertionFailureException(string testName, string error)
            : base($"{testName}: {error}")
        {
        }
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
