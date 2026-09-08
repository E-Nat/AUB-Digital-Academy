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

public static class QuizIntegrationTestRunner
{
    private class DummyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("📝  AUB DIGITAL ACADEMY - STEP 4.3 QUIZ MANAGEMENT & TAKING AUDIT");
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

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var enrollmentService = scope.ServiceProvider.GetRequiredService<IEnrollmentService>();
        var courseService = scope.ServiceProvider.GetRequiredService<ICourseService>();
        var curriculumService = scope.ServiceProvider.GetRequiredService<ICurriculumService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var academicLogger = loggerFactory.CreateLogger<AcademicController>();
        var studentLogger = loggerFactory.CreateLogger<StudentController>();

        // Preconditions
        if (!await context.Database.CanConnectAsync())
        {
            Fail("Database Connectivity", "Unable to connect to SQL Server database.");
            return;
        }

        string testRunId = Guid.NewGuid().ToString("N").Substring(0, 8);
        string studentAEmail = $"quiz-student-a-{testRunId}@aub.edu";
        string studentBEmail = $"quiz-student-b-{testRunId}@aub.edu";
        string course1Code = $"CS-QZ1-{testRunId}";
        string course2Code = $"CS-QZ2-{testRunId}";

        User? studentA = null;
        User? studentB = null;
        Course? course1 = null;
        Course? course2 = null;
        Chapter? module1 = null;
        Chapter? module2 = null;

        Quiz? createdQuiz = null;
        CourseItem? quizItem = null;
        Question? question1 = null;
        Question? question2 = null;
        QuizSubmission? studentASubmission = null;

        try
        {
            // Prerequisite setup: Teacher, Major, Semester
            var teacher = await userManager.FindByEmailAsync("teacher@aub.edu")
                ?? await context.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.Contains("teacher"))
                ?? await context.Users.FirstOrDefaultAsync();

            if (teacher == null)
            {
                teacher = new User
                {
                    UserName = $"teacher-quiz-{testRunId}@aub.edu",
                    Email = $"teacher-quiz-{testRunId}@aub.edu",
                    FullName = "Professor Alan Turing",
                    UniversityId = $"FAC-QZ-{testRunId}"
                };
                await userManager.CreateAsync(teacher, "TeacherPass123!");
            }

            var major = await context.Majors.FirstOrDefaultAsync();
            if (major == null)
            {
                major = new Major { Name = "Computer Science", Code = $"CSQ-{testRunId}" };
                context.Majors.Add(major);
                await context.SaveChangesAsync();
            }

            var semester = await context.Semesters.FirstOrDefaultAsync();
            if (semester == null)
            {
                semester = new Semester { Name = "Fall 2026", Code = $"F26Q-{testRunId}", StartDate = DateTime.UtcNow.AddMonths(-1), EndDate = DateTime.UtcNow.AddMonths(4) };
                context.Semesters.Add(semester);
                await context.SaveChangesAsync();
            }

            // Create Course 1
            course1 = new Course
            {
                Title = $"Introduction to Algorithms {testRunId}",
                Code = course1Code,
                Description = "Algorithms and Problem Solving",
                Status = CourseStatus.Published,
                TeacherId = teacher.Id,
                MajorId = major.Id,
                SemesterId = semester.Id,
                CreatedAt = DateTime.UtcNow
            };
            context.Courses.Add(course1);
            await context.SaveChangesAsync();

            // Create Course 2 (for isolation testing)
            course2 = new Course
            {
                Title = $"Advanced Systems {testRunId}",
                Code = course2Code,
                Description = "Operating Systems Architecture",
                Status = CourseStatus.Published,
                TeacherId = teacher.Id,
                MajorId = major.Id,
                SemesterId = semester.Id,
                CreatedAt = DateTime.UtcNow
            };
            context.Courses.Add(course2);
            await context.SaveChangesAsync();

            // Create Modules in Course 1 & Course 2
            module1 = new Chapter
            {
                CourseId = course1.Id,
                Title = "Module 1: Foundations",
                Position = 1
            };
            context.Chapters.Add(module1);

            module2 = new Chapter
            {
                CourseId = course2.Id,
                Title = "Module 1: OS Kernels",
                Position = 1
            };
            context.Chapters.Add(module2);
            await context.SaveChangesAsync();

            // Create Student A and Student B
            studentA = new User
            {
                UserName = studentAEmail,
                Email = studentAEmail,
                FullName = "Grace Hopper",
                UniversityId = $"STU-A-{testRunId}"
            };
            await userManager.CreateAsync(studentA, "StudentPass123!");

            studentB = new User
            {
                UserName = studentBEmail,
                Email = studentBEmail,
                FullName = "Ada Lovelace",
                UniversityId = $"STU-B-{testRunId}"
            };
            await userManager.CreateAsync(studentB, "StudentPass123!");

            // Enroll Student A in Course 1 ONLY
            var enrollA = new Enrollment
            {
                StudentId = studentA.Id,
                CourseId = course1.Id,
                EnrolledAt = DateTime.UtcNow
            };
            context.Enrollments.Add(enrollA);

            // Enroll Student B in Course 2 ONLY
            var enrollB = new Enrollment
            {
                StudentId = studentB.Id,
                CourseId = course2.Id,
                EnrolledAt = DateTime.UtcNow
            };
            context.Enrollments.Add(enrollB);
            await context.SaveChangesAsync();

            // Helper to configure Controller
            StudentController CreateStudentController(User student)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, student.Id),
                    new(ClaimTypes.Name, student.Email ?? ""),
                    new(ClaimTypes.Role, "Student")
                };
                var identity = new ClaimsIdentity(claims, "TestAuth");
                var userPrincipal = new ClaimsPrincipal(identity);

                var httpContext = new DefaultHttpContext { User = userPrincipal };
                var ctrl = new StudentController(enrollmentService, courseService, curriculumService, userManager, context, studentLogger)
                {
                    ControllerContext = new ControllerContext { HttpContext = httpContext },
                    TempData = new TempDataDictionary(httpContext, new DummyTempDataProvider())
                };
                return ctrl;
            }

            // -------------------------------------------------------------
            // TEST 1: Admin creates Quiz -> Quiz saved
            // -------------------------------------------------------------
            var createQuizModel = new QuizCreateViewModel
            {
                CourseId = course1.Id,
                ChapterId = module1.Id,
                Title = "Midterm Quiz: Algorithm Basics",
                TimeLimitMinutes = 45,
                PassingScore = 70.0,
                IsPublished = true,
                Position = 1
            };

            var createQuizResult = await curriculumService.CreateQuizAsync(createQuizModel);
            if (!createQuizResult.IsSuccess || !createQuizResult.EntityId.HasValue)
            {
                Fail("Admin creates Quiz", $"Failed to create quiz: {createQuizResult.Message}");
                return;
            }

            quizItem = await context.CourseItems
                .Include(ci => ci.Quiz)
                .FirstOrDefaultAsync(ci => ci.Id == createQuizResult.EntityId.Value && ci.Type == ItemType.Quiz);

            if (quizItem == null || quizItem.Quiz == null)
            {
                Fail("Admin creates Quiz", "CourseItem or Quiz record not found in SQL Server.");
                return;
            }

            createdQuiz = quizItem.Quiz;
            if (createdQuiz.PassingScore != 70.0 || createdQuiz.TimeLimitMinutes != 45 || !createdQuiz.IsPublished)
            {
                Fail("Admin creates Quiz", $"Quiz properties mismatch: PassingScore={createdQuiz.PassingScore}, TimeLimit={createdQuiz.TimeLimitMinutes}, IsPublished={createdQuiz.IsPublished}");
                return;
            }
            Pass("Admin creates Quiz -> Quiz saved to SQL Server");

            // -------------------------------------------------------------
            // TEST 2: Quiz belongs to correct Module
            // -------------------------------------------------------------
            if (quizItem.ChapterId != module1.Id)
            {
                Fail("Quiz belongs to correct Module", $"Quiz ChapterId {quizItem.ChapterId} != expected {module1.Id}.");
                return;
            }
            Pass("Quiz belongs to correct Module");

            // -------------------------------------------------------------
            // TEST 3: Admin adds Question
            // -------------------------------------------------------------
            var addQuestionModel1 = new QuestionViewModel
            {
                QuizId = createdQuiz.Id,
                Text = "What is the time complexity of binary search on a sorted array?",
                Points = 2.0,
                Order = 1,
                Type = QuestionType.MultipleChoice,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "O(n)", IsCorrect = false },
                    new() { Text = "O(log n)", IsCorrect = true },
                    new() { Text = "O(n^2)", IsCorrect = false },
                    new() { Text = "O(1)", IsCorrect = false }
                }
            };

            var addQ1Result = await curriculumService.AddQuestionAsync(createdQuiz.Id, addQuestionModel1);
            if (!addQ1Result.IsSuccess || !addQ1Result.EntityId.HasValue)
            {
                Fail("Admin adds Question", $"Failed to add Question 1: {addQ1Result.Message}");
                return;
            }

            question1 = await context.Questions
                .Include(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == addQ1Result.EntityId.Value);

            if (question1 == null || question1.QuizId != createdQuiz.Id || question1.Points != 2.0)
            {
                Fail("Admin adds Question", "Question 1 was not persisted properly with QuizId and Points.");
                return;
            }
            Pass("Admin adds Question -> Question created with points and order");

            // -------------------------------------------------------------
            // TEST 4: Admin adds Options
            // -------------------------------------------------------------
            if (question1.Options.Count != 4)
            {
                Fail("Admin adds Options", $"Expected 4 options, found {question1.Options.Count}.");
                return;
            }
            Pass("Admin adds Options -> 4 options created under Question");

            // -------------------------------------------------------------
            // TEST 5: Correct Answer saved
            // -------------------------------------------------------------
            var correctOptionQ1 = question1.Options.FirstOrDefault(o => o.IsCorrect);
            if (correctOptionQ1 == null || correctOptionQ1.Text != "O(log n)")
            {
                Fail("Correct Answer saved", "Correct answer is not 'O(log n)' or missing.");
                return;
            }

            int correctCountQ1 = question1.Options.Count(o => o.IsCorrect);
            if (correctCountQ1 != 1)
            {
                Fail("Correct Answer saved", $"Expected exactly 1 correct answer, found {correctCountQ1}.");
                return;
            }
            Pass("Correct Answer saved -> Exactly one correct option persisted in SQL Server");

            // -------------------------------------------------------------
            // TEST 6: Question validation: no correct answer -> rejected
            // -------------------------------------------------------------
            var invalidQModel = new QuestionViewModel
            {
                QuizId = createdQuiz.Id,
                Text = "Invalid Question with no correct answer",
                Points = 1.0,
                Order = 2,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "Option A", IsCorrect = false },
                    new() { Text = "Option B", IsCorrect = false }
                }
            };

            var invalidResult = await curriculumService.AddQuestionAsync(createdQuiz.Id, invalidQModel);
            if (invalidResult.IsSuccess)
            {
                Fail("Question validation: no correct answer -> rejected", "Server accepted question with 0 correct options.");
                return;
            }
            Pass("Question validation: no correct answer -> rejected safely by server");

            // Add second valid question for scoring tests: Question 2 (Points = 3.0, total quiz points = 5.0)
            var addQuestionModel2 = new QuestionViewModel
            {
                QuizId = createdQuiz.Id,
                Text = "Which data structure operates on a Last-In, First-Out (LIFO) principle?",
                Points = 3.0,
                Order = 2,
                Type = QuestionType.MultipleChoice,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "Queue", IsCorrect = false },
                    new() { Text = "Stack", IsCorrect = true },
                    new() { Text = "Hash Table", IsCorrect = false }
                }
            };
            var addQ2Result = await curriculumService.AddQuestionAsync(createdQuiz.Id, addQuestionModel2);
            question2 = await context.Questions
                .Include(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == addQ2Result.EntityId!.Value);

            // -------------------------------------------------------------
            // TEST 7: Enrolled student accesses Quiz -> allowed
            // -------------------------------------------------------------
            var studentACtrl = CreateStudentController(studentA);
            var getQuizResult = await studentACtrl.Quiz(course1.Id, quizItem.Id);

            if (getQuizResult is not ViewResult viewRes || viewRes.Model is not StudentQuizPlayerViewModel playerVm)
            {
                Fail("Enrolled student accesses Quiz -> allowed", "Expected ViewResult with StudentQuizPlayerViewModel.");
                return;
            }

            if (playerVm.QuizTitle != "Midterm Quiz: Algorithm Basics" || playerVm.Questions.Count != 2)
            {
                Fail("Enrolled student accesses Quiz -> allowed", $"Player ViewModel mismatch: Questions count {playerVm.Questions.Count}.");
                return;
            }
            Pass("Enrolled student accesses Quiz -> allowed (returns Quiz player ViewModel)");

            // -------------------------------------------------------------
            // TEST 8: Unenrolled student accesses Quiz -> 403 Forbidden
            // -------------------------------------------------------------
            var studentBCtrl = CreateStudentController(studentB); // Student B is unenrolled in Course 1
            var unenrolledAccess = await studentBCtrl.Quiz(course1.Id, quizItem.Id);

            if (unenrolledAccess is not ForbidResult)
            {
                Fail("Unenrolled student accesses Quiz -> 403 Forbidden", $"Expected ForbidResult, received {unenrolledAccess.GetType().Name}.");
                return;
            }
            Pass("Unenrolled student accesses Quiz -> 403 Forbidden");

            // -------------------------------------------------------------
            // TEST 9: Student submits answers -> QuizSubmission saved
            // -------------------------------------------------------------
            // Answer Question 1 correctly (O(log n) -> 2 points)
            // Answer Question 2 correctly (Stack -> 3 points)
            // Total points: 5.0, Earned: 5.0 -> Score = 100.0%, IsPassed = true (>= 70%)
            var q1CorrectOpt = question1.Options.First(o => o.IsCorrect);
            var q2CorrectOpt = question2!.Options.First(o => o.IsCorrect);

            var perfectAnswers = new Dictionary<int, int>
            {
                { question1.Id, q1CorrectOpt.Id },
                { question2.Id, q2CorrectOpt.Id }
            };

            var submitAction = await studentACtrl.SubmitQuiz(course1.Id, quizItem.Id, perfectAnswers);
            if (submitAction is not RedirectToActionResult redirectRes)
            {
                Fail("Student submits answers -> QuizSubmission saved", $"Expected RedirectToActionResult, got {submitAction.GetType().Name}.");
                return;
            }

            int submissionId = (int)redirectRes.RouteValues!["submissionId"]!;
            studentASubmission = await context.QuizSubmissions
                .FirstOrDefaultAsync(qs => qs.Id == submissionId);

            if (studentASubmission == null || studentASubmission.StudentId != studentA.Id || studentASubmission.QuizId != createdQuiz.Id)
            {
                Fail("Student submits answers -> QuizSubmission saved", "QuizSubmission not saved in SQL Server with correct StudentId & QuizId.");
                return;
            }
            Pass("Student submits answers -> QuizSubmission saved to SQL Server");

            // -------------------------------------------------------------
            // TEST 10: Server calculates correct score
            // -------------------------------------------------------------
            if (Math.Abs(studentASubmission.Score - 100.0) > 0.01 || !studentASubmission.IsPassed)
            {
                Fail("Server calculates correct score", $"Expected Score 100.0% and IsPassed true, got {studentASubmission.Score}% and {studentASubmission.IsPassed}.");
                return;
            }

            // Test partial score: Student submits Q1 correct (2 pts), Q2 incorrect (0 pts) -> 2/5 = 40.0%, IsPassed = false (< 70%)
            var q2WrongOpt = question2.Options.First(o => !o.IsCorrect);
            var partialAnswers = new Dictionary<int, int>
            {
                { question1.Id, q1CorrectOpt.Id },
                { question2.Id, q2WrongOpt.Id }
            };

            var partialSubmit = await studentACtrl.SubmitQuiz(course1.Id, quizItem.Id, partialAnswers);
            var partialRedirect = (RedirectToActionResult)partialSubmit;
            int partialSubId = (int)partialRedirect.RouteValues!["submissionId"]!;
            var partialSub = await context.QuizSubmissions.FirstAsync(qs => qs.Id == partialSubId);

            if (Math.Abs(partialSub.Score - 40.0) > 0.01 || partialSub.IsPassed)
            {
                Fail("Server calculates correct score", $"Expected partial score 40.0% and IsPassed false, got {partialSub.Score}% and {partialSub.IsPassed}.");
                return;
            }
            Pass("Server calculates correct score (100% on all correct, 40% on partial, pass/fail evaluated against passing score)");

            // -------------------------------------------------------------
            // TEST 11: Client cannot manipulate score
            // -------------------------------------------------------------
            // The client submission endpoint accepts ONLY Dictionary<int, int> (questionId -> optionId).
            // It has no parameter for score or isPassed.
            // When submitting wrong answers, the server strictly evaluates against database options.
            var allWrongAnswers = new Dictionary<int, int>
            {
                { question1.Id, question1.Options.First(o => !o.IsCorrect).Id },
                { question2.Id, question2.Options.First(o => !o.IsCorrect).Id }
            };
            var wrongSubmit = await studentACtrl.SubmitQuiz(course1.Id, quizItem.Id, allWrongAnswers);
            int wrongSubId = (int)((RedirectToActionResult)wrongSubmit).RouteValues!["submissionId"]!;
            var wrongSub = await context.QuizSubmissions.FirstAsync(qs => qs.Id == wrongSubId);

            if (wrongSub.Score != 0.0 || wrongSub.IsPassed)
            {
                Fail("Client cannot manipulate score", $"Score was {wrongSub.Score} instead of 0.0.");
                return;
            }
            Pass("Client cannot manipulate score (server strictly calculates score from DB QuestionOptions)");

            // -------------------------------------------------------------
            // TEST 12: Student A submission isolated from Student B
            // -------------------------------------------------------------
            // Check submissions in DB for Student A vs Student B
            var studentASubs = await context.QuizSubmissions.Where(qs => qs.StudentId == studentA.Id).ToListAsync();
            var studentBSubs = await context.QuizSubmissions.Where(qs => qs.StudentId == studentB.Id).ToListAsync();

            if (studentASubs.Count == 0 || studentBSubs.Count != 0)
            {
                Fail("Student A submission isolated from Student B", "Submission leak or incorrect student assignment in database.");
                return;
            }
            Pass("Student A submission isolated from Student B");

            // -------------------------------------------------------------
            // TEST 13: Student cannot access another Course Quiz
            // -------------------------------------------------------------
            // Create a quiz in Course 2 (Student A is NOT enrolled in Course 2)
            var course2QuizModel = new QuizCreateViewModel
            {
                CourseId = course2.Id,
                ChapterId = module2.Id,
                Title = "Course 2 Kernel Quiz",
                TimeLimitMinutes = 30,
                PassingScore = 60.0,
                IsPublished = true
            };
            var course2QuizResult = await curriculumService.CreateQuizAsync(course2QuizModel);
            int course2QuizItemId = course2QuizResult.EntityId!.Value;

            var crossCourseAccess = await studentACtrl.Quiz(course2.Id, course2QuizItemId);
            if (crossCourseAccess is not ForbidResult)
            {
                Fail("Student cannot access another Course Quiz", $"Expected ForbidResult for cross-course quiz, got {crossCourseAccess.GetType().Name}.");
                return;
            }
            Pass("Student cannot access another Course Quiz (returns 403 Forbid)");

            // -------------------------------------------------------------
            // TEST 14: Student cannot access another Student result
            // -------------------------------------------------------------
            // Student B attempts to access Student A's submission result
            var unauthorizedResultAccess = await studentBCtrl.QuizResult(course1.Id, quizItem.Id, studentASubmission.Id);
            if (unauthorizedResultAccess is not ForbidResult)
            {
                Fail("Student cannot access another Student result", $"Expected ForbidResult when accessing another student's result, got {unauthorizedResultAccess.GetType().Name}.");
                return;
            }
            Pass("Student cannot access another Student result (returns 403 Forbid)");

            // -------------------------------------------------------------
            // TEST 15: Invalid Quiz handled safely
            // -------------------------------------------------------------
            var invalidQuizAccess = await studentACtrl.Quiz(course1.Id, 999999);
            if (invalidQuizAccess is not NotFoundResult && invalidQuizAccess is not NotFoundObjectResult)
            {
                Fail("Invalid Quiz handled safely", $"Expected NotFound for non-existent quiz, got {invalidQuizAccess.GetType().Name}.");
                return;
            }
            Pass("Invalid Quiz handled safely (returns 404 NotFound)");

            // -------------------------------------------------------------
            // TEST 16: Quiz with zero Questions handled safely
            // -------------------------------------------------------------
            // Create a quiz with 0 questions in Course 1
            var emptyQuizModel = new QuizCreateViewModel
            {
                CourseId = course1.Id,
                ChapterId = module1.Id,
                Title = "Empty Quiz (0 Questions)",
                TimeLimitMinutes = 10,
                PassingScore = 50.0,
                IsPublished = true
            };
            var emptyQuizResult = await curriculumService.CreateQuizAsync(emptyQuizModel);
            int emptyQuizItemId = emptyQuizResult.EntityId!.Value;

            // Student accesses empty quiz
            var emptyQuizView = await studentACtrl.Quiz(course1.Id, emptyQuizItemId);
            if (emptyQuizView is not ViewResult emptyViewRes || ((StudentQuizPlayerViewModel)emptyViewRes.Model!).Questions.Count != 0)
            {
                Fail("Quiz with zero Questions handled safely", "Empty quiz view failed to load safely.");
                return;
            }

            // Student submits empty quiz (no questions to answer)
            var emptySubmit = await studentACtrl.SubmitQuiz(course1.Id, emptyQuizItemId, new Dictionary<int, int>());
            if (emptySubmit is not RedirectToActionResult emptyRedirect)
            {
                Fail("Quiz with zero Questions handled safely", "Empty quiz submission failed.");
                return;
            }

            int emptySubId = (int)emptyRedirect.RouteValues!["submissionId"]!;
            var emptySubRecord = await context.QuizSubmissions.FirstAsync(qs => qs.Id == emptySubId);
            if (emptySubRecord.Score != 0.0 || emptySubRecord.IsPassed)
            {
                Fail("Quiz with zero Questions handled safely", $"Empty quiz score should be 0.0, was {emptySubRecord.Score}.");
                return;
            }
            Pass("Quiz with zero Questions handled safely (no division-by-zero, score = 0.0)");
        }
        finally
        {
            // Cleanup test data
            try
            {
                if (studentASubmission != null)
                {
                    var allSubs = await context.QuizSubmissions
                        .Where(qs => (studentA != null && qs.StudentId == studentA.Id) || (studentB != null && qs.StudentId == studentB.Id))
                        .ToListAsync();
                    context.QuizSubmissions.RemoveRange(allSubs);
                }

                if (course1 != null)
                {
                    var c1Items = await context.CourseItems
                        .Include(ci => ci.Quiz).ThenInclude(q => q!.Questions).ThenInclude(qu => qu.Options)
                        .Where(ci => ci.Chapter.CourseId == course1.Id)
                        .ToListAsync();
                    foreach (var ci in c1Items)
                    {
                        if (ci.Quiz != null)
                        {
                            foreach (var q in ci.Quiz.Questions)
                            {
                                context.QuestionOptions.RemoveRange(q.Options);
                            }
                            context.Questions.RemoveRange(ci.Quiz.Questions);
                            context.Quizzes.Remove(ci.Quiz);
                        }
                        context.CourseItems.Remove(ci);
                    }
                    var c1Modules = await context.Chapters.Where(ch => ch.CourseId == course1.Id).ToListAsync();
                    context.Chapters.RemoveRange(c1Modules);
                    var c1Enrollments = await context.Enrollments.Where(e => e.CourseId == course1.Id).ToListAsync();
                    context.Enrollments.RemoveRange(c1Enrollments);
                    context.Courses.Remove(course1);
                }

                if (course2 != null)
                {
                    var c2Items = await context.CourseItems
                        .Include(ci => ci.Quiz).ThenInclude(q => q!.Questions).ThenInclude(qu => qu.Options)
                        .Where(ci => ci.Chapter.CourseId == course2.Id)
                        .ToListAsync();
                    foreach (var ci in c2Items)
                    {
                        if (ci.Quiz != null)
                        {
                            foreach (var q in ci.Quiz.Questions)
                            {
                                context.QuestionOptions.RemoveRange(q.Options);
                            }
                            context.Questions.RemoveRange(ci.Quiz.Questions);
                            context.Quizzes.Remove(ci.Quiz);
                        }
                        context.CourseItems.Remove(ci);
                    }
                    var c2Modules = await context.Chapters.Where(ch => ch.CourseId == course2.Id).ToListAsync();
                    context.Chapters.RemoveRange(c2Modules);
                    var c2Enrollments = await context.Enrollments.Where(e => e.CourseId == course2.Id).ToListAsync();
                    context.Enrollments.RemoveRange(c2Enrollments);
                    context.Courses.Remove(course2);
                }

                if (studentA != null) await userManager.DeleteAsync(studentA);
                if (studentB != null) await userManager.DeleteAsync(studentB);

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WARN] Cleanup error: {ex.Message}");
            }
        }

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"🎉 ALL {testsPassed}/{totalTests} QUIZ INTEGRATION TESTS PASSED CLEANLY!");
        Console.WriteLine("=================================================================\n");
    }
}
