using System.Reflection;
using System.Security.Claims;
using AUBDigitalAcademy.Controllers;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AUBDigitalAcademy.Data;

public static class NavigationPhase2TestRunner
{
    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext)
            => $"/{actionContext.Controller}/{actionContext.Action}";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url.StartsWith('/');
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var env = sp.GetRequiredService<IHostEnvironment>();
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var userManager = sp.GetRequiredService<UserManager<User>>();
        var signInManager = sp.GetRequiredService<SignInManager<User>>();
        var enrollmentService = sp.GetRequiredService<IEnrollmentService>();
        var courseService = sp.GetRequiredService<ICourseService>();
        var curriculumService = sp.GetRequiredService<ICurriculumService>();
        var storageService = sp.GetRequiredService<IAssignmentStorageService>();
        var authService = sp.GetRequiredService<IAuthService>();
        var telegramService = sp.GetRequiredService<ITelegramService>();
        var dp = sp.GetRequiredService<IDataProtectionProvider>();

        Console.WriteLine("\n=================================================");
        Console.WriteLine("  TASK-P1-01 PHASE 2: NAVIGATION & ROLE TESTS    ");
        Console.WriteLine("=================================================\n");

        int passed = 0;
        int failed = 0;

        void AssertTrue(bool condition, string name, string detail = "")
        {
            if (condition)
            {
                Console.WriteLine($"[PASS] {name}");
                passed++;
            }
            else
            {
                Console.WriteLine($"[FAIL] {name}{(string.IsNullOrEmpty(detail) ? "" : $" — {detail}")}");
                failed++;
            }
        }

        string? createdStudentId = null;
        string? createdRolelessId = null;
        int? createdCourseWithQuizFirstId = null;
        int? createdCourseNoLessonId = null;

        try
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var major = await db.Majors.FirstAsync();
            var semester = await db.Semesters.FirstAsync();
            var teacher = await db.Users.FirstAsync();

            var student = new User
            {
                UserName = $"p2stu_{suffix}",
                UniversityId = $"P2STU_{suffix}",
                FullName = "Phase2 Student",
                IsActive = true,
                IsTelegramVerified = true
            };
            await userManager.CreateAsync(student, "Student@123");
            await userManager.AddToRoleAsync(student, "Student");
            createdStudentId = student.Id;

            var rolelessUser = new User
            {
                UserName = $"p2norole_{suffix}",
                UniversityId = $"P2NR_{suffix}",
                FullName = "Phase2 Roleless",
                IsActive = true,
                IsTelegramVerified = true
            };
            await userManager.CreateAsync(rolelessUser, "Student@123");
            createdRolelessId = rolelessUser.Id;

            // Course 1: Quiz first, Lesson second
            var course1 = new Course
            {
                Code = $"P2Q1_{suffix}",
                Title = "Quiz First Course",
                MajorId = major.Id,
                SemesterId = semester.Id,
                TeacherId = teacher.Id,
                Status = CourseStatus.Published
            };
            db.Courses.Add(course1);
            await db.SaveChangesAsync();
            createdCourseWithQuizFirstId = course1.Id;

            var ch1 = new Chapter { CourseId = course1.Id, Title = "Mod 1", Position = 1 };
            db.Chapters.Add(ch1);
            await db.SaveChangesAsync();

            var quizItem = new CourseItem { ChapterId = ch1.Id, Title = "Intro Quiz", Type = ItemType.Quiz, Position = 1 };
            var lessonItem = new CourseItem { ChapterId = ch1.Id, Title = "First Lesson", Type = ItemType.Lesson, Position = 2 };
            db.CourseItems.AddRange(quizItem, lessonItem);
            await db.SaveChangesAsync();

            db.Quizzes.Add(new Quiz { CourseItemId = quizItem.Id, TimeLimitMinutes = 10, PassingScore = 50, MaxAttempts = 1, IsPublished = true });
            db.Lessons.Add(new Lesson { CourseItemId = lessonItem.Id, ContentHtml = "<p>Hello</p>" });
            db.Enrollments.Add(new Enrollment { StudentId = student.Id, CourseId = course1.Id });
            await db.SaveChangesAsync();

            // Course 2: No lessons (Quiz only)
            var course2 = new Course
            {
                Code = $"P2NL_{suffix}",
                Title = "No Lesson Course",
                MajorId = major.Id,
                SemesterId = semester.Id,
                TeacherId = teacher.Id,
                Status = CourseStatus.Published
            };
            db.Courses.Add(course2);
            await db.SaveChangesAsync();
            createdCourseNoLessonId = course2.Id;

            var ch2 = new Chapter { CourseId = course2.Id, Title = "Mod 1", Position = 1 };
            db.Chapters.Add(ch2);
            await db.SaveChangesAsync();

            var quizOnlyItem = new CourseItem { ChapterId = ch2.Id, Title = "Only Quiz", Type = ItemType.Quiz, Position = 1 };
            db.CourseItems.Add(quizOnlyItem);
            await db.SaveChangesAsync();

            db.Quizzes.Add(new Quiz { CourseItemId = quizOnlyItem.Id, TimeLimitMinutes = 10, PassingScore = 50, MaxAttempts = 1, IsPublished = true });
            db.Enrollments.Add(new Enrollment { StudentId = student.Id, CourseId = course2.Id });
            await db.SaveChangesAsync();

            StudentController BuildStudentController(User u)
            {
                var http = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, u.Id),
                        new Claim(ClaimTypes.Name, u.UserName!),
                        new Claim(ClaimTypes.Role, "Student")
                    }, "TestAuth"))
                };
                return new StudentController(
                    enrollmentService,
                    courseService,
                    curriculumService,
                    storageService,
                    userManager,
                    db,
                    sp.GetRequiredService<ILogger<StudentController>>())
                {
                    ControllerContext = new ControllerContext { HttpContext = http },
                    TempData = new TempDataDictionary(http, new StubTempDataProvider())
                };
            }

            // 1. B-05: Student Start Learning when first item is Quiz and later item is Lesson
            var learn1 = await BuildStudentController(student).Learn(course1.Id) as RedirectToActionResult;
            AssertTrue(
                learn1 != null &&
                learn1.ActionName == nameof(StudentController.Lesson) &&
                Equals(learn1.RouteValues?["courseId"], course1.Id) &&
                Equals(learn1.RouteValues?["lessonId"], lessonItem.Id),
                "1. B-05: StudentController.Learn selects first actual Lesson (skipping earlier Quiz)");

            // 2. B-05: Student Start Learning when no Lesson exists
            var stuCtrl2 = BuildStudentController(student);
            var learn2 = await stuCtrl2.Learn(course2.Id) as RedirectToActionResult;
            AssertTrue(
                learn2 != null &&
                learn2.ActionName == nameof(StudentController.Course) &&
                Equals(learn2.RouteValues?["id"], course2.Id) &&
                stuCtrl2.TempData["WarningMessage"] != null,
                "2. B-05: StudentController.Learn redirects to Course with WarningMessage when no Lesson exists");

            var courseOverviewPath = Path.Combine(env.ContentRootPath, "Views", "Student", "CourseOverview.cshtml");
            var courseOverviewContent = File.ReadAllText(courseOverviewPath);
            AssertTrue(
                courseOverviewContent.Contains("/Student/Course/@Model.CourseId/Learn") &&
                !courseOverviewContent.Contains("/Lesson/@Model.FirstCourseItemId") &&
                courseOverviewContent.Contains("TempData[\"WarningMessage\"]") &&
                courseOverviewContent.Contains("ItemType.Lesson"),
                "2b. B-05: CourseOverview.cshtml uses /Learn, checks ItemType.Lesson, and renders WarningMessage");

            // 3 & 5. A-02: Curriculum.cshtml Teacher vs Admin back-navigation
            var curriculumPath = Path.Combine(env.ContentRootPath, "Views", "Academic", "Curriculum.cshtml");
            var curriculumContent = File.ReadAllText(curriculumPath);
            AssertTrue(
                curriculumContent.Contains("var isAdminUser = User.IsInRole(\"Admin\");") &&
                curriculumContent.Contains("isAdminUser ? \"/Academic/Course\" : \"/Teacher/Courses\"") &&
                curriculumContent.Contains("isAdminUser ? \"/Academic/Course\" : $\"/Teacher/CourseDetails/{Model.CourseId}\""),
                "3 & 5. A-02: Curriculum.cshtml routes Admin to /Academic/Course and Teacher to /Teacher/Courses & CourseDetails");

            // 4 & 5. A-02: QuizQuestions.cshtml Teacher vs Admin back-navigation
            var quizQuestionsPath = Path.Combine(env.ContentRootPath, "Views", "Academic", "QuizQuestions.cshtml");
            var quizQuestionsContent = File.ReadAllText(quizQuestionsPath);
            AssertTrue(
                quizQuestionsContent.Contains("User.IsInRole(\"Admin\") ? \"/Academic/Course\" : \"/Teacher/Courses\""),
                "4 & 5. A-02: QuizQuestions.cshtml routes Admin to /Academic/Course and Teacher to /Teacher/Courses");

            // 6 & 7. A-03: CourseStudents.cshtml role-aware navigation for Admin, Teacher, and Assistance
            var courseStudentsPath = Path.Combine(env.ContentRootPath, "Views", "Enrollment", "CourseStudents.cshtml");
            var courseStudentsContent = File.ReadAllText(courseStudentsPath);
            AssertTrue(
                !courseStudentsContent.Contains("href=\"/Admin/Courses\"") &&
                courseStudentsContent.Contains("isAdminUser ? \"/Admin/Courses\" : \"/Teacher/Courses\"") &&
                courseStudentsContent.Contains("var canManageCurriculum = isAdminUser || isTeacherUser;") &&
                courseStudentsContent.Contains("canManageCurriculum ? $\"/Academic/Curriculum/{Model.CourseId}\" : $\"/Teacher/CourseDetails/{Model.CourseId}\"") &&
                courseStudentsContent.Contains("@if (canManageCurriculum)"),
                "6 & 7. A-03: CourseStudents.cshtml scopes /Admin/Courses to Admin, /Academic/Curriculum to Admin/Teacher, and routes Assistance safely");

            // 8. A-04: Roleless user redirects safely without accessing /Admin
            var acctCtrl = new AccountController(
                signInManager,
                userManager,
                db,
                telegramService,
                authService,
                dp,
                sp.GetRequiredService<ILogger<AccountController>>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, rolelessUser.Id),
                            new Claim(ClaimTypes.Name, rolelessUser.UserName!)
                        }, "TestAuth"))
                    }
                },
                Url = new StubUrlHelper()
            };

            var loginRedirect = acctCtrl.Login() as RedirectToActionResult;
            var getRoleRedirectMethod = typeof(AccountController).GetMethod("GetRoleRedirectUrlAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var rolelessTargetUrl = await (Task<string>)getRoleRedirectMethod!.Invoke(acctCtrl, new object[] { rolelessUser })!;

            AssertTrue(
                loginRedirect != null &&
                loginRedirect.ControllerName == "Home" &&
                loginRedirect.ActionName == "Index" &&
                !rolelessTargetUrl.Contains("/Admin", StringComparison.OrdinalIgnoreCase) &&
                rolelessTargetUrl.Contains("/Home/Index", StringComparison.OrdinalIgnoreCase),
                "8. A-04: Roleless user redirects to /Home/Index instead of /Admin");
        }
        finally
        {
            if (createdCourseWithQuizFirstId.HasValue)
            {
                await db.Enrollments.Where(e => e.CourseId == createdCourseWithQuizFirstId.Value).ExecuteDeleteAsync();
                await db.Courses.Where(c => c.Id == createdCourseWithQuizFirstId.Value).ExecuteDeleteAsync();
            }
            if (createdCourseNoLessonId.HasValue)
            {
                await db.Enrollments.Where(e => e.CourseId == createdCourseNoLessonId.Value).ExecuteDeleteAsync();
                await db.Courses.Where(c => c.Id == createdCourseNoLessonId.Value).ExecuteDeleteAsync();
            }

            // Clean up any orphaned Phase 2 test records from prior interrupted runs
            var staleCourseIds = await db.Courses
                .Where(c => c.Code.StartsWith("P2Q1_") || c.Code.StartsWith("P2NL_"))
                .Select(c => c.Id)
                .ToListAsync();
            if (staleCourseIds.Count > 0)
            {
                await db.Enrollments.Where(e => staleCourseIds.Contains(e.CourseId)).ExecuteDeleteAsync();
                await db.Courses.Where(c => staleCourseIds.Contains(c.Id)).ExecuteDeleteAsync();
            }
            var staleUsers = await db.Users
                .Where(u => u.UserName != null && (u.UserName.StartsWith("p2stu_") || u.UserName.StartsWith("p2norole_")))
                .ToListAsync();
            foreach (var su in staleUsers)
            {
                await userManager.DeleteAsync(su);
            }
        }

        Console.WriteLine($"\nNavigation Phase 2 Summary: {passed} Passed, {failed} Failed\n");
        if (failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }
}
