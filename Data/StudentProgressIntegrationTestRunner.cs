using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
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

public static class StudentProgressIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("📊  AUB DIGITAL ACADEMY - STEP 4.2 COURSE PROGRESS TRACKING AUDIT");
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
        var curriculumService = rootScope.ServiceProvider.GetRequiredService<ICurriculumService>();
        var userManager = rootScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var loggerFactory = rootScope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var studentLogger = loggerFactory.CreateLogger<StudentController>();

        // Preconditions
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            Fail("Database Connectivity", "Unable to connect to SQL Server database.");
            return;
        }

        // Test Fixtures
        string testRunId = Guid.NewGuid().ToString("N").Substring(0, 8);
        string studentAEmail = $"prog-student-a-{testRunId}@aub.edu";
        string studentBEmail = $"prog-student-b-{testRunId}@aub.edu";
        string course1Code = $"CS-PRG1-{testRunId}";
        string course2Code = $"CS-PRG2-{testRunId}";
        string course3Code = $"CS-PRG3-{testRunId}";
        string course4Code = $"CS-PRG4-{testRunId}";

        User? studentA = null;
        User? studentB = null;
        Course? course1 = null; // 10 lessons
        Course? course2 = null; // 1 lesson (Student A unenrolled)
        Course? course3 = null; // 0 lessons (Empty)
        Course? course4 = null; // 1 lesson (Student A enrolled)

        List<CourseItem> course1Items = new();
        CourseItem? course2Item = null;
        CourseItem? course4Item = null;

        try
        {
            // 1. Resolve or create prerequisite data
            var teacher = await userManager.FindByEmailAsync("teacher@aub.edu")
                ?? await context.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.Contains("teacher"))
                ?? await context.Users.FirstOrDefaultAsync();

            if (teacher == null)
            {
                teacher = new User
                {
                    UserName = $"teacher-prog-{testRunId}@aub.edu",
                    Email = $"teacher-prog-{testRunId}@aub.edu",
                    FullName = "Professor Alan Turing",
                    EmailConfirmed = true,
                    IsActive = true
                };
                await userManager.CreateAsync(teacher, "Teacher@123456");
            }

            var major = await context.Majors.FirstOrDefaultAsync();
            if (major == null)
            {
                major = new Major { Code = $"MAJ-{testRunId}", Name = "Computer Science" };
                context.Majors.Add(major);
                await context.SaveChangesAsync();
            }

            var semester = await context.Semesters.FirstOrDefaultAsync();
            if (semester == null)
            {
                semester = new Semester { Code = $"SEM-{testRunId}", Name = "Spring 2026", StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddMonths(4), IsCurrent = true };
                context.Semesters.Add(semester);
                await context.SaveChangesAsync();
            }

            // 2. Create Students A and B
            studentA = new User
            {
                UserName = studentAEmail,
                Email = studentAEmail,
                FullName = "Alice Walker",
                UniversityId = $"STU-PRG-A-{testRunId}",
                EmailConfirmed = true,
                IsActive = true
            };
            var resA = await userManager.CreateAsync(studentA, "Student@123456");
            if (!resA.Succeeded) throw new Exception("Failed to create student A: " + string.Join(", ", resA.Errors.Select(e => e.Description)));
            await userManager.AddToRoleAsync(studentA, "Student");

            studentB = new User
            {
                UserName = studentBEmail,
                Email = studentBEmail,
                FullName = "Bob Vance",
                UniversityId = $"STU-PRG-B-{testRunId}",
                EmailConfirmed = true,
                IsActive = true
            };
            var resB = await userManager.CreateAsync(studentB, "Student@123456");
            if (!resB.Succeeded) throw new Exception("Failed to create student B: " + string.Join(", ", resB.Errors.Select(e => e.Description)));
            await userManager.AddToRoleAsync(studentB, "Student");

            // 3. Create Course 1 (10 Lessons across 2 Modules)
            course1 = new Course
            {
                Code = course1Code,
                Title = "Comprehensive Software Architecture",
                Description = "A complete 10-lesson course on distributed enterprise systems",
                TeacherId = teacher.Id,
                MajorId = major.Id,
                SemesterId = semester.Id,
                Status = CourseStatus.Published
            };
            context.Courses.Add(course1);
            await context.SaveChangesAsync();

            var mod1A = new Chapter { CourseId = course1.Id, Title = "Module 1: Fundamentals", Position = 1 };
            var mod1B = new Chapter { CourseId = course1.Id, Title = "Module 2: Advanced Design", Position = 2 };
            context.Chapters.AddRange(mod1A, mod1B);
            await context.SaveChangesAsync();

            for (int i = 1; i <= 5; i++)
            {
                var item = new CourseItem { ChapterId = mod1A.Id, Title = $"Module 1 Lesson {i}", Position = i, Type = ItemType.Lesson };
                context.CourseItems.Add(item);
                await context.SaveChangesAsync();
                var lesson = new Lesson { CourseItemId = item.Id, ContentHtml = $"<p>Content for M1 Lesson {i}</p>" };
                context.Lessons.Add(lesson);
                await context.SaveChangesAsync();
                course1Items.Add(item);
            }

            for (int i = 1; i <= 5; i++)
            {
                var item = new CourseItem { ChapterId = mod1B.Id, Title = $"Module 2 Lesson {i}", Position = i, Type = ItemType.Lesson };
                context.CourseItems.Add(item);
                await context.SaveChangesAsync();
                var lesson = new Lesson { CourseItemId = item.Id, ContentHtml = $"<p>Content for M2 Lesson {i}</p>" };
                context.Lessons.Add(lesson);
                await context.SaveChangesAsync();
                course1Items.Add(item);
            }

            // 4. Create Course 2 (1 Module, 1 Lesson - Student A NOT enrolled)
            course2 = new Course
            {
                Code = course2Code,
                Title = "Restricted Financial Analysis",
                Description = "Course not accessible to Student A",
                TeacherId = teacher.Id,
                MajorId = major.Id,
                SemesterId = semester.Id,
                Status = CourseStatus.Published
            };
            context.Courses.Add(course2);
            await context.SaveChangesAsync();

            var mod2 = new Chapter { CourseId = course2.Id, Title = "Module 1: Finance", Position = 1 };
            context.Chapters.Add(mod2);
            await context.SaveChangesAsync();

            course2Item = new CourseItem { ChapterId = mod2.Id, Title = "Capital Asset Pricing Model", Position = 1, Type = ItemType.Lesson };
            context.CourseItems.Add(course2Item);
            await context.SaveChangesAsync();

            var lesson2 = new Lesson { CourseItemId = course2Item.Id, ContentHtml = "<p>CAPM and Risk Content</p>" };
            context.Lessons.Add(lesson2);
            await context.SaveChangesAsync();

            // 5. Create Course 3 (0 Lessons - Empty Curriculum)
            course3 = new Course
            {
                Code = course3Code,
                Title = "Future Computing Seminar",
                Description = "Seminar with 0 lessons currently defined",
                TeacherId = teacher.Id,
                MajorId = major.Id,
                SemesterId = semester.Id,
                Status = CourseStatus.Published
            };
            context.Courses.Add(course3);
            await context.SaveChangesAsync();

            // 6. Create Course 4 (1 Module, 1 Lesson - Single Lesson Course)
            course4 = new Course
            {
                Code = course4Code,
                Title = "Fast Track Micro-Course",
                Description = "Micro course with exactly 1 lesson",
                TeacherId = teacher.Id,
                MajorId = major.Id,
                SemesterId = semester.Id,
                Status = CourseStatus.Published
            };
            context.Courses.Add(course4);
            await context.SaveChangesAsync();

            var mod4 = new Chapter { CourseId = course4.Id, Title = "Module 1: Micro Focus", Position = 1 };
            context.Chapters.Add(mod4);
            await context.SaveChangesAsync();

            course4Item = new CourseItem { ChapterId = mod4.Id, Title = "Single Core Concept Lesson", Position = 1, Type = ItemType.Lesson };
            context.CourseItems.Add(course4Item);
            await context.SaveChangesAsync();

            var lesson4 = new Lesson { CourseItemId = course4Item.Id, ContentHtml = "<p>Single lesson content</p>" };
            context.Lessons.Add(lesson4);
            await context.SaveChangesAsync();

            // 7. Establish Enrollments
            // Student A -> Course 1, Course 3, Course 4
            var enA1 = await enrollmentService.EnrollStudentAsync(studentA.Id, course1.Id, isAdmin: true);
            if (!enA1.IsSuccess) throw new Exception("Failed to enroll student A in course 1: " + enA1.Message);

            var enA3 = await enrollmentService.EnrollStudentAsync(studentA.Id, course3.Id, isAdmin: true);
            if (!enA3.IsSuccess) throw new Exception("Failed to enroll student A in course 3: " + enA3.Message);

            var enA4 = await enrollmentService.EnrollStudentAsync(studentA.Id, course4.Id, isAdmin: true);
            if (!enA4.IsSuccess) throw new Exception("Failed to enroll student A in course 4: " + enA4.Message);

            // Student B -> Course 1, Course 2
            var enB1 = await enrollmentService.EnrollStudentAsync(studentB.Id, course1.Id, isAdmin: true);
            if (!enB1.IsSuccess) throw new Exception("Failed to enroll student B in course 1: " + enB1.Message);

            var enB2 = await enrollmentService.EnrollStudentAsync(studentB.Id, course2.Id, isAdmin: true);
            if (!enB2.IsSuccess) throw new Exception("Failed to enroll student B in course 2: " + enB2.Message);

            // Helper to build authenticated StudentController
            StudentController BuildController(User studentUser, bool isAjax = false)
            {
                var ctrl = new StudentController(enrollmentService, courseService, curriculumService, userManager, context, studentLogger);
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, studentUser.Id),
                    new Claim(ClaimTypes.Name, studentUser.UserName ?? "test"),
                    new Claim(ClaimTypes.Email, studentUser.Email ?? "test@aub.edu"),
                    new Claim(ClaimTypes.Role, "Student")
                };
                var identity = new ClaimsIdentity(claims, "TestAuth");
                var principal = new ClaimsPrincipal(identity);
                var httpCtx = new DefaultHttpContext { User = principal };
                if (isAjax)
                {
                    httpCtx.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
                }
                var tempData = new TempDataDictionary(httpCtx, new TestTempDataProvider());
                ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
                ctrl.TempData = tempData;
                return ctrl;
            }

            // -------------------------------------------------------------
            // TEST 01: Enrolled student marks Lesson 1 complete -> CourseProgress created
            // -------------------------------------------------------------
            var ctrlA_01 = BuildController(studentA, isAjax: true);
            var item1 = course1Items[0];
            var actionRes01 = await ctrlA_01.CompleteLesson(course1.Id, item1.Id);

            var savedProgress01 = await context.CourseProgresses
                .AsNoTracking()
                .FirstOrDefaultAsync(cp => cp.StudentId == studentA.Id && cp.CourseItemId == item1.Id);

            if (savedProgress01 != null && savedProgress01.IsCompleted && savedProgress01.CompletedAt > DateTime.MinValue)
            {
                Pass("Enrolled student marks Lesson 1 complete -> CourseProgress created with IsCompleted=true and CompletedAt != null");
            }
            else
            {
                Fail("Test 01: Mark Lesson Complete", "CourseProgress record was not created or completed properly.");
            }

            // -------------------------------------------------------------
            // TEST 02: Enrolled student marks same Lesson 1 complete again (Idempotency)
            // -------------------------------------------------------------
            var ctrlA_02 = BuildController(studentA, isAjax: true);
            var actionRes02 = await ctrlA_02.CompleteLesson(course1.Id, item1.Id);

            var matchingCount02 = await context.CourseProgresses
                .CountAsync(cp => cp.StudentId == studentA.Id && cp.CourseItemId == item1.Id);

            if (matchingCount02 == 1)
            {
                Pass("Idempotency: Re-marking same Lesson complete returns success with exactly ONE record in database");
            }
            else
            {
                Fail("Test 02: Idempotency", $"Expected 1 CourseProgress record but found {matchingCount02}.");
            }

            // -------------------------------------------------------------
            // TEST 03: Student refreshes lesson page -> Completion remains completed
            // -------------------------------------------------------------
            var ctrlA_03 = BuildController(studentA);
            var viewRes03 = await ctrlA_03.Lesson(course1.Id, item1.Id);

            if (viewRes03 is ViewResult vr03 && vr03.Model is StudentLessonPlayerViewModel vm03 && vm03.IsCompleted)
            {
                Pass("Page Refresh: Completion state remains active and ViewModel.IsCompleted is true");
            }
            else
            {
                Fail("Test 03: Page Refresh", "Lesson player did not reflect completed status on reload.");
            }

            // -------------------------------------------------------------
            // TEST 04: Application restart / fresh DbContext scope -> Completion persists
            // -------------------------------------------------------------
            using (var freshScope = serviceProvider.CreateScope())
            {
                var freshDb = freshScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var persistedProgress = await freshDb.CourseProgresses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cp => cp.StudentId == studentA.Id && cp.CourseItemId == item1.Id);

                if (persistedProgress != null && persistedProgress.IsCompleted)
                {
                    Pass("Application Restart: Completion record persists in SQL Server across independent DbContext scopes");
                }
                else
                {
                    Fail("Test 04: Persistence", "Completion record was not found in fresh DbContext scope.");
                }
            }

            // -------------------------------------------------------------
            // TEST 05: Student Isolation (Student A has completed Lesson 1, Student B has 0 completed)
            // -------------------------------------------------------------
            var studentBProgressCount = await context.CourseProgresses
                .CountAsync(cp => cp.StudentId == studentB.Id && cp.CourseItem.Chapter.CourseId == course1.Id);

            var ctrlB_05 = BuildController(studentB);
            var viewRes05 = await ctrlB_05.Lesson(course1.Id, item1.Id);

            if (studentBProgressCount == 0 && viewRes05 is ViewResult vr05 && vr05.Model is StudentLessonPlayerViewModel vm05 && !vm05.IsCompleted && vm05.CompletedLessonsCount == 0)
            {
                Pass("Student Isolation: Student A completing lessons does not affect Student B in same course (0 completed)");
            }
            else
            {
                Fail("Test 05: Student Isolation", "Student B's progress was incorrectly affected by Student A's completions.");
            }

            // -------------------------------------------------------------
            // TEST 06: Direct spoofing rejection / Cross-course item tampering
            // -------------------------------------------------------------
            // Student A tries to submit a courseItemId that belongs to Course 2 while passing course1.Id
            var ctrlA_06 = BuildController(studentA, isAjax: true);
            var actionRes06 = await ctrlA_06.CompleteLesson(course1.Id, course2Item!.Id);

            var spoofedRecord = await context.CourseProgresses
                .FirstOrDefaultAsync(cp => cp.StudentId == studentA.Id && cp.CourseItemId == course2Item.Id);

            if (spoofedRecord == null)
            {
                Pass("Anti-Spoofing: Cross-course item submission is rejected, no invalid progress recorded");
            }
            else
            {
                Fail("Test 06: Anti-Spoofing", "Course item belonging to a different course was incorrectly marked complete.");
            }

            // -------------------------------------------------------------
            // TEST 07: Unenrolled course rejection (403 Forbidden)
            // -------------------------------------------------------------
            // Student A is NOT enrolled in Course 2
            var ctrlA_07 = BuildController(studentA);
            var actionRes07 = await ctrlA_07.CompleteLesson(course2.Id, course2Item.Id);

            if (actionRes07 is ForbidResult)
            {
                Pass("Enrollment Authorization: Student completing lesson in unenrolled course is rejected with 403 Forbidden");
            }
            else
            {
                Fail("Test 07: Unenrolled Course", $"Expected ForbidResult but got {actionRes07.GetType().Name}.");
            }

            // -------------------------------------------------------------
            // TEST 08: Progress calculation: 10 lessons, 4 completed -> 40%
            // -------------------------------------------------------------
            // Mark items 2, 3, 4 complete for Student A in Course 1
            await curriculumService.MarkLessonCompleteAsync(studentA.Id, course1.Id, course1Items[1].Id);
            await curriculumService.MarkLessonCompleteAsync(studentA.Id, course1.Id, course1Items[2].Id);
            await curriculumService.MarkLessonCompleteAsync(studentA.Id, course1.Id, course1Items[3].Id);

            var ctrlA_08 = BuildController(studentA);
            var overviewRes08 = await ctrlA_08.Course(course1.Id);

            if (overviewRes08 is ViewResult vr08 && vr08.Model is StudentCourseOverviewViewModel ovVm08 &&
                ovVm08.TotalLessons == 10 && ovVm08.CompletedLessons == 4 && ovVm08.ProgressPercentage == 40)
            {
                Pass("Progress Calculation: 4 of 10 lessons completed yields exactly 40% progress");
            }
            else
            {
                Fail("Test 08: Progress Calculation", "Progress percentage mismatch for 4/10 lessons.");
            }

            // -------------------------------------------------------------
            // TEST 09: Edge Case: Course with 0 lessons -> 0% without divide-by-zero error
            // -------------------------------------------------------------
            var ctrlA_09 = BuildController(studentA);
            var overviewRes09 = await ctrlA_09.Course(course3.Id);

            if (overviewRes09 is ViewResult vr09 && vr09.Model is StudentCourseOverviewViewModel ovVm09 &&
                ovVm09.TotalLessons == 0 && ovVm09.CompletedLessons == 0 && ovVm09.ProgressPercentage == 0)
            {
                Pass("Edge Case (0 Lessons): Course with 0 lessons calculates 0% progress cleanly without division by zero");
            }
            else
            {
                Fail("Test 09: 0 Lessons Edge Case", "Failed to calculate 0% on course with 0 lessons.");
            }

            // -------------------------------------------------------------
            // TEST 10: Edge Case: Course with 1 lesson, completed -> 100%
            // -------------------------------------------------------------
            var ctrlA_10 = BuildController(studentA, isAjax: true);
            await ctrlA_10.CompleteLesson(course4.Id, course4Item!.Id);

            var overviewRes10 = await ctrlA_10.Course(course4.Id);
            if (overviewRes10 is ViewResult vr10 && vr10.Model is StudentCourseOverviewViewModel ovVm10 &&
                ovVm10.TotalLessons == 1 && ovVm10.CompletedLessons == 1 && ovVm10.ProgressPercentage == 100)
            {
                Pass("Edge Case (1 Lesson Complete): Course with 1 completed lesson yields exactly 100% progress");
            }
            else
            {
                Fail("Test 10: 1 Lesson Edge Case", "Failed to calculate 100% on course with 1/1 lesson completed.");
            }

            // -------------------------------------------------------------
            // TEST 11: Completed lessons display in curriculum navigation
            // -------------------------------------------------------------
            var ctrlA_11 = BuildController(studentA);
            var playerRes11 = await ctrlA_11.Lesson(course1.Id, course1Items[0].Id);

            if (playerRes11 is ViewResult vr11 && vr11.Model is StudentLessonPlayerViewModel playerVm11)
            {
                var allLessons = playerVm11.Modules.SelectMany(m => m.Lessons).ToList();
                var completedInNav = allLessons.Count(l => l.IsCompleted);

                if (completedInNav == 4 && playerVm11.CompletedLessonsCount == 4 && playerVm11.CourseProgressPercentage == 40)
                {
                    Pass("Curriculum Navigation: Sidebar navigation displays 4 completed lessons with 40% progress bar");
                }
                else
                {
                    Fail("Test 11: Curriculum Navigation", $"Expected 4 completed lessons in sidebar navigation, found {completedInNav}.");
                }
            }
            else
            {
                Fail("Test 11: Curriculum Navigation", "Failed to retrieve student player ViewModel.");
            }

            // -------------------------------------------------------------
            // TEST 12: My Courses page displays correct progress percentage
            // -------------------------------------------------------------
            var ctrlA_12 = BuildController(studentA);
            var coursesRes12 = await ctrlA_12.Courses();

            if (coursesRes12 is ViewResult vr12 && vr12.Model is StudentMyCoursesViewModel coursesVm12)
            {
                var c1Card = coursesVm12.Courses.FirstOrDefault(c => c.CourseId == course1.Id);
                var c4Card = coursesVm12.Courses.FirstOrDefault(c => c.CourseId == course4.Id);
                var c3Card = coursesVm12.Courses.FirstOrDefault(c => c.CourseId == course3.Id);

                bool c1Ok = c1Card != null && c1Card.TotalLessons == 10 && c1Card.CompletedLessons == 4 && c1Card.ProgressPercentage == 40;
                bool c4Ok = c4Card != null && c4Card.TotalLessons == 1 && c4Card.CompletedLessons == 1 && c4Card.ProgressPercentage == 100;
                bool c3Ok = c3Card != null && c3Card.TotalLessons == 0 && c3Card.CompletedLessons == 0 && c3Card.ProgressPercentage == 0;

                if (c1Ok && c4Ok && c3Ok)
                {
                    Pass("My Courses: All enrolled course cards render accurate real-time progress percentages (40%, 100%, 0%)");
                }
                else
                {
                    Fail("Test 12: My Courses Progress", $"Course card progress values mismatch: c1Ok={c1Ok}, c4Ok={c4Ok}, c3Ok={c3Ok}.");
                }
            }
            else
            {
                Fail("Test 12: My Courses Progress", "Failed to retrieve StudentMyCoursesViewModel.");
            }

            // -------------------------------------------------------------
            // TEST 13: Student Dashboard displays correct total completed lessons count
            // -------------------------------------------------------------
            var ctrlA_13 = BuildController(studentA);
            var dashRes13 = await ctrlA_13.Dashboard();

            if (dashRes13 is ViewResult vr13 && vr13.Model is StudentDashboardViewModel dashVm13)
            {
                // Total completed across all courses = 4 (Course 1) + 1 (Course 4) = 5
                if (dashVm13.CompletedLessonsCount == 5)
                {
                    Pass("Student Dashboard: Completed Lessons KPI stat card displays 5 (real aggregate across all courses)");
                }
                else
                {
                    Fail("Test 13: Student Dashboard KPI", $"Expected 5 total completed lessons, but got {dashVm13.CompletedLessonsCount}.");
                }
            }
            else
            {
                Fail("Test 13: Student Dashboard KPI", "Failed to retrieve StudentDashboardViewModel.");
            }

            // -------------------------------------------------------------
            // SUCCESS SUMMARY
            // -------------------------------------------------------------
            Console.WriteLine("\n=================================================================");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"🎉  STEP 4.2 COURSE PROGRESS AUDIT COMPLETE: {testsPassed}/{totalTests} TESTS PASSED");
            Console.ResetColor();
            Console.WriteLine("=================================================================\n");
        }
        catch (Exception ex)
        {
            Fail("Unhandled Test Exception", ex.ToString());
        }
        finally
        {
            // Cleanup test fixtures
            try
            {
                var allCourseIds = new[] { course1?.Id ?? 0, course2?.Id ?? 0, course3?.Id ?? 0, course4?.Id ?? 0 }.Where(id => id > 0).ToList();

                var progresses = context.CourseProgresses.Where(cp => allCourseIds.Contains(cp.CourseItem.Chapter.CourseId));
                context.CourseProgresses.RemoveRange(progresses);

                var lessons = context.Lessons.Where(l => allCourseIds.Contains(l.CourseItem.Chapter.CourseId));
                context.Lessons.RemoveRange(lessons);

                var items = context.CourseItems.Where(ci => allCourseIds.Contains(ci.Chapter.CourseId));
                context.CourseItems.RemoveRange(items);

                var chapters = context.Chapters.Where(ch => allCourseIds.Contains(ch.CourseId));
                context.Chapters.RemoveRange(chapters);

                var enrollments = context.Enrollments.Where(e => allCourseIds.Contains(e.CourseId));
                context.Enrollments.RemoveRange(enrollments);

                var courses = context.Courses.Where(c => allCourseIds.Contains(c.Id));
                context.Courses.RemoveRange(courses);

                await context.SaveChangesAsync();

                if (studentA != null) await userManager.DeleteAsync(studentA);
                if (studentB != null) await userManager.DeleteAsync(studentB);
            }
            catch
            {
                // Best effort cleanup
            }
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
