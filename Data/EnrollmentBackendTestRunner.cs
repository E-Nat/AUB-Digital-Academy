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

public static class EnrollmentBackendTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("🎓  AUB DIGITAL ACADEMY - STEP 3.1 ENROLLMENT BACKEND AUDIT");
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

        // 1. Connectivity Check & Schema Inspection
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            Fail("Database Connectivity", "Cannot connect to SQL Server database.");
            return;
        }

        var enrollmentEntityType = context.Model.FindEntityType(typeof(Enrollment));
        if (enrollmentEntityType == null)
        {
            Fail("Schema Inspection", "Enrollment entity not found in EF model.");
            return;
        }

        var primaryKey = enrollmentEntityType.FindPrimaryKey();
        if (primaryKey == null || primaryKey.Properties.Count != 1 || primaryKey.Properties[0].Name != "Id")
        {
            Fail("Schema Inspection", "Enrollment primary key is not 'Id'.");
            return;
        }

        var studentFk = enrollmentEntityType.GetForeignKeys().FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(User));
        var courseFk = enrollmentEntityType.GetForeignKeys().FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Course));
        if (studentFk == null || courseFk == null)
        {
            Fail("Schema Inspection", "Foreign keys to User and Course are missing on Enrollment.");
            return;
        }

        var uniqueIndex = enrollmentEntityType.GetIndexes().FirstOrDefault(i => i.IsUnique && i.Properties.Any(p => p.Name == "StudentId") && i.Properties.Any(p => p.Name == "CourseId"));
        if (uniqueIndex == null)
        {
            Fail("Schema Inspection", "Unique composite index on (StudentId, CourseId) is missing in EF model configuration.");
            return;
        }

        Pass("Database Connectivity & EF Schema Inspection: Primary key, foreign keys (User, Course), and unique index verified");

        // Seed or retrieve required entities
        var major = await context.Majors.FirstOrDefaultAsync();
        var semester = await context.Semesters.FirstOrDefaultAsync();
        var teacher = await userManager.FindByEmailAsync("teacher@aub.com");
        var adminUser = await userManager.FindByEmailAsync("admin@aub.com");

        if (major == null || semester == null || teacher == null || adminUser == null)
        {
            Fail("Precondition Check", "Required seeded entities (Major, Semester, Teacher, Admin) not found.");
            return;
        }

        // Setup Test Students
        var studentAEmail = "e2e_student_a@aub.edu.kh";
        var studentA = await userManager.FindByEmailAsync(studentAEmail);
        if (studentA == null)
        {
            studentA = new User
            {
                UserName = studentAEmail,
                Email = studentAEmail,
                FullName = "Alice Student",
                UniversityId = "STU-E2E-A",
                EmailConfirmed = true,
                IsActive = true
            };
            await userManager.CreateAsync(studentA, "Student@123");
            await userManager.AddToRoleAsync(studentA, "Student");
        }

        var studentBEmail = "e2e_student_b@aub.edu.kh";
        var studentB = await userManager.FindByEmailAsync(studentBEmail);
        if (studentB == null)
        {
            studentB = new User
            {
                UserName = studentBEmail,
                Email = studentBEmail,
                FullName = "Bob Student",
                UniversityId = "STU-E2E-B",
                EmailConfirmed = true,
                IsActive = true
            };
            await userManager.CreateAsync(studentB, "Student@123");
            await userManager.AddToRoleAsync(studentB, "Student");
        }

        var inactiveStudentEmail = "e2e_inactive_student@aub.edu.kh";
        var inactiveStudent = await userManager.FindByEmailAsync(inactiveStudentEmail);
        if (inactiveStudent == null)
        {
            inactiveStudent = new User
            {
                UserName = inactiveStudentEmail,
                Email = inactiveStudentEmail,
                FullName = "Inactive Student",
                UniversityId = "STU-INACT",
                EmailConfirmed = true,
                IsActive = false
            };
            await userManager.CreateAsync(inactiveStudent, "Student@123");
            await userManager.AddToRoleAsync(inactiveStudent, "Student");
        }
        else
        {
            inactiveStudent.IsActive = false;
            await userManager.UpdateAsync(inactiveStudent);
        }

        // Clean up previous test enrollments/courses
        var testCodes = new[] { "ENR-C1", "ENR-C2", "ENR-C3", "ENR-DRAFT", "ENR-ARCH" };
        var existingTestCourses = await context.Courses
            .Include(c => c.Enrollments)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Progress)
            .Where(c => testCodes.Contains(c.Code))
            .ToListAsync();

        foreach (var tc in existingTestCourses)
        {
            foreach (var ch in tc.Chapters)
            {
                foreach (var ci in ch.CourseItems)
                {
                    context.CourseProgresses.RemoveRange(ci.Progress);
                }
                context.CourseItems.RemoveRange(ch.CourseItems);
            }
            context.Chapters.RemoveRange(tc.Chapters);
            context.Enrollments.RemoveRange(tc.Enrollments);
            context.Courses.Remove(tc);
        }
        await context.SaveChangesAsync();

        // Create standard Published Course
        var course1 = new Course
        {
            Code = "ENR-C1",
            Title = "Cloud Computing Fundamentals",
            Description = "Intro to Cloud Architecture",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Published
        };
        context.Courses.Add(course1);
        await context.SaveChangesAsync();

        // 2. Validate Nonexistent Student ID
        var invalidStudentResult = await enrollmentService.EnrollStudentAsync("nonexistent-guid-9999", course1.Id);
        if (invalidStudentResult.Status != EnrollmentOperationStatus.StudentNotFound || invalidStudentResult.IsSuccess)
        {
            Fail("Validate Nonexistent Student", $"Expected StudentNotFound, got {invalidStudentResult.Status}");
            return;
        }
        Pass("Validate Student: Nonexistent student ID rejected (Status: StudentNotFound)");

        // 3. Validate Inactive Student
        var inactiveStudentResult = await enrollmentService.EnrollStudentAsync(inactiveStudent.Id, course1.Id);
        if (inactiveStudentResult.Status != EnrollmentOperationStatus.InvalidStudent || inactiveStudentResult.IsSuccess)
        {
            Fail("Validate Inactive Student", $"Expected InvalidStudent for inactive user, got {inactiveStudentResult.Status}");
            return;
        }
        Pass("Validate Student: Inactive student account rejected (Status: InvalidStudent)");

        // 4. Validate User Without Student Role
        var nonStudentResult = await enrollmentService.EnrollStudentAsync(teacher.Id, course1.Id);
        if (nonStudentResult.Status != EnrollmentOperationStatus.InvalidStudent || nonStudentResult.IsSuccess)
        {
            Fail("Validate Student Role", $"Expected InvalidStudent for non-student role, got {nonStudentResult.Status}");
            return;
        }
        Pass("Validate Student: User without Student role rejected (Status: InvalidStudent)");

        // 5. Validate Nonexistent Course ID
        var invalidCourseResult = await enrollmentService.EnrollStudentAsync(studentA.Id, 999999);
        if (invalidCourseResult.Status != EnrollmentOperationStatus.CourseNotFound || invalidCourseResult.IsSuccess)
        {
            Fail("Validate Nonexistent Course", $"Expected CourseNotFound, got {invalidCourseResult.Status}");
            return;
        }
        Pass("Validate Course: Nonexistent course ID rejected (Status: CourseNotFound)");

        // 6. Validate Archived Course
        var archivedCourse = new Course
        {
            Code = "ENR-ARCH",
            Title = "Legacy Fortran Architecture",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Archived
        };
        context.Courses.Add(archivedCourse);
        await context.SaveChangesAsync();

        var archivedResult = await enrollmentService.EnrollStudentAsync(studentA.Id, archivedCourse.Id);
        if (archivedResult.Status != EnrollmentOperationStatus.CourseNotAvailable || archivedResult.IsSuccess)
        {
            Fail("Validate Archived Course", $"Expected CourseNotAvailable for archived course, got {archivedResult.Status}");
            return;
        }
        Pass("Validate Course: Archived course rejected from enrollment (Status: CourseNotAvailable)");

        // 7. Validate Draft Course for Student
        var draftCourse = new Course
        {
            Code = "ENR-DRAFT",
            Title = "Upcoming Quantum Computing",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Draft
        };
        context.Courses.Add(draftCourse);
        await context.SaveChangesAsync();

        var draftResult = await enrollmentService.EnrollStudentAsync(studentA.Id, draftCourse.Id, isAdmin: false);
        if (draftResult.Status != EnrollmentOperationStatus.CourseNotAvailable || draftResult.IsSuccess)
        {
            Fail("Validate Draft Course", $"Expected CourseNotAvailable for draft course, got {draftResult.Status}");
            return;
        }
        Pass("Validate Course: Draft course unavailable for student self-enrollment (Status: CourseNotAvailable)");

        // 8. Test Valid Enrollment Creation (Student A -> Course 1)
        var validEnrollmentResult = await enrollmentService.EnrollStudentAsync(studentA.Id, course1.Id);
        if (!validEnrollmentResult.IsSuccess || validEnrollmentResult.Status != EnrollmentOperationStatus.Success)
        {
            Fail("Valid Enrollment Creation", $"Expected Success, got {validEnrollmentResult.Status} ({validEnrollmentResult.Message})");
            return;
        }

        // Verify in SQL Server
        using var verifyScope1 = serviceProvider.CreateScope();
        var verifyContext1 = verifyScope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbEnrollment = await verifyContext1.Enrollments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.StudentId == studentA.Id && e.CourseId == course1.Id);

        if (dbEnrollment == null || dbEnrollment.Id != validEnrollmentResult.EnrollmentId)
        {
            Fail("SQL Server Persistence", "Enrollment record not found in SQL Server after creation.");
            return;
        }
        Pass($"Enrollment Creation: Student '{studentA.FullName}' enrolled in '{course1.Title}' (ID: {dbEnrollment.Id}) and persisted to SQL Server");

        // 9. Prevent Duplicate Enrollment
        var duplicateResult = await enrollmentService.EnrollStudentAsync(studentA.Id, course1.Id);
        if (duplicateResult.IsSuccess || duplicateResult.Status != EnrollmentOperationStatus.AlreadyEnrolled)
        {
            Fail("Prevent Duplicate Enrollment", $"Expected AlreadyEnrolled, got {duplicateResult.Status}");
            return;
        }

        // Verify exactly 1 record in SQL Server
        using var verifyScope2 = serviceProvider.CreateScope();
        var verifyContext2 = verifyScope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var count = await verifyContext2.Enrollments.CountAsync(e => e.StudentId == studentA.Id && e.CourseId == course1.Id);
        if (count != 1)
        {
            Fail("Prevent Duplicate Enrollment", $"Expected exactly 1 enrollment record, found {count}.");
            return;
        }
        Pass("Prevent Duplicate Enrollment: Duplicate enrollment safely rejected; exactly 1 record exists in SQL Server");

        // 10. Check Enrollment Status
        var isEnrolledTrue = await enrollmentService.IsStudentEnrolledAsync(studentA.Id, course1.Id);
        var isEnrolledFalse = await enrollmentService.IsStudentEnrolledAsync(studentB.Id, course1.Id);
        var isEnrolledInvalid = await enrollmentService.IsStudentEnrolledAsync(studentA.Id, 999999);

        if (!isEnrolledTrue || isEnrolledFalse || isEnrolledInvalid)
        {
            Fail("Check Enrollment Status", $"Status check mismatch: A={isEnrolledTrue} (expected true), B={isEnrolledFalse} (expected false)");
            return;
        }
        Pass("Check Enrollment Status: IsStudentEnrolledAsync returns true for enrolled student and false for non-enrolled");

        // 11. Student Enrollments Retrieval Isolation
        // Enroll Student A in Course 2, Student B in Course 3
        var course2 = new Course
        {
            Code = "ENR-C2",
            Title = "Database Systems & Administration",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Published
        };
        var course3 = new Course
        {
            Code = "ENR-C3",
            Title = "Artificial Intelligence Principles",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Published
        };
        context.Courses.AddRange(course2, course3);
        await context.SaveChangesAsync();

        await enrollmentService.EnrollStudentAsync(studentA.Id, course2.Id);
        await enrollmentService.EnrollStudentAsync(studentB.Id, course3.Id);

        var studentAEnrollments = (await enrollmentService.GetStudentEnrollmentsAsync(studentA.Id)).ToList();
        var studentBEnrollments = (await enrollmentService.GetStudentEnrollmentsAsync(studentB.Id)).ToList();

        if (studentAEnrollments.Count != 2 || !studentAEnrollments.Any(e => e.CourseId == course1.Id) || !studentAEnrollments.Any(e => e.CourseId == course2.Id))
        {
            Fail("Student Enrollment Retrieval", $"Student A expected 2 enrollments (C1, C2), found {studentAEnrollments.Count}.");
            return;
        }

        if (studentAEnrollments.Any(e => e.CourseId == course3.Id))
        {
            Fail("Student Enrollment Retrieval Isolation", "Student B's course (C3) appeared in Student A's enrollment list.");
            return;
        }

        if (studentBEnrollments.Count != 1 || studentBEnrollments[0].CourseId != course3.Id)
        {
            Fail("Student Enrollment Retrieval", $"Student B expected 1 enrollment (C3), found {studentBEnrollments.Count}.");
            return;
        }

        if (studentAEnrollments[0].Course == null || studentAEnrollments[0].Course.Teacher == null || studentAEnrollments[0].Course.Major == null)
        {
            Fail("Student Enrollment Navigation", "Course navigation properties (Teacher, Major) were not included in retrieval.");
            return;
        }

        Pass("Student Enrollment Retrieval: Accurate student-specific course lists returned with navigation graphs; strict cross-student isolation verified");

        // 12. Course Enrollments Retrieval Isolation
        // Course 1 has Student A; let's enroll Student B in Course 1 as well
        await enrollmentService.EnrollStudentAsync(studentB.Id, course1.Id);

        var course1Enrollments = (await enrollmentService.GetCourseEnrollmentsAsync(course1.Id)).ToList();
        var course3Enrollments = (await enrollmentService.GetCourseEnrollmentsAsync(course3.Id)).ToList();

        if (course1Enrollments.Count != 2 || !course1Enrollments.Any(e => e.StudentId == studentA.Id) || !course1Enrollments.Any(e => e.StudentId == studentB.Id))
        {
            Fail("Course Enrollment Retrieval", $"Course 1 expected 2 students (A, B), found {course1Enrollments.Count}.");
            return;
        }

        if (course3Enrollments.Count != 1 || course3Enrollments[0].StudentId != studentB.Id)
        {
            Fail("Course Enrollment Retrieval", $"Course 3 expected 1 student (B), found {course3Enrollments.Count}.");
            return;
        }

        if (course1Enrollments[0].Student == null || string.IsNullOrEmpty(course1Enrollments[0].Student.FullName))
        {
            Fail("Course Enrollment Navigation", "Student navigation property not populated on Course enrollments.");
            return;
        }

        Pass("Course Enrollment Retrieval: Accurate course-specific student rosters returned; strict cross-course isolation verified");

        // 13. Student Ownership Security & Tamper Resistance
        // Student A attempts to unenroll Student B via service
        var studentBEnrollmentInC3 = studentBEnrollments.First(e => e.CourseId == course3.Id);
        var unauthorizedRemoval = await enrollmentService.RemoveEnrollmentAsync(studentBEnrollmentInC3.Id, requestedByUserId: studentA.Id, isAdmin: false);

        if (unauthorizedRemoval.Status != EnrollmentOperationStatus.Unauthorized || unauthorizedRemoval.IsSuccess)
        {
            Fail("Ownership Security", $"Expected Unauthorized when Student A tries to unenroll Student B, got {unauthorizedRemoval.Status}");
            return;
        }
        Pass("Student Ownership Security: Student A cannot unenroll Student B or manipulate another student's enrollment records (Status: Unauthorized)");

        // 14. Controller Authorization & Ownership Guard
        // Instantiate EnrollmentController with Student A's identity
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

        // Student calls MyCourses: returns only Student A's courses
        var myCoursesResult = await studentController.MyCourses() as ViewResult;
        if (myCoursesResult == null || myCoursesResult.Model is not List<StudentEnrollmentItemViewModel> myCoursesList)
        {
            Fail("Controller MyCourses", "MyCourses action did not return expected ViewResult with StudentEnrollmentItemViewModel list.");
            return;
        }

        if (myCoursesList.Count != 2 || myCoursesList.Any(c => c.CourseId == course3.Id))
        {
            Fail("Controller MyCourses Isolation", "MyCourses returned incorrect course count or leaked other students' enrollments.");
            return;
        }
        Pass("Controller Security: MyCourses automatically binds to authenticated Identity user; returns accurate enrolled courses with 0 data leakage");

        // Student attempts to enroll specifying Student B's ID in request
        // Controller must ignore the passed studentId and enroll Student A
        await studentController.Enroll(course3.Id, studentId: studentB.Id);
        var isStudentAInCourse3 = await enrollmentService.IsStudentEnrolledAsync(studentA.Id, course3.Id);
        if (!isStudentAInCourse3)
        {
            Fail("Controller Enroll Tamper Resistance", "Controller did not bind enrollment to authenticated student when unauthorized studentId was passed.");
            return;
        }
        Pass("Controller Security: Enroll action strictly overrides caller-supplied studentId with authenticated Claims identity for Student role");

        // 15. Teacher Access Control to Course Enrollments
        // Create Teacher 2 who does NOT teach Course 1
        var teacher2Email = "alanturing@aub.edu.kh";
        var teacher2 = await userManager.FindByEmailAsync(teacher2Email);
        if (teacher2 != null)
        {
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
                return;
            }
            Pass("Teacher Authorization: Teacher access to course enrollment roster strictly verified against course ownership (ForbidResult returned)");
        }

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
            return;
        }
        Pass("Admin Operations: Administrator successfully authorized to view and manage enrollment rosters for any course");

        // 17. Safe Enrollment Removal Policy - Case A: No Academic History
        // Student A is enrolled in Course 3 with no progress/submissions
        var safeRemoveResult = await enrollmentService.RemoveEnrollmentByStudentAndCourseAsync(studentA.Id, course3.Id, requestedByUserId: studentA.Id);
        if (!safeRemoveResult.IsSuccess || safeRemoveResult.Status != EnrollmentOperationStatus.Success)
        {
            Fail("Safe Enrollment Removal Case A", $"Expected Success when student has no academic history, got {safeRemoveResult.Status}");
            return;
        }

        var isStillEnrolledInC3 = await enrollmentService.IsStudentEnrolledAsync(studentA.Id, course3.Id);
        if (isStillEnrolledInC3)
        {
            Fail("Safe Enrollment Removal Case A", "Enrollment record still exists after safe removal.");
            return;
        }
        Pass("Safe Removal Policy (Case A - No Activity): Enrollment safely removed when zero learning progress or submissions exist");

        // 18. Safe Enrollment Removal Policy - Case B: Academic History Exists (Protected)
        // Add a Chapter and CourseItem to Course 1, then record CourseProgress for Student A
        var chapter1 = new Chapter { Title = "Module 1", Position = 1, CourseId = course1.Id };
        context.Chapters.Add(chapter1);
        await context.SaveChangesAsync();

        var item1 = new CourseItem { Title = "Lesson 1", Position = 1, ChapterId = chapter1.Id, Type = ItemType.Lesson };
        context.CourseItems.Add(item1);
        await context.SaveChangesAsync();

        var progress = new CourseProgress
        {
            StudentId = studentA.Id,
            CourseItemId = item1.Id,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        };
        context.CourseProgresses.Add(progress);
        await context.SaveChangesAsync();

        var hasActivity = await enrollmentService.HasStudentLearningActivityAsync(studentA.Id, course1.Id);
        if (!hasActivity)
        {
            Fail("Activity Detection", "HasStudentLearningActivityAsync returned false when CourseProgress exists.");
            return;
        }

        // Attempt to unenroll Student A from Course 1
        var blockedRemoveResult = await enrollmentService.RemoveEnrollmentByStudentAndCourseAsync(studentA.Id, course1.Id, requestedByUserId: studentA.Id);
        if (blockedRemoveResult.IsSuccess || blockedRemoveResult.Status != EnrollmentOperationStatus.BlockedByStudentActivity)
        {
            Fail("Safe Removal Policy Case B", $"Expected BlockedByStudentActivity when progress exists, got {blockedRemoveResult.Status}");
            return;
        }

        // Verify Enrollment is still in SQL Server and Progress is intact
        using var verifyScope3 = serviceProvider.CreateScope();
        var verifyContext3 = verifyScope3.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedEnrollment = await verifyContext3.Enrollments.FirstOrDefaultAsync(e => e.StudentId == studentA.Id && e.CourseId == course1.Id);
        var persistedProgress = await verifyContext3.CourseProgresses.FirstOrDefaultAsync(cp => cp.StudentId == studentA.Id && cp.CourseItemId == item1.Id);

        if (persistedEnrollment == null || persistedProgress == null)
        {
            Fail("Safe Removal Policy Case B", "Enrollment or CourseProgress was accidentally destroyed when removal should have been blocked!");
            return;
        }
        Pass("Safe Removal Policy (Case B - Academic History Exists): Unenrollment strictly blocked when student has learning activity; academic history fully preserved");

        // 19. Course Deletion Regression Test (Enrollments Block Course Deletion)
        var courseDeleteResult = await courseService.DeleteCourseAsync(course1.Id);
        if (courseDeleteResult.IsSuccess || courseDeleteResult.Status != CourseDeleteStatus.BlockedByStudentData)
        {
            Fail("Course Delete Regression", $"Expected BlockedByStudentData when enrollments and progress exist, got {courseDeleteResult.Status}");
            return;
        }
        Pass("Course Deletion Policy Regression: Active enrollments and student activity strictly protect course from accidental deletion");

        // 20. Database Cleanliness & Orphan Check
        // Clean up test progress, chapters, items, enrollments, courses
        context.CourseProgresses.Remove(progress);
        await context.SaveChangesAsync();

        // Now that progress is removed, unenrollment should succeed
        var nowSafeRemove = await enrollmentService.RemoveEnrollmentByStudentAndCourseAsync(studentA.Id, course1.Id, isAdmin: true);
        if (!nowSafeRemove.IsSuccess)
        {
            Fail("Unenroll After Activity Resolved", "Unenrollment failed after resolving academic activity.");
            return;
        }

        // Clean up remaining test enrollments
        var allTestEnrollments = await context.Enrollments
            .Where(e => e.StudentId == studentA.Id || e.StudentId == studentB.Id)
            .ToListAsync();
        context.Enrollments.RemoveRange(allTestEnrollments);
        await context.SaveChangesAsync();

        // Check for any orphan enrollments across entire database
        var orphanEnrollmentUsers = await context.Enrollments
            .Where(e => !context.Users.Any(u => u.Id == e.StudentId))
            .CountAsync();

        var orphanEnrollmentCourses = await context.Enrollments
            .Where(e => !context.Courses.Any(c => c.Id == e.CourseId))
            .CountAsync();

        if (orphanEnrollmentUsers > 0 || orphanEnrollmentCourses > 0)
        {
            Fail("Orphan Check", $"Found orphan enrollments: {orphanEnrollmentUsers} without user, {orphanEnrollmentCourses} without course.");
            return;
        }

        // Clean up test courses
        context.CourseItems.Remove(item1);
        context.Chapters.Remove(chapter1);
        context.Courses.RemoveRange(course1, course2, course3, draftCourse, archivedCourse);
        await context.SaveChangesAsync();

        Pass("Database Cleanliness & Orphan Check: 0 orphan enrollment rows; SQL Server database restored to clean state");

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"🎉  ALL {testsPassed}/{totalTests} ENROLLMENT BACKEND TESTS PASSED SUCCESSFULLY!");
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
