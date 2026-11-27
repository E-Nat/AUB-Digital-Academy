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

public static class EnrollmentBackendTestRunner
{
    public static async Task<int> RunTestsAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var emailTag = runId.ToLowerInvariant();
        var manifest = new TestRunManifest();

        Console.WriteLine("=================================================================");
        Console.WriteLine($"🎓  AUB DIGITAL ACADEMY - STEP 3.1 ENROLLMENT BACKEND AUDIT [Run: {runId}]");
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
            string role,
            bool isActive)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var user = new User
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                UniversityId = universityId,
                EmailConfirmed = true,
                IsActive = isActive
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

            // 1. Connectivity Check & Schema Inspection
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                Fail("Database Connectivity", "Cannot connect to SQL Server database.");
            }

            var enrollmentEntityType = context.Model.FindEntityType(typeof(Enrollment));
            if (enrollmentEntityType == null)
            {
                Fail("Schema Inspection", "Enrollment entity not found in EF model.");
            }

            var primaryKey = enrollmentEntityType!.FindPrimaryKey();
            if (primaryKey == null || primaryKey.Properties.Count != 1 || primaryKey.Properties[0].Name != "Id")
            {
                Fail("Schema Inspection", "Enrollment primary key is not 'Id'.");
            }

            var studentFk = enrollmentEntityType.GetForeignKeys().FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(User));
            var courseFk = enrollmentEntityType.GetForeignKeys().FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Course));
            if (studentFk == null || courseFk == null)
            {
                Fail("Schema Inspection", "Foreign keys to User and Course are missing on Enrollment.");
            }

            var uniqueIndex = enrollmentEntityType.GetIndexes().FirstOrDefault(i => i.IsUnique && i.Properties.Any(p => p.Name == "StudentId") && i.Properties.Any(p => p.Name == "CourseId"));
            if (uniqueIndex == null)
            {
                Fail("Schema Inspection", "Unique composite index on (StudentId, CourseId) is missing in EF model configuration.");
            }

            Pass("Database Connectivity & EF Schema Inspection: Primary key, foreign keys (User, Course), and unique index verified");

            // Retrieve shared reference entities (read-only)
            var major = await context.Majors.FirstOrDefaultAsync(cancellationToken);
            var semester = await context.Semesters.FirstOrDefaultAsync(cancellationToken);
            if (major == null || semester == null)
            {
                Fail("Precondition Check", "Required seeded reference entities (Major, Semester) not found.");
            }

            // Create isolated run-scoped users for every required role
            var adminUser = await CreateRunUserAsync(
                userManager,
                email: $"e2e_admin_{emailTag}@test.aub.local",
                fullName: $"E2E Admin {runId}",
                universityId: $"E2E-AD-{runId}",
                role: "Admin",
                isActive: true);

            var teacher1 = await CreateRunUserAsync(
                userManager,
                email: $"e2e_t1_{emailTag}@test.aub.local",
                fullName: $"E2E Teacher One {runId}",
                universityId: $"E2E-T1-{runId}",
                role: "Teacher",
                isActive: true);

            var teacher2 = await CreateRunUserAsync(
                userManager,
                email: $"e2e_t2_{emailTag}@test.aub.local",
                fullName: $"E2E Teacher Two {runId}",
                universityId: $"E2E-T2-{runId}",
                role: "Teacher",
                isActive: true);

            var studentA = await CreateRunUserAsync(
                userManager,
                email: $"e2e_a_{emailTag}@test.aub.local",
                fullName: $"Alice Student {runId}",
                universityId: $"E2E-A-{runId}",
                role: "Student",
                isActive: true);

            var studentB = await CreateRunUserAsync(
                userManager,
                email: $"e2e_b_{emailTag}@test.aub.local",
                fullName: $"Bob Student {runId}",
                universityId: $"E2E-B-{runId}",
                role: "Student",
                isActive: true);

            var inactiveStudent = await CreateRunUserAsync(
                userManager,
                email: $"e2e_inactive_{emailTag}@test.aub.local",
                fullName: $"Inactive Student {runId}",
                universityId: $"E2E-I-{runId}",
                role: "Student",
                isActive: false);

            // Create standard Published Course
            var course1 = new Course
            {
                Code = $"E2E-C1-{runId}",
                Title = $"Cloud Computing Fundamentals [{runId}]",
                Description = "Intro to Cloud Architecture",
                MajorId = major!.Id,
                SemesterId = semester!.Id,
                TeacherId = teacher1.Id,
                Status = CourseStatus.Published
            };
            context.Courses.Add(course1);
            await context.SaveChangesAsync(cancellationToken);
            manifest.CourseIds.Add(course1.Id);

            // 2. Validate Nonexistent Student ID
            var invalidStudentResult = await enrollmentService.EnrollStudentAsync($"nonexistent-{runId}", course1.Id);
            if (invalidStudentResult.Status != EnrollmentOperationStatus.StudentNotFound || invalidStudentResult.IsSuccess)
            {
                Fail("Validate Nonexistent Student", $"Expected StudentNotFound, got {invalidStudentResult.Status}");
            }
            Pass("Validate Student: Nonexistent student ID rejected (Status: StudentNotFound)");

            // 3. Validate Inactive Student
            var inactiveStudentResult = await enrollmentService.EnrollStudentAsync(inactiveStudent.Id, course1.Id);
            if (inactiveStudentResult.Status != EnrollmentOperationStatus.InvalidStudent || inactiveStudentResult.IsSuccess)
            {
                Fail("Validate Inactive Student", $"Expected InvalidStudent for inactive user, got {inactiveStudentResult.Status}");
            }
            Pass("Validate Student: Inactive student account rejected (Status: InvalidStudent)");

            // 4. Validate User Without Student Role
            var nonStudentResult = await enrollmentService.EnrollStudentAsync(teacher1.Id, course1.Id);
            if (nonStudentResult.Status != EnrollmentOperationStatus.InvalidStudent || nonStudentResult.IsSuccess)
            {
                Fail("Validate Student Role", $"Expected InvalidStudent for non-student role, got {nonStudentResult.Status}");
            }
            Pass("Validate Student: User without Student role rejected (Status: InvalidStudent)");

            // 5. Validate Nonexistent Course ID
            var invalidCourseResult = await enrollmentService.EnrollStudentAsync(studentA.Id, 999999);
            if (invalidCourseResult.Status != EnrollmentOperationStatus.CourseNotFound || invalidCourseResult.IsSuccess)
            {
                Fail("Validate Nonexistent Course", $"Expected CourseNotFound, got {invalidCourseResult.Status}");
            }
            Pass("Validate Course: Nonexistent course ID rejected (Status: CourseNotFound)");

            // 6. Validate Archived Course
            var archivedCourse = new Course
            {
                Code = $"E2E-AR-{runId}",
                Title = $"Legacy Fortran Architecture [{runId}]",
                MajorId = major.Id,
                SemesterId = semester.Id,
                TeacherId = teacher1.Id,
                Status = CourseStatus.Archived
            };
            context.Courses.Add(archivedCourse);
            await context.SaveChangesAsync(cancellationToken);
            manifest.CourseIds.Add(archivedCourse.Id);

            var archivedResult = await enrollmentService.EnrollStudentAsync(studentA.Id, archivedCourse.Id);
            if (archivedResult.Status != EnrollmentOperationStatus.CourseNotAvailable || archivedResult.IsSuccess)
            {
                Fail("Validate Archived Course", $"Expected CourseNotAvailable for archived course, got {archivedResult.Status}");
            }
            Pass("Validate Course: Archived course rejected from enrollment (Status: CourseNotAvailable)");

            // 7. Validate Draft Course for Student
            var draftCourse = new Course
            {
                Code = $"E2E-DR-{runId}",
                Title = $"Upcoming Quantum Computing [{runId}]",
                MajorId = major.Id,
                SemesterId = semester.Id,
                TeacherId = teacher1.Id,
                Status = CourseStatus.Draft
            };
            context.Courses.Add(draftCourse);
            await context.SaveChangesAsync(cancellationToken);
            manifest.CourseIds.Add(draftCourse.Id);

            var draftResult = await enrollmentService.EnrollStudentAsync(studentA.Id, draftCourse.Id, isAdmin: false);
            if (draftResult.Status != EnrollmentOperationStatus.CourseNotAvailable || draftResult.IsSuccess)
            {
                Fail("Validate Draft Course", $"Expected CourseNotAvailable for draft course, got {draftResult.Status}");
            }
            Pass("Validate Course: Draft course unavailable for student self-enrollment (Status: CourseNotAvailable)");

            // 8. Test Valid Enrollment Creation (Student A -> Course 1)
            var validEnrollmentResult = await enrollmentService.EnrollStudentAsync(studentA.Id, course1.Id);
            if (!validEnrollmentResult.IsSuccess || validEnrollmentResult.Status != EnrollmentOperationStatus.Success)
            {
                Fail("Valid Enrollment Creation", $"Expected Success, got {validEnrollmentResult.Status} ({validEnrollmentResult.Message})");
            }
            if (validEnrollmentResult.EnrollmentId is int validEnrollmentId)
            {
                manifest.EnrollmentIds.Add(validEnrollmentId);
            }
            else
            {
                Fail("Valid Enrollment Creation", "Enrollment succeeded but returned a null EnrollmentId.");
            }

            // Verify in SQL Server
            using (var verifyScope1 = serviceProvider.CreateScope())
            {
                var verifyContext1 = verifyScope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var dbEnrollment = await verifyContext1.Enrollments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.StudentId == studentA.Id && e.CourseId == course1.Id, cancellationToken);

                if (dbEnrollment == null || dbEnrollment.Id != validEnrollmentResult.EnrollmentId)
                {
                    Fail("SQL Server Persistence", "Enrollment record not found in SQL Server after creation.");
                }
                Pass($"Enrollment Creation: Student '{studentA.FullName}' enrolled in '{course1.Title}' (ID: {dbEnrollment!.Id}) and persisted to SQL Server");
            }

            // 9. Prevent Duplicate Enrollment
            var duplicateResult = await enrollmentService.EnrollStudentAsync(studentA.Id, course1.Id);
            if (duplicateResult.IsSuccess || duplicateResult.Status != EnrollmentOperationStatus.AlreadyEnrolled)
            {
                Fail("Prevent Duplicate Enrollment", $"Expected AlreadyEnrolled, got {duplicateResult.Status}");
            }

            // Verify exactly 1 record in SQL Server
            using (var verifyScope2 = serviceProvider.CreateScope())
            {
                var verifyContext2 = verifyScope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var count = await verifyContext2.Enrollments.CountAsync(
                    e => e.StudentId == studentA.Id && e.CourseId == course1.Id,
                    cancellationToken);
                if (count != 1)
                {
                    Fail("Prevent Duplicate Enrollment", $"Expected exactly 1 enrollment record, found {count}.");
                }
            }
            Pass("Prevent Duplicate Enrollment: Duplicate enrollment safely rejected; exactly 1 record exists in SQL Server");

            // 10. Check Enrollment Status
            var isEnrolledTrue = await enrollmentService.IsStudentEnrolledAsync(studentA.Id, course1.Id);
            var isEnrolledFalse = await enrollmentService.IsStudentEnrolledAsync(studentB.Id, course1.Id);
            var isEnrolledInvalid = await enrollmentService.IsStudentEnrolledAsync(studentA.Id, 999999);

            if (!isEnrolledTrue || isEnrolledFalse || isEnrolledInvalid)
            {
                Fail("Check Enrollment Status", $"Status check mismatch: A={isEnrolledTrue} (expected true), B={isEnrolledFalse} (expected false)");
            }
            Pass("Check Enrollment Status: IsStudentEnrolledAsync returns true for enrolled student and false for non-enrolled");

            // 11. Student Enrollments Retrieval Isolation
            var course2 = new Course
            {
                Code = $"E2E-C2-{runId}",
                Title = $"Database Systems & Administration [{runId}]",
                MajorId = major.Id,
                SemesterId = semester.Id,
                TeacherId = teacher1.Id,
                Status = CourseStatus.Published
            };
            var course3 = new Course
            {
                Code = $"E2E-C3-{runId}",
                Title = $"Artificial Intelligence Principles [{runId}]",
                MajorId = major.Id,
                SemesterId = semester.Id,
                TeacherId = teacher1.Id,
                Status = CourseStatus.Published
            };
            context.Courses.AddRange(course2, course3);
            await context.SaveChangesAsync(cancellationToken);
            manifest.CourseIds.Add(course2.Id);
            manifest.CourseIds.Add(course3.Id);

            var enrollA2 = await enrollmentService.EnrollStudentAsync(studentA.Id, course2.Id);
            if (!enrollA2.IsSuccess)
            {
                Fail("Student Enrollment Setup", $"Failed to enroll Student A in Course 2: {enrollA2.Message}");
            }
            if (enrollA2.EnrollmentId is int enrollA2Id)
            {
                manifest.EnrollmentIds.Add(enrollA2Id);
            }
            else
            {
                Fail("Student Enrollment Setup", "Enrollment for Student A in Course 2 succeeded but returned a null EnrollmentId.");
            }

            var enrollB3 = await enrollmentService.EnrollStudentAsync(studentB.Id, course3.Id);
            if (!enrollB3.IsSuccess)
            {
                Fail("Student Enrollment Setup", $"Failed to enroll Student B in Course 3: {enrollB3.Message}");
            }
            if (enrollB3.EnrollmentId is int enrollB3Id)
            {
                manifest.EnrollmentIds.Add(enrollB3Id);
            }
            else
            {
                Fail("Student Enrollment Setup", "Enrollment for Student B in Course 3 succeeded but returned a null EnrollmentId.");
            }

            var studentAEnrollments = (await enrollmentService.GetStudentEnrollmentsAsync(studentA.Id)).ToList();
            var studentBEnrollments = (await enrollmentService.GetStudentEnrollmentsAsync(studentB.Id)).ToList();

            if (studentAEnrollments.Count != 2 || !studentAEnrollments.Any(e => e.CourseId == course1.Id) || !studentAEnrollments.Any(e => e.CourseId == course2.Id))
            {
                Fail("Student Enrollment Retrieval", $"Student A expected 2 enrollments (C1, C2), found {studentAEnrollments.Count}.");
            }

            if (studentAEnrollments.Any(e => e.CourseId == course3.Id))
            {
                Fail("Student Enrollment Retrieval Isolation", "Student B's course (C3) appeared in Student A's enrollment list.");
            }

            if (studentBEnrollments.Count != 1 || studentBEnrollments[0].CourseId != course3.Id)
            {
                Fail("Student Enrollment Retrieval", $"Student B expected 1 enrollment (C3), found {studentBEnrollments.Count}.");
            }

            if (studentAEnrollments[0].Course == null || studentAEnrollments[0].Course.Teacher == null || studentAEnrollments[0].Course.Major == null)
            {
                Fail("Student Enrollment Navigation", "Course navigation properties (Teacher, Major) were not included in retrieval.");
            }

            Pass("Student Enrollment Retrieval: Accurate student-specific course lists returned with navigation graphs; strict cross-student isolation verified");

            // 12. Course Enrollments Retrieval Isolation
            var enrollB1 = await enrollmentService.EnrollStudentAsync(studentB.Id, course1.Id);
            if (!enrollB1.IsSuccess)
            {
                Fail("Course Enrollment Setup", $"Failed to enroll Student B in Course 1: {enrollB1.Message}");
            }
            if (enrollB1.EnrollmentId is int enrollB1Id)
            {
                manifest.EnrollmentIds.Add(enrollB1Id);
            }
            else
            {
                Fail("Course Enrollment Setup", "Enrollment for Student B in Course 1 succeeded but returned a null EnrollmentId.");
            }

            var course1Enrollments = (await enrollmentService.GetCourseEnrollmentsAsync(course1.Id)).ToList();
            var course3Enrollments = (await enrollmentService.GetCourseEnrollmentsAsync(course3.Id)).ToList();

            if (course1Enrollments.Count != 2 || !course1Enrollments.Any(e => e.StudentId == studentA.Id) || !course1Enrollments.Any(e => e.StudentId == studentB.Id))
            {
                Fail("Course Enrollment Retrieval", $"Course 1 expected 2 students (A, B), found {course1Enrollments.Count}.");
            }

            if (course3Enrollments.Count != 1 || course3Enrollments[0].StudentId != studentB.Id)
            {
                Fail("Course Enrollment Retrieval", $"Course 3 expected 1 student (B), found {course3Enrollments.Count}.");
            }

            if (course1Enrollments[0].Student == null || string.IsNullOrEmpty(course1Enrollments[0].Student.FullName))
            {
                Fail("Course Enrollment Navigation", "Student navigation property not populated on Course enrollments.");
            }

            Pass("Course Enrollment Retrieval: Accurate course-specific student rosters returned; strict cross-course isolation verified");

            // 13. Student Ownership Security & Tamper Resistance
            var studentBEnrollmentInC3 = studentBEnrollments.First(e => e.CourseId == course3.Id);
            var unauthorizedRemoval = await enrollmentService.RemoveEnrollmentAsync(studentBEnrollmentInC3.Id, requestedByUserId: studentA.Id, isAdmin: false);

            if (unauthorizedRemoval.Status != EnrollmentOperationStatus.Unauthorized || unauthorizedRemoval.IsSuccess)
            {
                Fail("Ownership Security", $"Expected Unauthorized when Student A tries to unenroll Student B, got {unauthorizedRemoval.Status}");
            }
            Pass("Student Ownership Security: Student A cannot unenroll Student B or manipulate another student's enrollment records (Status: Unauthorized)");

            // 14. Controller Authorization & Ownership Guard
            var studentController = new EnrollmentController(
                enrollmentService,
                courseService,
                userManager,
                context,
                controllerLogger);

            var studentClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, studentA.Id),
                new Claim(ClaimTypes.Name, studentA.UserName!),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            studentController.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = studentClaims }
            };
            studentController.TempData = new TempDataDictionary(studentController.HttpContext, new TestTempDataProvider());

            // Student calls MyCourses: redirects to canonical Student/Courses
            var myCoursesRedirect = studentController.MyCourses() as RedirectToActionResult;
            if (myCoursesRedirect == null || myCoursesRedirect.ControllerName != "Student" || myCoursesRedirect.ActionName != "Courses")
            {
                Fail("Controller MyCourses", "MyCourses action did not redirect to canonical Student/Courses.");
            }

            var studentAMyCoursesEnrollments = (await enrollmentService.GetStudentEnrollmentsAsync(studentA.Id)).ToList();
            if (studentAMyCoursesEnrollments.Count != 2 || studentAMyCoursesEnrollments.Any(c => c.CourseId == course3.Id))
            {
                Fail("Controller MyCourses Isolation", "Student A enrollment isolation returned incorrect course count or leaked other students' enrollments.");
            }
            Pass("Controller Security: MyCourses redirects to canonical Student/Courses while student enrollments remain strictly isolated with 0 data leakage");

            // Student attempts to enroll specifying Student B's ID in request
            // Controller must ignore the passed studentId, enroll Student A, and redirect to Student/Courses
            var enrollRedirect = await studentController.Enroll(course3.Id, studentId: studentB.Id) as RedirectToActionResult;
            if (enrollRedirect == null || enrollRedirect.ControllerName != "Student" || enrollRedirect.ActionName != "Courses")
            {
                Fail("Controller Enroll Redirect", "Enroll fallback did not redirect student to canonical Student/Courses.");
            }

            var controllerCreatedEnrollment = await context.Enrollments
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.StudentId == studentA.Id && e.CourseId == course3.Id, cancellationToken);
            if (controllerCreatedEnrollment == null)
            {
                Fail("Controller Enroll Tamper Resistance", "Controller did not bind enrollment to authenticated student when unauthorized studentId was passed.");
            }
            manifest.EnrollmentIds.Add(controllerCreatedEnrollment!.Id);
            Pass("Controller Security: Enroll action strictly overrides caller-supplied studentId with authenticated Claims identity for Student role");

            // 15. Teacher Access Control to Course Enrollments (using run-scoped teacher2 who does NOT teach Course 1)
            var teacher2Controller = new EnrollmentController(
                enrollmentService,
                courseService,
                userManager,
                context,
                controllerLogger);

            var teacher2Claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, teacher2.Id),
                new Claim(ClaimTypes.Name, teacher2.UserName!),
                new Claim(ClaimTypes.Role, "Teacher")
            }, "TestAuth"));

            teacher2Controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = teacher2Claims }
            };

            var forbiddenResult = await teacher2Controller.CourseStudents(course1.Id);
            if (forbiddenResult is not ForbidResult)
            {
                Fail("Teacher Course Authorization", "Teacher was able to access enrollments for a course they do not teach.");
            }
            Pass("Teacher Authorization: Teacher access to course enrollment roster strictly verified against course ownership (ForbidResult returned)");

            // 16. Admin Enrollment Operations
            var adminController = new EnrollmentController(
                enrollmentService,
                courseService,
                userManager,
                context,
                controllerLogger);

            var adminClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, adminUser.Id),
                new Claim(ClaimTypes.Name, adminUser.UserName!),
                new Claim(ClaimTypes.Role, "Admin")
            }, "TestAuth"));

            adminController.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = adminClaims }
            };

            var adminCourseStudentsResult = await adminController.CourseStudents(course1.Id);
            if (adminCourseStudentsResult is not ViewResult)
            {
                Fail("Admin Course Students", "Admin was denied access to view course enrollments.");
            }
            Pass("Admin Operations: Administrator successfully authorized to view and manage enrollment rosters for any course");

            // 17. Safe Enrollment Removal Policy - Case A: No Academic History
            var safeRemoveResult = await enrollmentService.RemoveEnrollmentByStudentAndCourseAsync(studentA.Id, course3.Id, requestedByUserId: studentA.Id);
            if (!safeRemoveResult.IsSuccess || safeRemoveResult.Status != EnrollmentOperationStatus.Success)
            {
                Fail("Safe Enrollment Removal Case A", $"Expected Success when student has no academic history, got {safeRemoveResult.Status}");
            }

            var isStillEnrolledInC3 = await enrollmentService.IsStudentEnrolledAsync(studentA.Id, course3.Id);
            if (isStillEnrolledInC3)
            {
                Fail("Safe Enrollment Removal Case A", "Enrollment record still exists after safe removal.");
            }
            Pass("Safe Removal Policy (Case A - No Activity): Enrollment safely removed when zero learning progress or submissions exist");

            // 18. Safe Enrollment Removal Policy - Case B: Academic History Exists (Protected)
            var chapter1 = new Chapter { Title = $"Module 1 [{runId}]", Position = 1, CourseId = course1.Id };
            context.Chapters.Add(chapter1);
            await context.SaveChangesAsync(cancellationToken);
            manifest.ChapterIds.Add(chapter1.Id);

            var item1 = new CourseItem { Title = $"Lesson 1 [{runId}]", Position = 1, ChapterId = chapter1.Id, Type = ItemType.Lesson };
            context.CourseItems.Add(item1);
            await context.SaveChangesAsync(cancellationToken);
            manifest.CourseItemIds.Add(item1.Id);

            var progress = new CourseProgress
            {
                StudentId = studentA.Id,
                CourseItemId = item1.Id,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow
            };
            context.CourseProgresses.Add(progress);
            await context.SaveChangesAsync(cancellationToken);
            manifest.CourseProgressIds.Add(progress.Id);

            var hasActivity = await enrollmentService.HasStudentLearningActivityAsync(studentA.Id, course1.Id);
            if (!hasActivity)
            {
                Fail("Activity Detection", "HasStudentLearningActivityAsync returned false when CourseProgress exists.");
            }

            var blockedRemoveResult = await enrollmentService.RemoveEnrollmentByStudentAndCourseAsync(studentA.Id, course1.Id, requestedByUserId: studentA.Id);
            if (blockedRemoveResult.IsSuccess || blockedRemoveResult.Status != EnrollmentOperationStatus.BlockedByStudentActivity)
            {
                Fail("Safe Removal Policy Case B", $"Expected BlockedByStudentActivity when progress exists, got {blockedRemoveResult.Status}");
            }

            // Verify Enrollment is still in SQL Server and Progress is intact
            using (var verifyScope3 = serviceProvider.CreateScope())
            {
                var verifyContext3 = verifyScope3.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var persistedEnrollment = await verifyContext3.Enrollments.FirstOrDefaultAsync(
                    e => e.StudentId == studentA.Id && e.CourseId == course1.Id,
                    cancellationToken);
                var persistedProgress = await verifyContext3.CourseProgresses.FirstOrDefaultAsync(
                    cp => cp.StudentId == studentA.Id && cp.CourseItemId == item1.Id,
                    cancellationToken);

                if (persistedEnrollment == null || persistedProgress == null)
                {
                    Fail("Safe Removal Policy Case B", "Enrollment or CourseProgress was accidentally destroyed when removal should have been blocked!");
                }
            }
            Pass("Safe Removal Policy (Case B - Academic History Exists): Unenrollment strictly blocked when student has learning activity; academic history fully preserved");

            // 19. Course Deletion Regression Test (Enrollments Block Course Deletion)
            var courseDeleteResult = await courseService.DeleteCourseAsync(course1.Id);
            if (courseDeleteResult.IsSuccess || courseDeleteResult.Status != CourseDeleteStatus.BlockedByStudentData)
            {
                Fail("Course Delete Regression", $"Expected BlockedByStudentData when enrollments and progress exist, got {courseDeleteResult.Status}");
            }
            Pass("Course Deletion Policy Regression: Active enrollments and student activity strictly protect course from accidental deletion");

            // 20. Database Cleanliness & Orphan Check (remove progress & verify unenroll-after-activity-resolved)
            context.CourseProgresses.Remove(progress);
            await context.SaveChangesAsync(cancellationToken);

            var nowSafeRemove = await enrollmentService.RemoveEnrollmentByStudentAndCourseAsync(studentA.Id, course1.Id, isAdmin: true);
            if (!nowSafeRemove.IsSuccess)
            {
                Fail("Unenroll After Activity Resolved", "Unenrollment failed after resolving academic activity.");
            }

            var orphanEnrollmentUsers = await context.Enrollments
                .Where(e => !context.Users.Any(u => u.Id == e.StudentId))
                .CountAsync(cancellationToken);

            var orphanEnrollmentCourses = await context.Enrollments
                .Where(e => !context.Courses.Any(c => c.Id == e.CourseId))
                .CountAsync(cancellationToken);

            if (orphanEnrollmentUsers > 0 || orphanEnrollmentCourses > 0)
            {
                Fail("Orphan Check", $"Found orphan enrollments: {orphanEnrollmentUsers} without user, {orphanEnrollmentCourses} without course.");
            }

            Pass("Database Cleanliness & Orphan Check: 0 orphan enrollment rows; run-scoped entities queued for manifest cleanup");

            suiteSucceeded = true;
        }
        catch (OperationCanceledException)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  [CANCELLED] Enrollment backend test run {runId} was cancelled. Running manifest cleanup...");
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
            Console.WriteLine($"🎉  ALL {testsPassed}/{totalTests} ENROLLMENT BACKEND TESTS PASSED SUCCESSFULLY! [Run: {runId}]");
            Console.WriteLine("=================================================================\n");
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n❌  ENROLLMENT BACKEND TEST RUN FAILED OR INCOMPLETE [Run: {runId}] (Passed: {testsPassed}/{totalTests}, CleanupFailed: {cleanupFailed})");
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
