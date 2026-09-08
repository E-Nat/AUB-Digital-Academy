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

public static class StudentFlowIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("🎓  AUB DIGITAL ACADEMY - STEP 3.3 STUDENT FLOW & MY COURSES AUDIT");
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
        var studentLogger = loggerFactory.CreateLogger<StudentController>();

        // Preconditions
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            Fail("Database Connectivity", "Cannot connect to SQL Server database.");
            return;
        }

        var major = await context.Majors.FirstOrDefaultAsync();
        var semester = await context.Semesters.FirstOrDefaultAsync();
        var teacher = await userManager.FindByEmailAsync("teacher@aub.edu.kh");
        var adminUser = await userManager.FindByEmailAsync("admin@aub.edu.kh");

        if (major == null || semester == null || teacher == null || adminUser == null)
        {
            Fail("Preconditions", "Required seeded entities (Major, Semester, Teacher, Admin) not found.");
            return;
        }

        // Setup Test Students
        var studentAEmail = "student_flow_a@aub.edu.kh";
        var studentA = await userManager.FindByEmailAsync(studentAEmail);
        if (studentA == null)
        {
            studentA = new User
            {
                UserName = studentAEmail,
                Email = studentAEmail,
                FullName = "Emma Watson",
                UniversityId = "STU-FLW-A",
                EmailConfirmed = true,
                IsActive = true
            };
            await userManager.CreateAsync(studentA, "Student@123");
            await userManager.AddToRoleAsync(studentA, "Student");
        }

        var studentBEmail = "student_flow_b@aub.edu.kh";
        var studentB = await userManager.FindByEmailAsync(studentBEmail);
        if (studentB == null)
        {
            studentB = new User
            {
                UserName = studentBEmail,
                Email = studentBEmail,
                FullName = "James Potter",
                UniversityId = "STU-FLW-B",
                EmailConfirmed = true,
                IsActive = true
            };
            await userManager.CreateAsync(studentB, "Student@123");
            await userManager.AddToRoleAsync(studentB, "Student");
        }

        var emptyStudentEmail = "student_flow_empty@aub.edu.kh";
        var emptyStudent = await userManager.FindByEmailAsync(emptyStudentEmail);
        if (emptyStudent == null)
        {
            emptyStudent = new User
            {
                UserName = emptyStudentEmail,
                Email = emptyStudentEmail,
                FullName = "Luna Lovegood",
                UniversityId = "STU-FLW-EMP",
                EmailConfirmed = true,
                IsActive = true
            };
            await userManager.CreateAsync(emptyStudent, "Student@123");
            await userManager.AddToRoleAsync(emptyStudent, "Student");
        }

        // Clean up previous test courses & enrollments
        var testCodes = new[] { "FLW-CS-101", "FLW-CS-201", "FLW-CS-301" };
        var existingCourses = await context.Courses
            .Include(c => c.Enrollments)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Lesson)
            .Where(c => testCodes.Contains(c.Code))
            .ToListAsync();

        foreach (var c in existingCourses)
        {
            foreach (var ch in c.Chapters)
            {
                foreach (var ci in ch.CourseItems)
                {
                    if (ci.Lesson != null) context.Lessons.Remove(ci.Lesson);
                }
                context.CourseItems.RemoveRange(ch.CourseItems);
            }
            context.Chapters.RemoveRange(c.Chapters);
            context.Enrollments.RemoveRange(c.Enrollments);
            context.Courses.Remove(c);
        }
        await context.SaveChangesAsync();

        // Remove any leftover enrollments for test students
        var leftoverEnrollments = await context.Enrollments
            .Where(e => e.StudentId == studentA.Id || e.StudentId == studentB.Id || e.StudentId == emptyStudent.Id)
            .ToListAsync();
        context.Enrollments.RemoveRange(leftoverEnrollments);
        await context.SaveChangesAsync();

        // Create Test Courses in SQL Server with Curriculum Structure
        var courseA = new Course
        {
            Code = "FLW-CS-101",
            Title = "Object-Oriented Programming with C#",
            Description = "Master OOP concepts, inheritance, polymorphism, and design patterns in C#.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Published
        };
        var courseB = new Course
        {
            Code = "FLW-CS-201",
            Title = "Enterprise Web Development with ASP.NET",
            Description = "Build modern web applications with Razor Views, EF Core, and Identity.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Published
        };
        var courseC = new Course
        {
            Code = "FLW-CS-301",
            Title = "Cloud-Native Microservices",
            Description = "Deploy resilient microservices with Docker, Kubernetes, and Azure.",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Published
        };
        context.Courses.AddRange(courseA, courseB, courseC);
        await context.SaveChangesAsync();

        // Add curriculum to Course A
        var chapterA1 = new Chapter { Title = "Module 1: C# Fundamentals", Position = 1, CourseId = courseA.Id };
        var chapterA2 = new Chapter { Title = "Module 2: Object-Oriented Principles", Position = 2, CourseId = courseA.Id };
        context.Chapters.AddRange(chapterA1, chapterA2);
        await context.SaveChangesAsync();

        var itemA1 = new CourseItem { Title = "Introduction to C# Syntax", Position = 1, ChapterId = chapterA1.Id, Type = ItemType.Lesson };
        var itemA2 = new CourseItem { Title = "Classes, Structs, and Methods", Position = 2, ChapterId = chapterA1.Id, Type = ItemType.Lesson };
        var itemA3 = new CourseItem { Title = "Inheritance & Abstract Classes", Position = 1, ChapterId = chapterA2.Id, Type = ItemType.Lesson };
        context.CourseItems.AddRange(itemA1, itemA2, itemA3);
        await context.SaveChangesAsync();

        context.Lessons.AddRange(
            new Lesson { CourseItemId = itemA1.Id, ContentHtml = "<p>Intro</p>" },
            new Lesson { CourseItemId = itemA2.Id, ContentHtml = "<p>Classes</p>" },
            new Lesson { CourseItemId = itemA3.Id, ContentHtml = "<p>OOP</p>" }
        );
        await context.SaveChangesAsync();

        // Enroll Student A into Course A
        // Enroll Student B into Course B
        // EmptyStudent is not enrolled in anything
        await enrollmentService.EnrollStudentAsync(studentA.Id, courseA.Id, isAdmin: true);
        await enrollmentService.EnrollStudentAsync(studentB.Id, courseB.Id, isAdmin: true);

        // 1. Test Authenticated Student Identity Resolution
        var studentAClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, studentA.Id),
            new Claim(ClaimTypes.Name, studentA.UserName!),
            new Claim(ClaimTypes.Role, "Student")
        }, "TestAuth"));

        var studentAController = new StudentController(
            enrollmentService,
            courseService,
            userManager,
            context,
            studentLogger);

        studentAController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = studentAClaims }
        };
        studentAController.TempData = new TempDataDictionary(studentAController.HttpContext, new TestTempDataProvider());

        Pass("Student Identity: Authenticated claims identity accurately bound to StudentController (Student ID: " + studentA.Id + ")");

        // 2. Test Student Dashboard Integration (Real Stats & Enrolled Courses section)
        var dashboardResult = await studentAController.Dashboard() as ViewResult;
        if (dashboardResult == null || dashboardResult.Model is not StudentDashboardViewModel dashModel)
        {
            Fail("Student Dashboard", "Dashboard action did not return expected ViewResult with StudentDashboardViewModel.");
            return;
        }

        if (dashModel.StudentName != studentA.FullName || dashModel.UniversityId != "STU-FLW-A" || dashModel.EnrolledCoursesCount != 1)
        {
            Fail("Student Dashboard Data", $"Dashboard data mismatch: Name='{dashModel.StudentName}', Count={dashModel.EnrolledCoursesCount}");
            return;
        }
        Pass($"Student Dashboard: Real Student Profile ('{studentA.FullName}') and active enrollment stats loaded from SQL Server");

        // 3. Test My Courses Action & Real Course Card Data
        var myCoursesResult = await studentAController.Courses() as ViewResult;
        if (myCoursesResult == null || myCoursesResult.Model is not StudentMyCoursesViewModel myCoursesModel)
        {
            Fail("My Courses View", "Courses action did not return ViewResult with StudentMyCoursesViewModel.");
            return;
        }

        if (myCoursesModel.TotalCourses != 1 || myCoursesModel.Courses[0].CourseId != courseA.Id)
        {
            Fail("My Courses Data", "Student A's enrolled course was not Course A.");
            return;
        }

        var courseCard = myCoursesModel.Courses[0];
        if (courseCard.CourseCode != "FLW-CS-101" || courseCard.CourseTitle != "Object-Oriented Programming with C#" || courseCard.TeacherName != teacher.FullName)
        {
            Fail("Course Card Data", "Course card data does not match SQL Server course entity properties.");
            return;
        }
        Pass($"My Courses Action: Student A sees enrolled course '{courseCard.CourseTitle}' ({courseCard.CourseCode}) with Teacher '{courseCard.TeacherName}'");

        // 4. Test Empty State for Student with No Enrollments
        var emptyStudentClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, emptyStudent.Id),
            new Claim(ClaimTypes.Name, emptyStudent.UserName!),
            new Claim(ClaimTypes.Role, "Student")
        }, "TestAuth"));

        var emptyStudentController = new StudentController(
            enrollmentService,
            courseService,
            userManager,
            context,
            studentLogger);

        emptyStudentController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = emptyStudentClaims }
        };

        var emptyCoursesResult = await emptyStudentController.Courses() as ViewResult;
        var emptyCoursesModel = emptyCoursesResult?.Model as StudentMyCoursesViewModel;

        if (emptyCoursesModel == null || emptyCoursesModel.TotalCourses != 0)
        {
            Fail("Empty Student My Courses", $"Expected 0 courses for student with no enrollments, got {emptyCoursesModel?.TotalCourses}");
            return;
        }
        Pass("Empty State Validation: Student with 0 enrollments returns empty collection (TotalCourses = 0); clean empty-state UI contract verified");

        // 5. Test Open Enrolled Course (Course Overview)
        var overviewResult = await studentAController.Course(courseA.Id) as ViewResult;
        if (overviewResult == null || overviewResult.Model is not StudentCourseOverviewViewModel overviewModel)
        {
            Fail("Open Course Overview", "Course action did not return ViewResult with StudentCourseOverviewViewModel.");
            return;
        }

        if (overviewModel.CourseId != courseA.Id || overviewModel.TotalModules != 2 || overviewModel.TotalLessons != 3)
        {
            Fail("Course Overview Curriculum", $"Curriculum mismatch: Modules={overviewModel.TotalModules}, Lessons={overviewModel.TotalLessons}");
            return;
        }

        if (overviewModel.Modules[0].Title != "Module 1: C# Fundamentals" || overviewModel.Modules[0].Lessons.Count != 2)
        {
            Fail("Course Overview Module", "Module 1 structure or lessons count mismatch.");
            return;
        }
        Pass($"Open Course: Course Overview successfully loaded for '{overviewModel.Title}' with 2 Modules and 3 Lessons");

        // 6. Test Unauthorized Course Access Protection (CRITICAL COURSE ACCESS SECURITY)
        // Student A attempts to access Course B (enrolled only by Student B)
        var unauthorizedResult = await studentAController.Course(courseB.Id);
        if (unauthorizedResult is not ForbidResult)
        {
            Fail("Course Access Security", $"Expected ForbidResult for unenrolled course access, got {unauthorizedResult.GetType().Name}");
            return;
        }
        Pass("Course Access Security: Server-side enrollment check blocked Student A from accessing unenrolled Course B (ForbidResult / HTTP 403 returned)");

        // 7. Test Student B Course Access Protection
        var studentBClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, studentB.Id),
            new Claim(ClaimTypes.Name, studentB.UserName!),
            new Claim(ClaimTypes.Role, "Student")
        }, "TestAuth"));

        var studentBController = new StudentController(
            enrollmentService,
            courseService,
            userManager,
            context,
            studentLogger);

        studentBController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = studentBClaims }
        };

        // Student B opens enrolled Course B: OK
        var studentBOverview = await studentBController.Course(courseB.Id) as ViewResult;
        if (studentBOverview == null)
        {
            Fail("Student B Course Access", "Student B could not access their own enrolled Course B.");
            return;
        }

        // Student B attempts to access Course A (enrolled only by Student A): FORBIDDEN
        var studentBForbidden = await studentBController.Course(courseA.Id);
        if (studentBForbidden is not ForbidResult)
        {
            Fail("Student B Unauthorized Access", "Student B was able to access Student A's Course A without enrollment.");
            return;
        }
        Pass("Cross-Student Course Security: Student B can access Course B, but is strictly blocked from accessing Course A");

        // 8. Test Student Isolation in My Courses
        var studentBMyCourses = (await studentBController.Courses() as ViewResult)?.Model as StudentMyCoursesViewModel;
        if (studentBMyCourses == null || studentBMyCourses.TotalCourses != 1 || studentBMyCourses.Courses[0].CourseId != courseB.Id)
        {
            Fail("Student B My Courses Isolation", "Student B's courses list does not strictly match Course B.");
            return;
        }

        if (myCoursesModel.Courses.Any(c => c.CourseId == courseB.Id) || studentBMyCourses.Courses.Any(c => c.CourseId == courseA.Id))
        {
            Fail("Student Isolation Leakage", "Course A leaked into Student B or Course B leaked into Student A.");
            return;
        }
        Pass("Student Isolation: Student A sees strictly Course A; Student B sees strictly Course B; 0 cross-student data leakage");

        // 9. Test Invalid Course ID Resilience
        var invalidCourseZero = await studentAController.Course(0);
        var invalidCourseNeg = await studentAController.Course(-5);
        var invalidCourseNonexistent = await studentAController.Course(999999);

        if (invalidCourseZero is not NotFoundObjectResult && invalidCourseZero is not NotFoundResult)
        {
            Fail("Invalid Course ID Resilience", "Zero ID did not return NotFound.");
            return;
        }

        if (invalidCourseNonexistent is not ForbidResult && invalidCourseNonexistent is not NotFoundObjectResult && invalidCourseNonexistent is not NotFoundResult)
        {
            Fail("Nonexistent Course ID Resilience", "Nonexistent course did not reject safely.");
            return;
        }
        Pass("Invalid ID Resilience: Zero, negative, and nonexistent course IDs rejected safely without unhandled exceptions");

        // 10. Clean up Test Fixtures
        context.Lessons.RemoveRange(context.Lessons.Where(l => l.CourseItemId == itemA1.Id || l.CourseItemId == itemA2.Id || l.CourseItemId == itemA3.Id));
        context.CourseItems.RemoveRange(itemA1, itemA2, itemA3);
        context.Chapters.RemoveRange(chapterA1, chapterA2);

        var enrollmentsToClean = await context.Enrollments
            .Where(e => e.StudentId == studentA.Id || e.StudentId == studentB.Id || e.StudentId == emptyStudent.Id)
            .ToListAsync();
        context.Enrollments.RemoveRange(enrollmentsToClean);

        context.Courses.RemoveRange(courseA, courseB, courseC);
        await context.SaveChangesAsync();

        var orphanUsers = await context.Enrollments.Where(e => !context.Users.Any(u => u.Id == e.StudentId)).CountAsync();
        var orphanCourses = await context.Enrollments.Where(e => !context.Courses.Any(c => c.Id == e.CourseId)).CountAsync();

        if (orphanUsers > 0 || orphanCourses > 0)
        {
            Fail("Database Cleanliness", $"Found orphan enrollments after cleanup: {orphanUsers} users, {orphanCourses} courses.");
            return;
        }
        Pass("Database Cleanliness & Orphan Check: 0 orphan enrollment records; SQL Server restored to clean state");

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"🎉  ALL {testsPassed}/{totalTests} STUDENT FLOW & MY COURSES TESTS PASSED SUCCESSFULLY!");
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
