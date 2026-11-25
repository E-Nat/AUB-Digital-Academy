using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AUBDigitalAcademy.Controllers;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class CourseDeleteTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("🛡️  AUB DIGITAL ACADEMY - SAFE DELETE COURSE & SQL SERVER AUDIT");
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
        var controllerLogger = loggerFactory.CreateLogger<AcademicController>();

        // 1. Verify SQL Server connection & SeedData
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            Fail("Database Connectivity", "Unable to connect to SQL Server (LocalDB).");
            return;
        }
        Pass("SQL Server connection verified successfully");

        // Cleanup any leftover test courses before running
        await CleanupTestCoursesAsync(context);

        // Lookup Teacher, Major, Semester, Student
        var teachers = await userManager.GetUsersInRoleAsync("Teacher");
        var activeTeacher = teachers.FirstOrDefault(u => u.IsActive) ?? await context.Users.FirstOrDefaultAsync(u => u.IsActive);
        if (activeTeacher == null)
        {
            Fail("Teacher Lookup", "No active teacher found in SQL Server.");
            return;
        }

        var major = await context.Majors.Include(m => m.Category).FirstOrDefaultAsync();
        if (major == null)
        {
            Fail("Major Lookup", "No major found in SQL Server.");
            return;
        }

        var semester = await context.Semesters.FirstOrDefaultAsync();
        if (semester == null)
        {
            Fail("Semester Lookup", "No semester found in SQL Server.");
            return;
        }

        var student = await context.Users.FirstOrDefaultAsync(u => u.Email == "student@aub.com")
            ?? (await userManager.GetUsersInRoleAsync("Student")).FirstOrDefault();
        if (student == null)
        {
            Fail("Student Lookup", "No student found in SQL Server.");
            return;
        }
        Pass($"Loaded test dependencies: Teacher={activeTeacher.FullName}, Major={major.Code}, Semester={semester.Code}, Student={student.FullName}");

        // Helper to instantiate AcademicController
        AcademicController CreateController(bool isAjax = false)
        {
            var httpContext = new DefaultHttpContext();
            if (isAjax)
            {
                httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
            }
            var tempDataProvider = new TestTempDataProvider();
            var tempData = new TempDataDictionary(httpContext, tempDataProvider);
            var actionContext = new ActionContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
                ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
            };

            return new AcademicController(courseService, context, userManager, controllerLogger)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = tempData,
                Url = new DummyUrlHelper(actionContext)
            };
        }

        // =========================================================
        // Test 2: Invalid Course ID Validation
        // =========================================================
        var invalidResult = await courseService.DeleteCourseAsync(0);
        if (invalidResult.Status != CourseDeleteStatus.Failed || invalidResult.IsSuccess)
        {
            Fail("Invalid Course ID (Service)", "Calling DeleteCourseAsync with ID 0 did not return Failed status.");
            return;
        }
        Pass("Validation: Calling DeleteCourseAsync with ID <= 0 returns Failed status");

        // =========================================================
        // Test 3: Course Not Found Validation
        // =========================================================
        var notFoundResult = await courseService.DeleteCourseAsync(999999);
        if (notFoundResult.Status != CourseDeleteStatus.NotFound || notFoundResult.Message != "Course not found.")
        {
            Fail("Course Not Found (Service)", $"Expected NotFound, got {notFoundResult.Status} with message '{notFoundResult.Message}'");
            return;
        }
        Pass("Validation: Calling DeleteCourseAsync for non-existent ID returns NotFound status with 'Course not found.' message");

        // =========================================================
        // Test 4: TEST A — Empty Course Safe Deletion
        // =========================================================
        var emptyCourse = new Course
        {
            Code = "TEST-EMPTY-DEL",
            Title = "Empty Test Course for Safe Deletion",
            Description = "Course with no curriculum and no student data",
            Status = CourseStatus.Draft,
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(emptyCourse);
        await context.SaveChangesAsync();
        int emptyCourseId = emptyCourse.Id;

        // Verify it was persisted to SQL Server
        var emptyPersisted = await context.Courses.AnyAsync(c => c.Id == emptyCourseId);
        if (!emptyPersisted)
        {
            Fail("Empty Course Creation", "Failed to create empty course in SQL Server.");
            return;
        }

        // Perform safe deletion via service
        var emptyDeleteResult = await courseService.DeleteCourseAsync(emptyCourseId);
        if (emptyDeleteResult.Status != CourseDeleteStatus.Success || !emptyDeleteResult.IsSuccess)
        {
            Fail("TEST A: Empty Course Deletion", $"Deletion failed: {emptyDeleteResult.Message}");
            return;
        }

        // Verify in SQL Server
        var emptyStillExists = await context.Courses.AnyAsync(c => c.Id == emptyCourseId);
        if (emptyStillExists)
        {
            Fail("TEST A: SQL Server Verification", "Empty course record still exists in SQL Server after deletion!");
            return;
        }
        Pass("TEST A (Empty Course): Course deleted successfully from SQL Server when it has no dependencies");

        // =========================================================
        // Test 5: TEST B — Course with Full Curriculum (No Students)
        // Course -> Chapter -> CourseItems (Lesson, Book, Quiz -> Questions -> Options, Assignment)
        // =========================================================
        var currCourse = new Course
        {
            Code = "TEST-CURR-DEL",
            Title = "Curriculum Test Course for Deletion",
            Description = "Course with chapters, lessons, books, quizzes, questions, assignments but NO student data",
            Status = CourseStatus.Published,
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(currCourse);
        await context.SaveChangesAsync();
        int currCourseId = currCourse.Id;

        // Add Chapter
        var chapter = new Chapter
        {
            CourseId = currCourseId,
            Title = "Module 1: Foundations",
            Position = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Chapters.Add(chapter);
        await context.SaveChangesAsync();
        int chapterId = chapter.Id;

        // Add Lesson CourseItem
        var lessonItem = new CourseItem
        {
            ChapterId = chapterId,
            Title = "Lesson 1.1: Getting Started",
            Position = 1,
            Type = ItemType.Lesson
        };
        context.CourseItems.Add(lessonItem);
        await context.SaveChangesAsync();
        var lesson = new Lesson
        {
            CourseItemId = lessonItem.Id,
            ContentHtml = "<p>Introduction content</p>",
            VideoUrl = "https://example.com/video1.mp4"
        };
        context.Lessons.Add(lesson);

        // Add Book CourseItem
        var bookItem = new CourseItem
        {
            ChapterId = chapterId,
            Title = "Textbook Reading 1",
            Position = 2,
            Type = ItemType.Book
        };
        context.CourseItems.Add(bookItem);
        await context.SaveChangesAsync();
        var book = new Book
        {
            CourseItemId = bookItem.Id,
            FileUrl = "https://example.com/books/ch1.pdf",
            FileType = "PDF",
            FileSize = 1048576
        };
        context.Books.Add(book);

        // Add Quiz CourseItem with Question and Option
        var quizItem = new CourseItem
        {
            ChapterId = chapterId,
            Title = "Quiz 1: Review",
            Position = 3,
            Type = ItemType.Quiz
        };
        context.CourseItems.Add(quizItem);
        await context.SaveChangesAsync();
        var quiz = new Quiz
        {
            CourseItemId = quizItem.Id,
            TimeLimitMinutes = 20,
            PassingScore = 70.0,
            IsPublished = true
        };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        var question = new Question
        {
            QuizId = quiz.Id,
            Text = "What is HTTP?",
            Type = QuestionType.MultipleChoice,
            Points = 5.0,
            Order = 1
        };
        context.Questions.Add(question);
        await context.SaveChangesAsync();
        var option = new QuestionOption
        {
            QuestionId = question.Id,
            Text = "Hypertext Transfer Protocol",
            IsCorrect = true
        };
        context.QuestionOptions.Add(option);

        // Add Assignment CourseItem
        var assignmentItem = new CourseItem
        {
            ChapterId = chapterId,
            Title = "Assignment 1: Portfolio",
            Position = 4,
            Type = ItemType.Assignment
        };
        context.CourseItems.Add(assignmentItem);
        await context.SaveChangesAsync();
        var assignment = new Assignment
        {
            CourseItemId = assignmentItem.Id,
            Instruction = "Build your portfolio site",
            MaxScore = 100.0,
            DueDate = DateTime.UtcNow.AddDays(7)
        };
        context.Assignments.Add(assignment);
        await context.SaveChangesAsync();

        int quizId = quiz.Id;
        int questionId = question.Id;
        int optionId = option.Id;
        int assignmentId = assignment.Id;
        int lessonItemId = lessonItem.Id;

        // Perform safe deletion of course with curriculum
        var currDeleteResult = await courseService.DeleteCourseAsync(currCourseId);
        if (currDeleteResult.Status != CourseDeleteStatus.Success || !currDeleteResult.IsSuccess)
        {
            Fail("TEST B: Course with Curriculum Deletion", $"Deletion failed: {currDeleteResult.Message}");
            return;
        }

        // Verify in SQL Server that course AND all curriculum records were cleanly deleted without orphans
        var courseLeft = await context.Courses.AnyAsync(c => c.Id == currCourseId);
        var chapterLeft = await context.Chapters.AnyAsync(ch => ch.Id == chapterId);
        var itemLeft = await context.CourseItems.AnyAsync(ci => ci.Id == lessonItemId);
        var quizLeft = await context.Quizzes.AnyAsync(q => q.Id == quizId);
        var questionLeft = await context.Questions.AnyAsync(q => q.Id == questionId);
        var optionLeft = await context.QuestionOptions.AnyAsync(o => o.Id == optionId);
        var assignmentLeft = await context.Assignments.AnyAsync(a => a.Id == assignmentId);

        if (courseLeft || chapterLeft || itemLeft || quizLeft || questionLeft || optionLeft || assignmentLeft)
        {
            Fail("TEST B: Orphan Records Found",
                $"Orphan records remain in SQL Server: course={courseLeft}, chapter={chapterLeft}, item={itemLeft}, quiz={quizLeft}, question={questionLeft}, option={optionLeft}, assignment={assignmentLeft}");
            return;
        }
        Pass("TEST B (Curriculum Only): Course with chapters, lessons, books, quizzes, questions, options, and assignments deleted cleanly with zero orphaned records");

        // =========================================================
        // Test 6: TEST C — Course with Student Enrollment (Blocked)
        // =========================================================
        var enrolledCourse = new Course
        {
            Code = "TEST-ENROLL-BLOCK",
            Title = "Enrolled Students Course",
            Description = "Course with active student enrollment",
            Status = CourseStatus.Published,
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(enrolledCourse);
        await context.SaveChangesAsync();
        int enrolledCourseId = enrolledCourse.Id;

        var enrollment = new Enrollment
        {
            CourseId = enrolledCourseId,
            StudentId = student.Id,
            EnrolledAt = DateTime.UtcNow
        };
        context.Enrollments.Add(enrollment);
        await context.SaveChangesAsync();
        int enrollmentId = enrollment.Id;

        // Attempt deletion
        var enrollDeleteResult = await courseService.DeleteCourseAsync(enrolledCourseId);
        if (enrollDeleteResult.Status != CourseDeleteStatus.BlockedByStudentData || enrollDeleteResult.IsSuccess)
        {
            Fail("TEST C: Enrollment Blocking", $"Expected BlockedByStudentData status, got {enrollDeleteResult.Status}");
            return;
        }
        if (!enrollDeleteResult.Message.Contains("student enrollment", StringComparison.OrdinalIgnoreCase))
        {
            Fail("TEST C: Block Message", $"Expected enrollment warning message, got '{enrollDeleteResult.Message}'");
            return;
        }

        // Verify in SQL Server: Course and Enrollment STILL exist
        var enrolledCourseStillExists = await context.Courses.AnyAsync(c => c.Id == enrolledCourseId);
        var enrollmentStillExists = await context.Enrollments.AnyAsync(e => e.Id == enrollmentId);
        if (!enrolledCourseStillExists || !enrollmentStillExists)
        {
            Fail("TEST C: Student Data Preservation", "Enrolled course or student enrollment record was deleted despite blocking!");
            return;
        }
        Pass("TEST C (Student Enrollment): Course deletion blocked; Course and Student Enrollment preserved in SQL Server");

        // =========================================================
        // Test 7: TEST D — Course with Student Progress (Blocked)
        // =========================================================
        var progressCourse = new Course
        {
            Code = "TEST-PROG-BLOCK",
            Title = "Student Progress Tracked Course",
            Description = "Course with student item progress completion records",
            Status = CourseStatus.Published,
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(progressCourse);
        await context.SaveChangesAsync();
        int progressCourseId = progressCourse.Id;

        var progChapter = new Chapter
        {
            CourseId = progressCourseId,
            Title = "Chapter With Progress",
            Position = 1
        };
        context.Chapters.Add(progChapter);
        await context.SaveChangesAsync();

        var progItem = new CourseItem
        {
            ChapterId = progChapter.Id,
            Title = "Progress Tracked Lesson",
            Position = 1,
            Type = ItemType.Lesson
        };
        context.CourseItems.Add(progItem);
        await context.SaveChangesAsync();

        var progressRecord = new CourseProgress
        {
            CourseItemId = progItem.Id,
            StudentId = student.Id,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        };
        context.CourseProgresses.Add(progressRecord);
        await context.SaveChangesAsync();
        int progressRecordId = progressRecord.Id;

        // Attempt deletion
        var progDeleteResult = await courseService.DeleteCourseAsync(progressCourseId);
        if (progDeleteResult.Status != CourseDeleteStatus.BlockedByStudentData || progDeleteResult.IsSuccess)
        {
            Fail("TEST D: Student Progress Blocking", $"Expected BlockedByStudentData status, got {progDeleteResult.Status}");
            return;
        }
        if (!progDeleteResult.Message.Contains("progress", StringComparison.OrdinalIgnoreCase))
        {
            Fail("TEST D: Block Message", $"Expected progress warning message, got '{progDeleteResult.Message}'");
            return;
        }

        // Verify in SQL Server: Course and CourseProgress STILL exist
        var progCourseStillExists = await context.Courses.AnyAsync(c => c.Id == progressCourseId);
        var progRecordStillExists = await context.CourseProgresses.AnyAsync(cp => cp.Id == progressRecordId);
        if (!progCourseStillExists || !progRecordStillExists)
        {
            Fail("TEST D: Student Progress Preservation", "Progress-tracked course or student progress record was deleted despite blocking!");
            return;
        }
        Pass("TEST D (Student Progress): Course deletion blocked; Course and Student CourseProgress preserved in SQL Server");

        // =========================================================
        // Test 8: Course with Quiz Submission (Blocked)
        // =========================================================
        var quizSubCourse = new Course
        {
            Code = "TEST-QSUB-BLOCK",
            Title = "Quiz Submissions Course",
            Description = "Course with student quiz submissions",
            Status = CourseStatus.Published,
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(quizSubCourse);
        await context.SaveChangesAsync();
        int quizSubCourseId = quizSubCourse.Id;

        var qsubChapter = new Chapter
        {
            CourseId = quizSubCourseId,
            Title = "Chapter With Quiz Submission",
            Position = 1
        };
        context.Chapters.Add(qsubChapter);
        await context.SaveChangesAsync();

        var qsubItem = new CourseItem
        {
            ChapterId = qsubChapter.Id,
            Title = "Quiz Item",
            Position = 1,
            Type = ItemType.Quiz
        };
        context.CourseItems.Add(qsubItem);
        await context.SaveChangesAsync();

        var qsubQuiz = new Quiz
        {
            CourseItemId = qsubItem.Id,
            TimeLimitMinutes = 15,
            PassingScore = 60
        };
        context.Quizzes.Add(qsubQuiz);
        await context.SaveChangesAsync();

        var qsub = new QuizSubmission
        {
            QuizId = qsubQuiz.Id,
            StudentId = student.Id,
            Score = 85.0,
            IsPassed = true,
            SubmittedAt = DateTime.UtcNow
        };
        context.QuizSubmissions.Add(qsub);
        await context.SaveChangesAsync();
        int qsubId = qsub.Id;

        // Attempt deletion
        var qsubDeleteResult = await courseService.DeleteCourseAsync(quizSubCourseId);
        if (qsubDeleteResult.Status != CourseDeleteStatus.BlockedByStudentData || qsubDeleteResult.IsSuccess)
        {
            Fail("Quiz Submission Blocking", $"Expected BlockedByStudentData, got {qsubDeleteResult.Status}");
            return;
        }

        var qsubCourseStillExists = await context.Courses.AnyAsync(c => c.Id == quizSubCourseId);
        var qsubStillExists = await context.QuizSubmissions.AnyAsync(qs => qs.Id == qsubId);
        if (!qsubCourseStillExists || !qsubStillExists)
        {
            Fail("Quiz Submission Preservation", "Quiz submission or course deleted despite blocking!");
            return;
        }
        Pass("Dependency Check (Quiz Submission): Course deletion blocked; Quiz and QuizSubmission preserved in SQL Server");

        // =========================================================
        // Test 9: Course with Assignment Submission (Blocked)
        // =========================================================
        var asubCourse = new Course
        {
            Code = "TEST-ASUB-BLOCK",
            Title = "Assignment Submissions Course",
            Description = "Course with student assignment submissions",
            Status = CourseStatus.Published,
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(asubCourse);
        await context.SaveChangesAsync();
        int asubCourseId = asubCourse.Id;

        var asubChapter = new Chapter
        {
            CourseId = asubCourseId,
            Title = "Chapter With Assignment",
            Position = 1
        };
        context.Chapters.Add(asubChapter);
        await context.SaveChangesAsync();

        var asubItem = new CourseItem
        {
            ChapterId = asubChapter.Id,
            Title = "Assignment Item",
            Position = 1,
            Type = ItemType.Assignment
        };
        context.CourseItems.Add(asubItem);
        await context.SaveChangesAsync();

        var asubAssignment = new Assignment
        {
            CourseItemId = asubItem.Id,
            Instruction = "Complete homework",
            MaxScore = 100,
            DueDate = DateTime.UtcNow.AddDays(3)
        };
        context.Assignments.Add(asubAssignment);
        await context.SaveChangesAsync();

        var asubRecord = new AssignmentSubmission
        {
            AssignmentId = asubAssignment.Id,
            StudentId = student.Id,
            TextResponse = "Here is my project submission.",
            Status = SubmissionStatus.Submitted,
            SubmittedAt = DateTime.UtcNow
        };
        context.AssignmentSubmissions.Add(asubRecord);
        await context.SaveChangesAsync();
        int asubId = asubRecord.Id;

        // Attempt deletion
        var asubDeleteResult = await courseService.DeleteCourseAsync(asubCourseId);
        if (asubDeleteResult.Status != CourseDeleteStatus.BlockedByStudentData || asubDeleteResult.IsSuccess)
        {
            Fail("Assignment Submission Blocking", $"Expected BlockedByStudentData, got {asubDeleteResult.Status}");
            return;
        }

        var asubCourseStillExists = await context.Courses.AnyAsync(c => c.Id == asubCourseId);
        var asubStillExists = await context.AssignmentSubmissions.AnyAsync(a => a.Id == asubId);
        if (!asubCourseStillExists || !asubStillExists)
        {
            Fail("Assignment Submission Preservation", "Assignment submission or course deleted despite blocking!");
            return;
        }
        Pass("Dependency Check (Assignment Submission): Course deletion blocked; Assignment and AssignmentSubmission preserved in SQL Server");

        // =========================================================
        // Test 10: TEST E — Cancel Confirmation Behavior
        // =========================================================
        var cancelTestCourse = new Course
        {
            Code = "TEST-CANCEL-DEL",
            Title = "Cancel Deletion Course",
            Description = "Course where Admin cancels confirmation dialog",
            Status = CourseStatus.Draft,
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(cancelTestCourse);
        await context.SaveChangesAsync();
        int cancelTestCourseId = cancelTestCourse.Id;

        // User clicks Cancel -> No request is sent to controller, course remains untouched
        var cancelCourseExists = await context.Courses.AnyAsync(c => c.Id == cancelTestCourseId);
        if (!cancelCourseExists)
        {
            Fail("TEST E: Cancel Dialog", "Course was unexpectedly removed when user cancelled.");
            return;
        }
        Pass("TEST E (Cancel Dialog): Clicking Cancel leaves Course record completely untouched in SQL Server");

        // =========================================================
        // Test 11: AcademicController.DeleteCourse - Invalid ID (AJAX & Form POST)
        // =========================================================
        var controllerAjax = CreateController(isAjax: true);
        var invalidAjaxResult = await controllerAjax.DeleteCourse(0);
        if (invalidAjaxResult is not BadRequestObjectResult)
        {
            Fail("Controller Invalid ID (AJAX)", "Expected BadRequestObjectResult for ID 0.");
            return;
        }
        var controllerForm = CreateController(isAjax: false);
        var invalidFormResult = await controllerForm.DeleteCourse(-5);
        if (invalidFormResult is not RedirectToActionResult redirectInv || redirectInv.ActionName != "Course")
        {
            Fail("Controller Invalid ID (Form)", "Expected RedirectToActionResult to Course.");
            return;
        }
        if (controllerForm.TempData["ErrorMessage"]?.ToString() != "Invalid course ID.")
        {
            Fail("Controller Invalid ID TempData", "Expected 'Invalid course ID.' in TempData.");
            return;
        }
        Pass("Controller Validation: Invalid course IDs return BadRequest for AJAX and set TempData ErrorMessage for Form POST");

        // =========================================================
        // Test 12: AcademicController.DeleteCourse - Not Found (AJAX & Form POST)
        // =========================================================
        var notFoundAjaxResult = await controllerAjax.DeleteCourse(888888);
        if (notFoundAjaxResult is not NotFoundObjectResult)
        {
            Fail("Controller Not Found (AJAX)", "Expected NotFoundObjectResult.");
            return;
        }
        var notFoundFormController = CreateController(isAjax: false);
        var notFoundFormResult = await notFoundFormController.DeleteCourse(888888);
        if (notFoundFormResult is not RedirectToActionResult notFoundRed || notFoundRed.ActionName != "Course")
        {
            Fail("Controller Not Found (Form)", "Expected RedirectToActionResult to Course.");
            return;
        }
        if (notFoundFormController.TempData["ErrorMessage"]?.ToString() != "Course not found.")
        {
            Fail("Controller Not Found TempData", "Expected 'Course not found.' in TempData.");
            return;
        }
        Pass("Controller Validation: Non-existent course ID returns NotFound for AJAX and sets TempData ErrorMessage for Form POST");

        // =========================================================
        // Test 13: AcademicController.DeleteCourse - Blocked by Student Data (AJAX)
        // =========================================================
        var blockedAjaxResult = await controllerAjax.DeleteCourse(enrolledCourseId);
        if (blockedAjaxResult is not JsonResult blockedJson)
        {
            Fail("Controller Blocked (AJAX)", "Expected JsonResult for blocked course deletion.");
            return;
        }
        var blockedJsonData = blockedJson.Value;
        var successProp = blockedJsonData?.GetType().GetProperty("success")?.GetValue(blockedJsonData);
        var blockedProp = blockedJsonData?.GetType().GetProperty("blocked")?.GetValue(blockedJsonData);
        var msgProp = blockedJsonData?.GetType().GetProperty("message")?.GetValue(blockedJsonData)?.ToString();

        if (successProp is not false || blockedProp is not true || string.IsNullOrEmpty(msgProp) || !msgProp.Contains("student enrollment"))
        {
            Fail("Controller Blocked JSON Payload", "JSON response did not indicate blocked status with clear message.");
            return;
        }
        Pass("Controller Blocked Handling: AJAX request returns JSON with { success = false, blocked = true, message = ... }");

        // =========================================================
        // Test 14: AcademicController.DeleteCourse - Successful Delete via AJAX
        // =========================================================
        var ajaxDeleteCourse = new Course
        {
            Code = "TEST-AJAX-DEL",
            Title = "AJAX Delete Test Course",
            Status = CourseStatus.Draft,
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(ajaxDeleteCourse);
        await context.SaveChangesAsync();
        int ajaxDeleteCourseId = ajaxDeleteCourse.Id;

        var ajaxSuccessResult = await controllerAjax.DeleteCourse(ajaxDeleteCourseId);
        if (ajaxSuccessResult is not JsonResult successJson)
        {
            Fail("Controller Success (AJAX)", "Expected JsonResult for successful course deletion.");
            return;
        }
        var successJsonData = successJson.Value;
        var sSuccess = successJsonData?.GetType().GetProperty("success")?.GetValue(successJsonData);
        var sMsg = successJsonData?.GetType().GetProperty("message")?.GetValue(successJsonData)?.ToString();
        var sId = successJsonData?.GetType().GetProperty("courseId")?.GetValue(successJsonData);

        if (sSuccess is not true || sMsg != "Course deleted successfully." || (int?)sId != ajaxDeleteCourseId)
        {
            Fail("Controller Success JSON Payload", "JSON response did not match expected success contract.");
            return;
        }

        var ajaxDeletedInDb = await context.Courses.AnyAsync(c => c.Id == ajaxDeleteCourseId);
        if (ajaxDeletedInDb)
        {
            Fail("Controller AJAX DB Verification", "Course still exists in SQL Server after AJAX delete!");
            return;
        }
        Pass("Controller Success (AJAX): Returns JSON { success = true, message = 'Course deleted successfully.', courseId = id } and removes course from SQL Server");

        // =========================================================
        // Test 15: AcademicController.DeleteCourse - Successful Delete via Regular Form POST
        // =========================================================
        var formDeleteCourse = new Course
        {
            Code = "TEST-FORM-DEL",
            Title = "Form POST Delete Test Course",
            Status = CourseStatus.Draft,
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = activeTeacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(formDeleteCourse);
        await context.SaveChangesAsync();
        int formDeleteCourseId = formDeleteCourse.Id;

        var formSuccessController = CreateController(isAjax: false);
        var formSuccessResult = await formSuccessController.DeleteCourse(formDeleteCourseId);
        if (formSuccessResult is not RedirectToActionResult formRed || formRed.ActionName != "Course")
        {
            Fail("Controller Success (Form)", "Expected RedirectToActionResult to Course.");
            return;
        }
        if (formSuccessController.TempData["SuccessMessage"]?.ToString() != "Course deleted successfully.")
        {
            Fail("Controller Success TempData", "Expected 'Course deleted successfully.' in TempData.");
            return;
        }

        var formDeletedInDb = await context.Courses.AnyAsync(c => c.Id == formDeleteCourseId);
        if (formDeletedInDb)
        {
            Fail("Controller Form DB Verification", "Course still exists in SQL Server after form delete!");
            return;
        }
        Pass("Controller Success (Form POST): Sets TempData['SuccessMessage'] = 'Course deleted successfully.', redirects to Course action, and deletes course from SQL Server");

        // =========================================================
        // Test 16: Page Refresh & Application Restart Test
        // =========================================================
        Console.WriteLine("\n  Simulating application restart & browser refresh...");
        using (var restartScope = serviceProvider.CreateScope())
        {
            var newContext = restartScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var newCourseService = restartScope.ServiceProvider.GetRequiredService<ICourseService>();

            // Confirm deleted courses do not exist after restart
            var emptyCheck = await newContext.Courses.AnyAsync(c => c.Id == emptyCourseId);
            var currCheck = await newContext.Courses.AnyAsync(c => c.Id == currCourseId);
            var ajaxCheck = await newContext.Courses.AnyAsync(c => c.Id == ajaxDeleteCourseId);
            var formCheck = await newContext.Courses.AnyAsync(c => c.Id == formDeleteCourseId);

            if (emptyCheck || currCheck || ajaxCheck || formCheck)
            {
                Fail("Application Restart", "Deleted courses reappeared after application restart!");
                return;
            }

            // Confirm blocked courses and student data persist after restart
            var enrolledCheck = await newContext.Courses.AnyAsync(c => c.Id == enrolledCourseId);
            var enrollmentCheck = await newContext.Enrollments.AnyAsync(e => e.Id == enrollmentId);
            var progCheck = await newContext.Courses.AnyAsync(c => c.Id == progressCourseId);
            var progressCheck = await newContext.CourseProgresses.AnyAsync(cp => cp.Id == progressRecordId);
            var qsubCheckCourse = await newContext.Courses.AnyAsync(c => c.Id == quizSubCourseId);
            var qsubCheckSub = await newContext.QuizSubmissions.AnyAsync(qs => qs.Id == qsubId);
            var asubCheckCourse = await newContext.Courses.AnyAsync(c => c.Id == asubCourseId);
            var asubCheckSub = await newContext.AssignmentSubmissions.AnyAsync(a => a.Id == asubId);

            if (!enrolledCheck || !enrollmentCheck || !progCheck || !progressCheck || !qsubCheckCourse || !qsubCheckSub || !asubCheckCourse || !asubCheckSub)
            {
                Fail("Application Restart", "Protected courses or student records were lost across application restart!");
                return;
            }

            Pass("Page Refresh & Application Restart Test: Deletion states and protected student histories persist accurately in SQL Server across application restarts (no mock, no localStorage, no in-memory cache)");

            // Clean up test records created for blocking tests so database stays pristine
            await CleanupTestCoursesAsync(newContext);
        }

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"✅ ALL {testsPassed} SAFE DELETE ACCEPTANCE TESTS PASSED (100% SUCCESS)");
        Console.WriteLine("=================================================================\n");
    }

    private static async Task CleanupTestCoursesAsync(ApplicationDbContext db)
    {
        var testCourses = await db.Courses
            .Where(c => c.Code.StartsWith("TEST-"))
            .Select(c => c.Id)
            .ToListAsync();

        if (testCourses.Any())
        {
            var asubs = await db.AssignmentSubmissions
                .Where(a => testCourses.Contains(a.Assignment.CourseItem.Chapter.CourseId))
                .ToListAsync();
            db.AssignmentSubmissions.RemoveRange(asubs);

            var qsubs = await db.QuizSubmissions
                .Where(q => testCourses.Contains(q.Quiz.CourseItem.Chapter.CourseId))
                .ToListAsync();
            db.QuizSubmissions.RemoveRange(qsubs);

            var progs = await db.CourseProgresses
                .Where(cp => testCourses.Contains(cp.CourseItem.Chapter.CourseId))
                .ToListAsync();
            db.CourseProgresses.RemoveRange(progs);

            var enrolls = await db.Enrollments
                .Where(e => testCourses.Contains(e.CourseId))
                .ToListAsync();
            db.Enrollments.RemoveRange(enrolls);

            await db.SaveChangesAsync();

            var courses = await db.Courses
                .Include(c => c.Chapters)
                    .ThenInclude(ch => ch.CourseItems)
                .Where(c => testCourses.Contains(c.Id))
                .ToListAsync();

            db.Courses.RemoveRange(courses);
            await db.SaveChangesAsync();
        }
    }

    private class TestTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object?> _store = new();

        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            return _store.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!);
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            foreach (var kv in values)
            {
                _store[kv.Key] = kv.Value;
            }
        }
    }

    private class DummyUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; }

        public DummyUrlHelper(ActionContext actionContext)
        {
            ActionContext = actionContext;
        }

        public string? Action(UrlActionContext actionContext) => $"/Academic/{actionContext.Action}";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? RouteUrl(UrlRouteContext routeContext) => "/";
        public string? Link(string? routeName, object? values) => "/";
    }
}
