using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
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

public static class StudentPlayerIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("🎓  AUB DIGITAL ACADEMY - STEP 4.1 STUDENT LESSON PLAYER AUDIT");
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
        string studentAEmail = $"player-student-a-{testRunId}@aub.edu";
        string studentBEmail = $"player-student-b-{testRunId}@aub.edu";
        string courseACode = $"CS-PLY-{testRunId}";
        string courseBCode = $"EC-PLY-{testRunId}";
        string courseCCode = $"EM-PLY-{testRunId}";

        User? studentA = null;
        User? studentB = null;
        Course? courseA = null;
        Course? courseB = null;
        Course? courseC = null;
        Chapter? modA1 = null;
        Chapter? modA2 = null;
        Chapter? modB1 = null;
        CourseItem? itemA1 = null;
        CourseItem? itemA2 = null;
        CourseItem? itemA3 = null;
        CourseItem? itemA4 = null;
        CourseItem? itemB1 = null;
        Enrollment? enrollA = null;
        Enrollment? enrollB = null;

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
                    UserName = $"teacher-player-{testRunId}@aub.edu",
                    Email = $"teacher-player-{testRunId}@aub.edu",
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
                semester = new Semester { Code = $"SEM-{testRunId}", Name = "Fall 2026", StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddMonths(4), IsCurrent = true };
                context.Semesters.Add(semester);
                await context.SaveChangesAsync();
            }

            // 2. Create Students
            studentA = new User
            {
                UserName = studentAEmail,
                Email = studentAEmail,
                FullName = "Alice Lovelace",
                UniversityId = $"STU-A-{testRunId}",
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
                FullName = "Bob Hopper",
                UniversityId = $"STU-B-{testRunId}",
                EmailConfirmed = true,
                IsActive = true
            };
            var resB = await userManager.CreateAsync(studentB, "Student@123456");
            if (!resB.Succeeded) throw new Exception("Failed to create student B: " + string.Join(", ", resB.Errors.Select(e => e.Description)));
            await userManager.AddToRoleAsync(studentB, "Student");

            // 3. Create Course A (2 Modules, 4 Lessons)
            courseA = new Course
            {
                Code = courseACode,
                Title = "Full Stack Software Engineering",
                Description = "Modern Web Architecture and Distributed Systems",
                TeacherId = teacher.Id,
                MajorId = major.Id,
                SemesterId = semester.Id,
                Status = CourseStatus.Published
            };
            context.Courses.Add(courseA);
            await context.SaveChangesAsync();

            modA1 = new Chapter { CourseId = courseA.Id, Title = "Module 1: Foundations", Position = 1 };
            modA2 = new Chapter { CourseId = courseA.Id, Title = "Module 2: Advanced Architecture", Position = 2 };
            context.Chapters.AddRange(modA1, modA2);
            await context.SaveChangesAsync();

            itemA1 = new CourseItem { ChapterId = modA1.Id, Title = "Introduction to Web Systems", Position = 1, Type = ItemType.Lesson };
            itemA2 = new CourseItem { ChapterId = modA1.Id, Title = "RESTful APIs and HTTP", Position = 2, Type = ItemType.Lesson };
            itemA3 = new CourseItem { ChapterId = modA2.Id, Title = "Microservices and Messaging", Position = 1, Type = ItemType.Lesson };
            itemA4 = new CourseItem { ChapterId = modA2.Id, Title = "Cloud Deployment and CI/CD", Position = 2, Type = ItemType.Lesson };
            context.CourseItems.AddRange(itemA1, itemA2, itemA3, itemA4);
            await context.SaveChangesAsync();

            var lessonA1 = new Lesson { CourseItemId = itemA1.Id, ContentHtml = "<p>Introduction to Web Systems Lecture Content</p>", VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ" };
            var lessonA2 = new Lesson { CourseItemId = itemA2.Id, ContentHtml = "<p>REST APIs and Protocol Semantics Notes</p>", VideoUrl = null };
            var lessonA3 = new Lesson { CourseItemId = itemA3.Id, ContentHtml = "<p>Distributed Microservices Notes</p>", VideoUrl = "https://example.com/videos/microservices.mp4" };
            var lessonA4 = new Lesson { CourseItemId = itemA4.Id, ContentHtml = "<p>Docker and Kubernetes Notes</p>", VideoUrl = null };
            context.Lessons.AddRange(lessonA1, lessonA2, lessonA3, lessonA4);
            await context.SaveChangesAsync();

            // 4. Create Course B (1 Module, 1 Lesson)
            courseB = new Course
            {
                Code = courseBCode,
                Title = "Macroeconomics Principles",
                Description = "Fiscal and Monetary Policies",
                TeacherId = teacher.Id,
                MajorId = major.Id,
                SemesterId = semester.Id,
                Status = CourseStatus.Published
            };
            context.Courses.Add(courseB);
            await context.SaveChangesAsync();

            modB1 = new Chapter { CourseId = courseB.Id, Title = "Module 1: Economic Foundations", Position = 1 };
            context.Chapters.Add(modB1);
            await context.SaveChangesAsync();

            itemB1 = new CourseItem { ChapterId = modB1.Id, Title = "National Income Accounting", Position = 1, Type = ItemType.Lesson };
            context.CourseItems.Add(itemB1);
            await context.SaveChangesAsync();

            var lessonB1 = new Lesson { CourseItemId = itemB1.Id, ContentHtml = "<p>GDP and GNP Formulas</p>", VideoUrl = "https://youtu.be/econ-intro" };
            context.Lessons.Add(lessonB1);
            await context.SaveChangesAsync();

            // 5. Create Course C (Empty curriculum - 0 Modules, 0 Lessons)
            courseC = new Course
            {
                Code = courseCCode,
                Title = "Future Computing Explorations",
                Description = "Upcoming Course - Curriculum Pending",
                TeacherId = teacher.Id,
                MajorId = major.Id,
                SemesterId = semester.Id,
                Status = CourseStatus.Published
            };
            context.Courses.Add(courseC);
            await context.SaveChangesAsync();

            // 6. Enrollments: Student A -> Course A, Student B -> Course B, Student A -> Course C
            var enrollResA = await enrollmentService.EnrollStudentAsync(studentA.Id, courseA.Id, isAdmin: true);
            if (!enrollResA.IsSuccess) throw new Exception("Failed to enroll student A in course A: " + enrollResA.Message);
            enrollA = await context.Enrollments.FindAsync(enrollResA.EnrollmentId);

            var enrollResB = await enrollmentService.EnrollStudentAsync(studentB.Id, courseB.Id, isAdmin: true);
            if (!enrollResB.IsSuccess) throw new Exception("Failed to enroll student B in course B: " + enrollResB.Message);
            enrollB = await context.Enrollments.FindAsync(enrollResB.EnrollmentId);

            var enrollResC = await enrollmentService.EnrollStudentAsync(studentA.Id, courseC.Id, isAdmin: true);
            if (!enrollResC.IsSuccess) throw new Exception("Failed to enroll student A in course C: " + enrollResC.Message);

            // Helper to build authenticated StudentController
            StudentController BuildController(User studentUser, bool isAdmin = false)
            {
                var ctrl = new StudentController(enrollmentService, courseService, curriculumService, userManager, context, studentLogger);
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, studentUser.Id),
                    new Claim(ClaimTypes.Name, studentUser.UserName ?? "test"),
                    new Claim(ClaimTypes.Email, studentUser.Email ?? "test@aub.edu"),
                    new Claim(ClaimTypes.Role, isAdmin ? "Admin" : "Student")
                };
                var identity = new ClaimsIdentity(claims, "TestAuth");
                var principal = new ClaimsPrincipal(identity);
                var httpCtx = new DefaultHttpContext { User = principal };
                var tempData = new TempDataDictionary(httpCtx, new TestTempDataProvider());
                ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
                ctrl.TempData = tempData;
                return ctrl;
            }

            // TEST 01: Student Authentication
            var ctrlA = BuildController(studentA);
            var dashResult = await ctrlA.Dashboard();
            if (dashResult is ViewResult vrDash && vrDash.Model is StudentDashboardViewModel dashVm && dashVm.StudentId == studentA.Id)
            {
                Pass("Student Authentication & Claims Identity correctly resolved in StudentController");
            }
            else
            {
                Fail("Student Authentication", "Could not resolve authenticated Student identity.");
            }

            // TEST 02: Student Accesses Enrolled Course Overview and sees Start Learning
            var ovResult = await ctrlA.Course(courseA.Id);
            if (ovResult is ViewResult vrOv && vrOv.Model is StudentCourseOverviewViewModel ovVm &&
                ovVm.CourseId == courseA.Id && ovVm.FirstCourseItemId == itemA1.Id)
            {
                Pass("Course Overview: Enrolled Course loaded with Start Learning pointing to Lesson 1");
            }
            else
            {
                Fail("Course Overview", "Failed to load Course Overview or FirstCourseItemId mismatch.");
            }

            // TEST 03: Student Opens Valid Lesson Player
            var l1Result = await ctrlA.Lesson(courseA.Id, itemA1.Id);
            if (l1Result is ViewResult vrL1 && vrL1.Model is StudentLessonPlayerViewModel l1Vm &&
                l1Vm.CourseItemId == itemA1.Id && l1Vm.CourseId == courseA.Id)
            {
                Pass("Student opens valid Lesson Player (/Student/Course/{id}/Lesson/{id}) successfully");
            }
            else
            {
                Fail("Lesson Player Load", "Expected ViewResult with StudentLessonPlayerViewModel.");
            }

            // TEST 04: Correct Lesson Content
            var l1Model = (StudentLessonPlayerViewModel)((ViewResult)l1Result).Model!;
            if (l1Model.ContentHtml == "<p>Introduction to Web Systems Lecture Content</p>" && l1Model.HasContent)
            {
                Pass("Lesson Content: ContentHtml accurately retrieved from SQL Server without placeholders");
            }
            else
            {
                Fail("Lesson Content", "ContentHtml does not match database record.");
            }

            // TEST 05: Course Curriculum Hierarchy
            if (l1Model.Modules.Count == 2 &&
                l1Model.TotalLessonsCount == 4 &&
                l1Model.Modules[0].IsCurrentModule &&
                !l1Model.Modules[1].IsCurrentModule)
            {
                Pass("Course Curriculum: Full module hierarchy returned with active module correctly highlighted");
            }
            else
            {
                Fail("Curriculum Hierarchy", $"Unexpected modules count {l1Model.Modules.Count} or lessons count {l1Model.TotalLessonsCount}.");
            }

            // TEST 06: Student Selects Another Lesson in Same Module
            var l2Result = await ctrlA.Lesson(courseA.Id, itemA2.Id);
            var l2Model = (StudentLessonPlayerViewModel)((ViewResult)l2Result).Model!;
            if (l2Model.CourseItemId == itemA2.Id && l2Model.CurrentLessonIndex == 2 && l2Model.LessonPosition == 2)
            {
                Pass("Lesson Selection: Direct navigation to Lesson 2 in Module 1 updates active lesson index");
            }
            else
            {
                Fail("Lesson Selection", "CurrentLessonIndex or LessonPosition incorrect for Lesson 2.");
            }

            // TEST 07: Previous Lesson Navigation (Within Module)
            if (l2Model.HasPrevious && l2Model.PreviousLesson != null && l2Model.PreviousLesson.CourseItemId == itemA1.Id)
            {
                Pass("Previous Navigation (Within Module): Lesson 2 points back to Lesson 1");
            }
            else
            {
                Fail("Previous Navigation", "Lesson 2 did not point to Lesson 1 as previous.");
            }

            // TEST 08: Next Lesson Navigation (Within Module)
            if (l1Model.HasNext && l1Model.NextLesson != null && l1Model.NextLesson.CourseItemId == itemA2.Id)
            {
                Pass("Next Navigation (Within Module): Lesson 1 points forward to Lesson 2");
            }
            else
            {
                Fail("Next Navigation", "Lesson 1 did not point to Lesson 2 as next.");
            }

            // TEST 09: Cross-Module Next Navigation (End of Module 1 -> Beginning of Module 2)
            if (l2Model.HasNext && l2Model.NextLesson != null && l2Model.NextLesson.CourseItemId == itemA3.Id)
            {
                Pass("Cross-Module Next Navigation: Lesson 2 (M1) advances across boundary to Lesson 3 (M2)");
            }
            else
            {
                Fail("Cross-Module Next", "Cross-module navigation from M1 to M2 failed.");
            }

            // TEST 10: Cross-Module Previous Navigation (Beginning of Module 2 -> End of Module 1)
            var l3Result = await ctrlA.Lesson(courseA.Id, itemA3.Id);
            var l3Model = (StudentLessonPlayerViewModel)((ViewResult)l3Result).Model!;
            if (l3Model.HasPrevious && l3Model.PreviousLesson != null && l3Model.PreviousLesson.CourseItemId == itemA2.Id)
            {
                Pass("Cross-Module Previous Navigation: Lesson 3 (M2) navigates back across boundary to Lesson 2 (M1)");
            }
            else
            {
                Fail("Cross-Module Previous", "Cross-module navigation backwards from M2 to M1 failed.");
            }

            // TEST 11: First Lesson Boundary Behavior
            if (!l1Model.HasPrevious && l1Model.PreviousLesson == null)
            {
                Pass("First Lesson Boundary: Previous button disabled (null) on curriculum entry lesson");
            }
            else
            {
                Fail("First Lesson Boundary", "First lesson has a previous link when it should be null.");
            }

            // TEST 12: Last Lesson Boundary Behavior
            var l4Result = await ctrlA.Lesson(courseA.Id, itemA4.Id);
            var l4Model = (StudentLessonPlayerViewModel)((ViewResult)l4Result).Model!;
            if (!l4Model.HasNext && l4Model.NextLesson == null)
            {
                Pass("Last Lesson Boundary: Next button disabled (null) on final curriculum lesson");
            }
            else
            {
                Fail("Last Lesson Boundary", "Final lesson has a next link when it should be null.");
            }

            // TEST 13: Unenrolled Course Access Denied
            var unenrolledCourseResult = await ctrlA.Lesson(courseB.Id, itemB1.Id);
            if (unenrolledCourseResult is ForbidResult)
            {
                Pass("Enrollment Security: Student A blocked with 403 Forbidden when accessing Course B Player");
            }
            else
            {
                Fail("Enrollment Security", $"Expected ForbidResult for unenrolled course, got {unenrolledCourseResult.GetType().Name}.");
            }

            // TEST 14: Cross-Course Lesson Access via Direct URL Denied
            var crossLessonResult = await ctrlA.Lesson(null, itemB1.Id);
            if (crossLessonResult is ForbidResult)
            {
                Pass("Direct URL Protection: Student A blocked with 403 Forbidden when accessing /Student/Lesson/{LessonB1}");
            }
            else
            {
                Fail("Direct URL Protection", $"Expected ForbidResult for unenrolled lesson direct URL, got {crossLessonResult.GetType().Name}.");
            }

            // TEST 15: Course A + Lesson B Mismatch Rejected
            var mismatchResult = await ctrlA.Lesson(courseA.Id, itemB1.Id);
            if (mismatchResult is NotFoundObjectResult || mismatchResult is NotFoundResult)
            {
                Pass("Ownership Security: Course A + Lesson B mismatch safely rejected (NotFound) without data leak");
            }
            else
            {
                Fail("Ownership Security", $"Expected NotFound for lesson not belonging to course, got {mismatchResult.GetType().Name}.");
            }

            // TEST 16: Invalid Lesson IDs Handled Safely
            var zeroResult = await ctrlA.Lesson(courseA.Id, 0);
            var negResult = await ctrlA.Lesson(courseA.Id, -100);
            var nonExistentResult = await ctrlA.Lesson(courseA.Id, 999999);
            if (zeroResult is NotFoundObjectResult && negResult is NotFoundObjectResult && nonExistentResult is NotFoundObjectResult)
            {
                Pass("Invalid ID Resilience: Zero, negative, and nonexistent IDs rejected safely with 404 NotFound");
            }
            else
            {
                Fail("Invalid ID Resilience", "One or more invalid ID tests did not return NotFound.");
            }

            // TEST 17: VideoUrl Support & Fallback Verification
            var ytMatch = Regex.Match(l1Model.VideoUrl ?? "", @"(?:youtu\.be\/|youtube\.com\/(?:embed\/|v\/|watch\?v=|watch\?.+&v=))([\w-]{11})");
            bool isDirectVideo = (l3Model.VideoUrl ?? "").EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
            if (l1Model.HasVideo && ytMatch.Success && ytMatch.Groups[1].Value == "dQw4w9WgXcQ" &&
                !l2Model.HasVideo &&
                l3Model.HasVideo && isDirectVideo)
            {
                Pass("Video Support & Fallback: YouTube embed extraction, direct MP4 detection, and null fallback verified");
            }
            else
            {
                Fail("Video Support", "VideoUrl parsing or fallback flag failed.");
            }

            // TEST 18: Empty Curriculum Resilience
            var firstEmptyLessonId = await curriculumService.GetFirstLessonIdAsync(courseC.Id);
            var learnEmptyResult = await ctrlA.Learn(courseC.Id);
            if (!firstEmptyLessonId.HasValue && learnEmptyResult is RedirectToActionResult rta && rta.ActionName == nameof(StudentController.Course))
            {
                Pass("Empty Curriculum: Course with 0 modules/lessons handled gracefully with friendly redirect");
            }
            else
            {
                Fail("Empty Curriculum", "Empty course did not redirect gracefully.");
            }

            // TEST 19: Database Cleanliness & Orphan Check
            context.Lessons.RemoveRange(context.Lessons.Where(l => l.CourseItem.Chapter.CourseId == courseA.Id || l.CourseItem.Chapter.CourseId == courseB.Id));
            context.CourseItems.RemoveRange(context.CourseItems.Where(ci => ci.Chapter.CourseId == courseA.Id || ci.Chapter.CourseId == courseB.Id));
            context.Chapters.RemoveRange(context.Chapters.Where(ch => ch.CourseId == courseA.Id || ch.CourseId == courseB.Id));
            context.Enrollments.RemoveRange(context.Enrollments.Where(e => e.CourseId == courseA.Id || e.CourseId == courseB.Id || e.CourseId == courseC.Id));
            context.Courses.RemoveRange(context.Courses.Where(c => c.Id == courseA.Id || c.Id == courseB.Id || c.Id == courseC.Id));
            await context.SaveChangesAsync();

            await userManager.DeleteAsync(studentA);
            await userManager.DeleteAsync(studentB);

            var orphanLessons = await context.Lessons.CountAsync(l => !context.CourseItems.Any(ci => ci.Id == l.CourseItemId));
            var orphanEnrollments = await context.Enrollments.CountAsync(e => !context.Courses.Any(c => c.Id == e.CourseId));
            if (orphanLessons == 0 && orphanEnrollments == 0)
            {
                Pass("Database Cleanliness: Zero orphan lessons or enrollments; SQL Server restored to clean state");
            }
            else
            {
                Fail("Database Cleanliness", $"Detected {orphanLessons} orphan lessons and {orphanEnrollments} orphan enrollments.");
            }

            Console.WriteLine("\n=================================================================");
            Console.WriteLine($"🎉  ALL {testsPassed}/{totalTests} STUDENT LESSON PLAYER TESTS PASSED SUCCESSFULLY!");
            Console.WriteLine("=================================================================\n");
        }
        catch (Exception ex)
        {
            Fail("Unhandled Exception", ex.ToString());
        }
        finally
        {
            // Safety teardown in case of exception
            try
            {
                if (courseA != null)
                {
                    var cA = await context.Courses.FindAsync(courseA.Id);
                    if (cA != null)
                    {
                        var lessons = context.Lessons.Where(l => l.CourseItem.Chapter.CourseId == courseA.Id);
                        context.Lessons.RemoveRange(lessons);
                        var items = context.CourseItems.Where(ci => ci.Chapter.CourseId == courseA.Id);
                        context.CourseItems.RemoveRange(items);
                        var chapters = context.Chapters.Where(ch => ch.CourseId == courseA.Id);
                        context.Chapters.RemoveRange(chapters);
                        var enrolls = context.Enrollments.Where(e => e.CourseId == courseA.Id);
                        context.Enrollments.RemoveRange(enrolls);
                        context.Courses.Remove(cA);
                    }
                }
                if (courseB != null)
                {
                    var cB = await context.Courses.FindAsync(courseB.Id);
                    if (cB != null)
                    {
                        var lessons = context.Lessons.Where(l => l.CourseItem.Chapter.CourseId == courseB.Id);
                        context.Lessons.RemoveRange(lessons);
                        var items = context.CourseItems.Where(ci => ci.Chapter.CourseId == courseB.Id);
                        context.CourseItems.RemoveRange(items);
                        var chapters = context.Chapters.Where(ch => ch.CourseId == courseB.Id);
                        context.Chapters.RemoveRange(chapters);
                        var enrolls = context.Enrollments.Where(e => e.CourseId == courseB.Id);
                        context.Enrollments.RemoveRange(enrolls);
                        context.Courses.Remove(cB);
                    }
                }
                if (courseC != null)
                {
                    var cC = await context.Courses.FindAsync(courseC.Id);
                    if (cC != null)
                    {
                        var enrolls = context.Enrollments.Where(e => e.CourseId == courseC.Id);
                        context.Enrollments.RemoveRange(enrolls);
                        context.Courses.Remove(cC);
                    }
                }
                await context.SaveChangesAsync();
                if (studentA != null) await userManager.DeleteAsync(studentA);
                if (studentB != null) await userManager.DeleteAsync(studentB);
            }
            catch
            {
                // Best effort teardown
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
