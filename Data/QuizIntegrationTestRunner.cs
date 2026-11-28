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
using AUBDigitalAcademy.Services.Implementations;
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

            // Start Attempt #2 explicitly via StartNextAttempt before submitting
            await studentACtrl.StartNextAttempt(course1.Id, quizItem.Id);
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
            await studentACtrl.StartNextAttempt(course1.Id, quizItem.Id);
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

            // -------------------------------------------------------------
            // TEST 17: Draft Quiz Security (Unpublished Quiz Isolation)
            // -------------------------------------------------------------
            var draftQuizCreate = await curriculumService.CreateQuizAsync(new QuizCreateViewModel
            {
                CourseId = course1.Id,
                ChapterId = module1.Id,
                Title = "Draft Secret Quiz",
                TimeLimitMinutes = 15,
                PassingScore = 60.0,
                MaxAttempts = 2,
                IsPublished = false
            });
            if (!draftQuizCreate.IsSuccess || !draftQuizCreate.EntityId.HasValue)
            {
                Fail("Draft Quiz Security", $"Failed to create draft quiz: {draftQuizCreate.Message}");
                return;
            }
            int draftQuizItemId = draftQuizCreate.EntityId.Value;
            var draftQuizEntity = await context.Quizzes.FirstAsync(q => q.CourseItemId == draftQuizItemId);

            await curriculumService.AddQuestionAsync(draftQuizEntity.Id, new QuestionViewModel
            {
                QuizId = draftQuizEntity.Id,
                Text = "Secret Draft Question?",
                Points = 10,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "Option A", IsCorrect = true },
                    new() { Text = "Option B", IsCorrect = false }
                }
            });
            var draftOptionId = await context.QuestionOptions
                .Where(o => o.Question.QuizId == draftQuizEntity.Id && o.IsCorrect)
                .Select(o => o.Id)
                .FirstAsync();
            var draftQuestionId = await context.Questions
                .Where(q => q.QuizId == draftQuizEntity.Id)
                .Select(q => q.Id)
                .FirstAsync();

            // 17a: Draft quiz absent from student course overview & gradebook; published quiz remains visible
            var overviewRes = await studentACtrl.Course(course1.Id);
            var overviewVm = (overviewRes as ViewResult)?.Model as StudentCourseOverviewViewModel;
            var overviewItems = overviewVm?.Modules.SelectMany(m => m.Lessons).ToList() ?? new();
            bool draftHiddenInOverview = !overviewItems.Any(i => i.CourseItemId == draftQuizItemId);
            bool publishedVisibleInOverview = overviewItems.Any(i => i.CourseItemId == quizItem.Id);

            var gradesVm = await curriculumService.GetStudentGradesAsync(studentA.Id, course1.Id);
            bool draftHiddenInGrades = !gradesVm.GradeItems.Any(g => g.CourseItemId == draftQuizItemId);
            bool publishedVisibleInGrades = gradesVm.GradeItems.Any(g => g.CourseItemId == quizItem.Id);

            if (!draftHiddenInOverview || !publishedVisibleInOverview || !draftHiddenInGrades || !publishedVisibleInGrades)
            {
                Fail("Draft Quiz Security (Visibility)", $"OverviewHidden={draftHiddenInOverview}, OverviewPub={publishedVisibleInOverview}, GradesHidden={draftHiddenInGrades}, GradesPub={publishedVisibleInGrades}");
                return;
            }

            // 17b: Student direct access to draft quiz player (by CourseItemId and by QuizId) is rejected
            var draftPlayerByItem = await studentACtrl.Quiz(course1.Id, draftQuizItemId);
            var draftPlayerByQuizId = await studentACtrl.Quiz(null, draftQuizEntity.Id);
            if ((draftPlayerByItem is not NotFoundResult && draftPlayerByItem is not NotFoundObjectResult) ||
                (draftPlayerByQuizId is not NotFoundResult && draftPlayerByQuizId is not NotFoundObjectResult))
            {
                Fail("Draft Quiz Security (Player)", $"Expected NotFound for draft quiz player, got {draftPlayerByItem.GetType().Name} and {draftPlayerByQuizId.GetType().Name}");
                return;
            }

            // 17c: Student submission to draft quiz is rejected and creates no submissions or progress
            var draftSubmitRes = await studentACtrl.SubmitQuiz(course1.Id, draftQuizItemId, new Dictionary<int, int> { { draftQuestionId, draftOptionId } });
            if (draftSubmitRes is not NotFoundResult && draftSubmitRes is not NotFoundObjectResult && draftSubmitRes is not ForbidResult)
            {
                Fail("Draft Quiz Security (Submit)", $"Expected NotFound/Forbid for draft quiz submit, got {draftSubmitRes.GetType().Name}");
                return;
            }

            int draftSubCount = await context.QuizSubmissions.CountAsync(qs => qs.QuizId == draftQuizEntity.Id);
            int draftProgressCount = await context.CourseProgresses.CountAsync(cp => cp.CourseItemId == draftQuizItemId);
            if (draftSubCount != 0 || draftProgressCount != 0)
            {
                Fail("Draft Quiz Security (No Side Effects)", $"Expected 0 submissions and 0 progress, got Subs={draftSubCount}, Progress={draftProgressCount}");
                return;
            }

            // 17d: Faculty/Admin authoring access to draft quiz remains intact
            var facultyDraftEdit = await curriculumService.GetQuizAsync(draftQuizItemId);
            if (facultyDraftEdit == null || facultyDraftEdit.IsPublished)
            {
                Fail("Draft Quiz Security (Authoring)", "Faculty/Admin GetQuizAsync failed to load draft quiz.");
                return;
            }
            Pass("Draft Quiz Security: Draft quiz hidden from student listings/grades, player & submit rejected with no DB side effects, published & authoring access preserved");

            // -------------------------------------------------------------
            // TEST 18: Admin Preview Bypass (Read-Only)
            // -------------------------------------------------------------
            var previewLessonRes = await curriculumService.CreateLessonAsync(module1.Id, "Preview Lesson", "<p>Lesson body</p>", null, null, course1.Id);
            var previewBookRes = await curriculumService.CreateBookAsync(new BookCreateViewModel { CourseId = course1.Id, ChapterId = module1.Id, Title = "Preview Book", FileUrl = "/files/preview.pdf", FileType = "PDF" });
            var previewAssignRes = await curriculumService.CreateAssignmentAsync(new AssignmentCreateViewModel { CourseId = course1.Id, ChapterId = module1.Id, Title = "Preview Assignment", Instruction = "Submit work", MaxScore = 100 });
            if (!previewLessonRes.IsSuccess || !previewBookRes.IsSuccess || !previewAssignRes.IsSuccess)
            {
                Fail("Admin Preview Setup", "Failed to create Lesson/Book/Assignment fixtures.");
                return;
            }
            int pLessonItemId = previewLessonRes.EntityId!.Value;
            int pBookItemId = previewBookRes.EntityId!.Value;
            int pAssignItemId = previewAssignRes.EntityId!.Value;

            var adminUser = await userManager.FindByEmailAsync("admin@aub.com")
                ?? new User { UserName = $"admin-prev-{testRunId}@aub.edu", Email = $"admin-prev-{testRunId}@aub.edu", FullName = "Admin Previewer", UniversityId = $"ADM-{testRunId}" };
            bool createdTempAdmin = adminUser.Id == null || await userManager.FindByIdAsync(adminUser.Id) == null;
            if (createdTempAdmin)
            {
                await userManager.CreateAsync(adminUser, "AdminPass123!");
            }

            StudentController BuildAdminController(User admin)
            {
                var ctrl = new StudentController(enrollmentService, courseService, curriculumService, userManager, context, studentLogger);
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, admin.Id),
                    new(ClaimTypes.Name, admin.UserName!),
                    new(ClaimTypes.Role, "Admin")
                };
                ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) } };
                ctrl.TempData = new TempDataDictionary(ctrl.HttpContext, new DummyTempDataProvider());
                return ctrl;
            }

            var adminCtrl = BuildAdminController(adminUser);

            // 18a: Unenrolled Admin can preview Lesson, Book, Assignment, and Draft Quiz
            var adminLessonRes = await adminCtrl.Lesson(course1.Id, pLessonItemId);
            var adminBookRes = await adminCtrl.Book(course1.Id, pBookItemId);
            var adminAssignRes = await adminCtrl.Assignment(course1.Id, pAssignItemId);
            var adminDraftQuizRes = await adminCtrl.Quiz(course1.Id, draftQuizItemId);

            if (adminLessonRes is not ViewResult || adminBookRes is not ViewResult || adminAssignRes is not ViewResult || adminDraftQuizRes is not ViewResult)
            {
                Fail("Admin Preview Access", $"Expected ViewResult for all 4 previews, got Lesson={adminLessonRes.GetType().Name}, Book={adminBookRes.GetType().Name}, Assign={adminAssignRes.GetType().Name}, DraftQuiz={adminDraftQuizRes.GetType().Name}");
                return;
            }

            // 18b: Admin preview is strictly read-only (no CourseProgress, QuizSubmission, QuizSubmissionAnswer, or AssignmentSubmission)
            int adminProgressCount = await context.CourseProgresses.CountAsync(cp => cp.StudentId == adminUser.Id);
            int adminQuizSubCount = await context.QuizSubmissions.CountAsync(qs => qs.StudentId == adminUser.Id && qs.QuizId == draftQuizEntity.Id);
            int adminAssignSubCount = await context.AssignmentSubmissions.CountAsync(s => s.StudentId == adminUser.Id);
            if (adminProgressCount != 0 || adminQuizSubCount != 0 || adminAssignSubCount != 0)
            {
                Fail("Admin Preview Read-Only", $"Expected 0 mutations for Admin, got Progress={adminProgressCount}, QuizSubs={adminQuizSubCount}, AssignSubs={adminAssignSubCount}");
                return;
            }

            // 18c: Unenrolled Student B remains denied (403 Forbid) for Lesson, Book, Assignment, and Draft Quiz
            var studentB_Lesson = await studentBCtrl.Lesson(course1.Id, pLessonItemId);
            var studentB_Book = await studentBCtrl.Book(course1.Id, pBookItemId);
            var studentB_Assign = await studentBCtrl.Assignment(course1.Id, pAssignItemId);
            var studentB_DraftQuiz = await studentBCtrl.Quiz(course1.Id, draftQuizItemId);
            if (studentB_Lesson is not ForbidResult || studentB_Book is not ForbidResult || studentB_Assign is not ForbidResult || studentB_DraftQuiz is not ForbidResult)
            {
                Fail("Unenrolled Student Denial", $"Expected ForbidResult for unenrolled Student B, got Lesson={studentB_Lesson.GetType().Name}, Book={studentB_Book.GetType().Name}, Assign={studentB_Assign.GetType().Name}, Quiz={studentB_DraftQuiz.GetType().Name}");
                return;
            }

            if (createdTempAdmin)
            {
                await userManager.DeleteAsync(adminUser);
            }

            Pass("Admin Preview Bypass: Admin previews Lesson, Book, Assignment, and Draft Quiz without enrollment and without DB mutations; unenrolled Student B remains denied");

            // -------------------------------------------------------------
            // TEST 19: Edit Question & Server-Side Quiz Time Limit
            // -------------------------------------------------------------
            var updateQRes = await curriculumService.UpdateQuestionAsync(new QuestionViewModel
            {
                Id = draftQuestionId,
                QuizId = draftQuizEntity.Id,
                Text = "Updated Draft Question?",
                Points = 15.0,
                Order = 1,
                Type = QuestionType.MultipleChoice,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "Updated A", IsCorrect = false },
                    new() { Text = "Updated B", IsCorrect = true }
                }
            });
            var updatedQ = await curriculumService.GetQuestionByIdAsync(draftQuestionId);
            if (!updateQRes.IsSuccess || updatedQ == null || updatedQ.Text != "Updated Draft Question?" || updatedQ.Points != 15.0 ||
                !updatedQ.Options.Any(o => o.Text == "Updated B" && o.IsCorrect))
            {
                Fail("Edit Question", "UpdateQuestionAsync / GetQuestionByIdAsync failed to update question text, points, or correct option.");
                return;
            }

            // -------------------------------------------------------------
            // TEST 19: Persistent Server-Side Quiz Timer, Auto-Save, Expiration & Concurrency
            // -------------------------------------------------------------
            var timedQuizRes = await curriculumService.CreateQuizAsync(new QuizCreateViewModel
            {
                CourseId = course1.Id,
                ChapterId = module1.Id,
                Title = "Timed Quiz DB Persistence",
                TimeLimitMinutes = 10,
                PassingScore = 50.0,
                MaxAttempts = 2,
                IsPublished = true
            });
            int timedQuizItemId = timedQuizRes.EntityId!.Value;
            var timedQuizEntity = await context.Quizzes.FirstAsync(q => q.CourseItemId == timedQuizItemId);

            var tq1Res = await curriculumService.AddQuestionAsync(timedQuizEntity.Id, new QuestionViewModel
            {
                QuizId = timedQuizEntity.Id,
                Text = "Timed Q1 (2 pts)?",
                Points = 2.0,
                Order = 1,
                Type = QuestionType.MultipleChoice,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "Correct 1", IsCorrect = true },
                    new() { Text = "Wrong 1", IsCorrect = false }
                }
            });
            var tq2Res = await curriculumService.AddQuestionAsync(timedQuizEntity.Id, new QuestionViewModel
            {
                QuizId = timedQuizEntity.Id,
                Text = "Timed Q2 (3 pts)?",
                Points = 3.0,
                Order = 2,
                Type = QuestionType.MultipleChoice,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "Correct 2", IsCorrect = true },
                    new() { Text = "Wrong 2", IsCorrect = false }
                }
            });
            var tq1 = await context.Questions.Include(q => q.Options).FirstAsync(q => q.Id == tq1Res.EntityId!.Value);
            var tq2 = await context.Questions.Include(q => q.Options).FirstAsync(q => q.Id == tq2Res.EntityId!.Value);
            var tq1Correct = tq1.Options.First(o => o.IsCorrect);
            var tq2Correct = tq2.Options.First(o => o.IsCorrect);

            // 19a: Submitting via controller without starting an active attempt is rejected
            var unstartedSubmitRes = await studentACtrl.SubmitQuiz(course1.Id, timedQuizItemId, new Dictionary<int, int> { { tq1.Id, tq1Correct.Id } });
            int preStartSubCount = await context.QuizSubmissions.CountAsync(qs => qs.QuizId == timedQuizEntity.Id);
            if (unstartedSubmitRes is not RedirectToActionResult unstartedRta || unstartedRta.ActionName != nameof(StudentController.Quiz) || preStartSubCount != 0)
            {
                Fail("Server-Side Quiz Timer (Require Active Attempt)", "Direct POST without opening quiz should be rejected with 0 submissions.");
                return;
            }

            // 19b: Opening quiz persists StartedAtUtc and DeadlineUtc in SQL Server
            var firstLoad = (await studentACtrl.Quiz(course1.Id, timedQuizItemId) as ViewResult)?.Model as StudentQuizPlayerViewModel;
            if (firstLoad == null || !firstLoad.ActiveAttemptId.HasValue || !firstLoad.AttemptStartedAtUtc.HasValue ||
                !firstLoad.AttemptDeadlineUtc.HasValue || !firstLoad.RemainingSeconds.HasValue || firstLoad.RemainingSeconds.Value <= 0)
            {
                Fail("Server-Side Quiz Timer (Start)", "Expected persisted ActiveAttemptId, StartedAtUtc, DeadlineUtc, and RemainingSeconds > 0.");
                return;
            }
            var attempt1Id = firstLoad.ActiveAttemptId.Value;

            // Save Q1 pre-deadline via SaveQuizAnswer
            var saveQ1Res = await studentACtrl.SaveQuizAnswer(course1.Id, timedQuizItemId, attempt1Id, tq1.Id, tq1Correct.Id);
            if (saveQ1Res is not JsonResult)
            {
                Fail("Quiz Draft Answer Auto-Save", $"Expected JsonResult from SaveQuizAnswer, got {saveQ1Res.GetType().Name}.");
                return;
            }

            // 19c: Refreshing in a brand new DI scope (simulating restart / memory wipe) preserves identical StartedAtUtc, DeadlineUtc, and saved draft answers
            StudentQuizPlayerViewModel? refreshInNewScope;
            using (var scope2 = serviceProvider.CreateScope())
            {
                var curSvc2 = scope2.ServiceProvider.GetRequiredService<ICurriculumService>();
                refreshInNewScope = await curSvc2.GetStudentQuizPlayerAsync(course1.Id, timedQuizItemId, studentA.Id);
            }
            if (refreshInNewScope == null ||
                refreshInNewScope.ActiveAttemptId != attempt1Id ||
                refreshInNewScope.AttemptStartedAtUtc != firstLoad.AttemptStartedAtUtc ||
                refreshInNewScope.AttemptDeadlineUtc != firstLoad.AttemptDeadlineUtc ||
                !refreshInNewScope.SavedAnswers.TryGetValue(tq1.Id, out var restoredOptId) ||
                restoredOptId != tq1Correct.Id)
            {
                Fail("Server-Side Quiz Timer (Refresh Persistence)", "Refreshing across fresh scope altered StartedAtUtc/DeadlineUtc or lost saved draft answer.");
                return;
            }

            // 19d: Simulate expiration beyond 10m + 30s grace; late POST answers are rejected and ONLY pre-deadline Q1 (40%) is finalized
            var attempt1Db = await context.QuizAttempts.FirstAsync(qa => qa.Id == attempt1Id);
            attempt1Db.StartedAtUtc = DateTime.UtcNow.AddMinutes(-12);
            attempt1Db.DeadlineUtc = DateTime.UtcNow.AddMinutes(-2);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Student attempts late SubmitQuiz with both Q1 and Q2 (Q2 must be rejected!)
            var lateAnswers = new Dictionary<int, int>
            {
                { tq1.Id, tq1Correct.Id },
                { tq2.Id, tq2Correct.Id }
            };
            var expiredSubmitRes = await studentACtrl.SubmitQuiz(course1.Id, timedQuizItemId, lateAnswers, attempt1Id);
            var attempt1Reloaded = await context.QuizAttempts.AsNoTracking().FirstAsync(qa => qa.Id == attempt1Id);
            var finalizedExpSub = await context.QuizSubmissions.AsNoTracking().Include(qs => qs.Answers).FirstOrDefaultAsync(qs => qs.Id == attempt1Reloaded.QuizSubmissionId);

            if (expiredSubmitRes is not RedirectToActionResult expRta ||
                expRta.ActionName != nameof(StudentController.QuizResult) ||
                attempt1Reloaded.Status != QuizAttemptStatus.Expired ||
                finalizedExpSub == null ||
                !finalizedExpSub.IsAutoFinalized ||
                Math.Abs(finalizedExpSub.Score - 40.0) > 0.01)
            {
                Fail("Server-Side Quiz Timer (Expired Auto-Finalize)",
                    $"Expected QuizResult redirect, Expired status, IsAutoFinalized=true, and Score=40.0% from pre-deadline Q1 only. Actual Status={attempt1Reloaded.Status}, Score={finalizedExpSub?.Score}.");
                return;
            }

            // 19e: Repeated submission on already-finalized attempt is rejected and does NOT mutate Score or create duplicate submission
            var repeatSubmitRes = await curriculumService.SubmitQuizAsync(studentA.Id, course1.Id, timedQuizItemId, lateAnswers, attempt1Id, requireActiveAttempt: true);
            int subCountAfterRepeat = await context.QuizSubmissions.CountAsync(qs => qs.QuizId == timedQuizEntity.Id);
            if (repeatSubmitRes.Result.Status != CurriculumOperationStatus.Conflict || subCountAfterRepeat != 1)
            {
                Fail("Repeated Submission Protection", $"Expected Conflict and 1 submission, got Status={repeatSubmitRes.Result.Status}, Count={subCountAfterRepeat}.");
                return;
            }

            // 19f: Refresh after expiration does NOT auto-start Attempt #2; explicit StartNextAttempt starts Attempt #2; expiring Attempt #2 exhausts MaxAttempts (2/2)
            var preStartReload = (await studentACtrl.Quiz(course1.Id, timedQuizItemId) as ViewResult)?.Model as StudentQuizPlayerViewModel;
            if (preStartReload == null ||
                preStartReload.ActiveAttemptId.HasValue ||
                !preStartReload.RequiresExplicitNextAttemptStart ||
                preStartReload.AttemptCount != 1 ||
                preStartReload.RemainingAttempts != 1)
            {
                Fail("Refresh Does Not Auto-Start Retake",
                    $"Expected ActiveAttemptId=null and RequiresExplicitNextAttemptStart=true after prior submission, got Active={preStartReload?.ActiveAttemptId}, RequiresStart={preStartReload?.RequiresExplicitNextAttemptStart}.");
                return;
            }

            await studentACtrl.StartNextAttempt(course1.Id, timedQuizItemId);
            var attempt2Load = (await studentACtrl.Quiz(course1.Id, timedQuizItemId) as ViewResult)?.Model as StudentQuizPlayerViewModel;
            if (attempt2Load == null || !attempt2Load.ActiveAttemptId.HasValue || attempt2Load.CurrentAttemptNumber != 2)
            {
                Fail("Retake After Expired Attempt", "Expected Attempt #2 to start cleanly via explicit StartNextAttempt.");
                return;
            }
            var attempt2Db = await context.QuizAttempts.FirstAsync(qa => qa.Id == attempt2Load.ActiveAttemptId.Value);
            attempt2Db.StartedAtUtc = DateTime.UtcNow.AddMinutes(-15);
            attempt2Db.DeadlineUtc = DateTime.UtcNow.AddMinutes(-5);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var postExpiryReload = (await studentACtrl.Quiz(course1.Id, timedQuizItemId) as ViewResult)?.Model as StudentQuizPlayerViewModel;
            var attempt2Reloaded = await context.QuizAttempts.AsNoTracking().FirstAsync(qa => qa.Id == attempt2Load.ActiveAttemptId.Value);
            int subCountAfterExhaustion = await context.QuizSubmissions.CountAsync(qs => qs.QuizId == timedQuizEntity.Id);
            var blockedAttempt3Res = await studentACtrl.StartNextAttempt(course1.Id, timedQuizItemId);
            if (postExpiryReload == null ||
                postExpiryReload.CanAttempt ||
                postExpiryReload.ActiveAttemptId.HasValue ||
                postExpiryReload.RequiresExplicitNextAttemptStart ||
                postExpiryReload.AttemptCount != 2 ||
                postExpiryReload.RemainingAttempts != 0 ||
                !postExpiryReload.LatestIsAutoFinalized ||
                string.IsNullOrWhiteSpace(postExpiryReload.ExpiredAttemptNotice) ||
                attempt2Reloaded.Status != QuizAttemptStatus.Expired ||
                subCountAfterExhaustion != 2 ||
                blockedAttempt3Res is not ForbidResult)
            {
                Fail("Reload Auto-Finalize & MaxAttempts Exhaustion",
                    $"Expected CanAttempt=false, AttemptCount=2, ExpiredAttemptNotice set, StartNextAttempt Forbidden, got CanAttempt={postExpiryReload?.CanAttempt}, Count={postExpiryReload?.AttemptCount}, Subs={subCountAfterExhaustion}, BlockedType={blockedAttempt3Res.GetType().Name}.");
                return;
            }

            // 19g: Concurrent Start Race & Concurrent Submission Race + Grace Period
            var raceQuizRes = await curriculumService.CreateQuizAsync(new QuizCreateViewModel
            {
                CourseId = course1.Id,
                ChapterId = module1.Id,
                Title = "Concurrency & Grace Quiz",
                TimeLimitMinutes = 5,
                PassingScore = 50.0,
                MaxAttempts = 3,
                IsPublished = true
            });
            int raceQuizItemId = raceQuizRes.EntityId!.Value;
            var raceQuizEntity = await context.Quizzes.FirstAsync(q => q.CourseItemId == raceQuizItemId);
            var rq1Res = await curriculumService.AddQuestionAsync(raceQuizEntity.Id, new QuestionViewModel
            {
                QuizId = raceQuizEntity.Id,
                Text = "Concurrency Q1?",
                Points = 10.0,
                Order = 1,
                Type = QuestionType.MultipleChoice,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "Yes", IsCorrect = true },
                    new() { Text = "No", IsCorrect = false }
                }
            });
            var rq1 = await context.Questions.Include(q => q.Options).FirstAsync(q => q.Id == rq1Res.EntityId!.Value);
            var rq1Correct = rq1.Options.First(o => o.IsCorrect);

            // Concurrent Start (2 scopes call GetStudentQuizPlayerAsync simultaneously)
            var startTasks = new[]
            {
                Task.Run(async () =>
                {
                    using var s = serviceProvider.CreateScope();
                    return await s.ServiceProvider.GetRequiredService<ICurriculumService>().GetStudentQuizPlayerAsync(course1.Id, raceQuizItemId, studentA.Id);
                }),
                Task.Run(async () =>
                {
                    using var s = serviceProvider.CreateScope();
                    return await s.ServiceProvider.GetRequiredService<ICurriculumService>().GetStudentQuizPlayerAsync(course1.Id, raceQuizItemId, studentA.Id);
                })
            };
            var startResults = await Task.WhenAll(startTasks);
            int inProgressCount = await context.QuizAttempts.CountAsync(qa => qa.QuizId == raceQuizEntity.Id && qa.StudentId == studentA.Id && qa.Status == QuizAttemptStatus.InProgress);
            if (startResults[0]?.ActiveAttemptId == null || startResults[0]!.ActiveAttemptId != startResults[1]?.ActiveAttemptId || inProgressCount != 1)
            {
                Fail("Concurrent Quiz Attempt Start", $"Expected 1 shared ActiveAttemptId, got {startResults[0]?.ActiveAttemptId} vs {startResults[1]?.ActiveAttemptId}, InProgressCount={inProgressCount}.");
                return;
            }
            int raceAttempt1Id = startResults[0]!.ActiveAttemptId!.Value;

            // Grace period check: 10 seconds past DeadlineUtc (within 30s QuizGracePeriod) still succeeds
            var raceAttempt1Db = await context.QuizAttempts.FirstAsync(qa => qa.Id == raceAttempt1Id);
            raceAttempt1Db.StartedAtUtc = DateTime.UtcNow.AddMinutes(-5).AddSeconds(-10);
            raceAttempt1Db.DeadlineUtc = DateTime.UtcNow.AddSeconds(-10);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Concurrent Submit on the same active attempt
            var submitTasks = new[]
            {
                Task.Run(async () =>
                {
                    using var s = serviceProvider.CreateScope();
                    return await s.ServiceProvider.GetRequiredService<ICurriculumService>().SubmitQuizAsync(
                        studentA.Id, course1.Id, raceQuizItemId, new Dictionary<int, int> { { rq1.Id, rq1Correct.Id } }, raceAttempt1Id, requireActiveAttempt: true);
                }),
                Task.Run(async () =>
                {
                    using var s = serviceProvider.CreateScope();
                    return await s.ServiceProvider.GetRequiredService<ICurriculumService>().SubmitQuizAsync(
                        studentA.Id, course1.Id, raceQuizItemId, new Dictionary<int, int> { { rq1.Id, rq1Correct.Id } }, raceAttempt1Id, requireActiveAttempt: true);
                })
            };
            var submitResults = await Task.WhenAll(submitTasks);
            int successCount = submitResults.Count(r => r.Result.IsSuccess);
            int conflictCount = submitResults.Count(r => r.Result.Status == CurriculumOperationStatus.Conflict);
            int raceSubCount = await context.QuizSubmissions.CountAsync(qs => qs.QuizId == raceQuizEntity.Id);
            if (successCount != 1 || conflictCount != 1 || raceSubCount != 1)
            {
                Fail("Concurrent Quiz Submission & Grace Window",
                    $"Expected 1 Success + 1 Conflict + 1 DB submission, got Success={successCount}, Conflict={conflictCount}, DBSubs={raceSubCount}.");
                return;
            }

            // 19h: Frontend Quiz.cshtml timer hardening verification
            var quizViewPath = Path.Combine(
                scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().ContentRootPath,
                "Views", "Student", "Quiz.cshtml");
            var quizViewText = await System.IO.File.ReadAllTextAsync(quizViewPath);
            if (!quizViewText.Contains("data-deadline-utc") ||
                !quizViewText.Contains("data-remaining-seconds") ||
                !quizViewText.Contains("DateTime.SpecifyKind") ||
                !quizViewText.Contains("alert-permanent") ||
                !quizViewText.Contains("parseExplicitUtcTimestamp") ||
                !quizViewText.Contains("performance.now()") ||
                !quizViewText.Contains("lockQuizSubmission") ||
                quizViewText.Contains("Date.now()") ||
                quizViewText.Contains("alert("))
            {
                Fail("Frontend Quiz.cshtml Timer Hardening", "Quiz.cshtml missing server-anchored UTC/remaining-seconds/alert-permanent hardening or contains Date.now()/alert().");
                return;
            }

            Pass("Edit Question & Server-Side Quiz Timer: Persisted UTC timing, refresh/draft resilience, expiration auto-finalize, grace window, repeat/concurrent protection, and UI hardening verified");

            // -------------------------------------------------------------
            // TEST 20: UTC DateTimeKind Normalization & Browser Refresh Serialization
            // -------------------------------------------------------------
            var sampleUtc = new DateTime(2026, 11, 28, 8, 30, 0, DateTimeKind.Utc);
            var sampleUnspecified = new DateTime(sampleUtc.Ticks, DateTimeKind.Unspecified);
            var normalizedFromUtc = CurriculumService.EnsureUtcKind(sampleUtc);
            var normalizedFromUnspecified = CurriculumService.EnsureUtcKind(sampleUnspecified);
            var normalizedNullable = CurriculumService.EnsureUtcKind((DateTime?)sampleUnspecified);
            var normalizedNull = CurriculumService.EnsureUtcKind((DateTime?)null);
            if (normalizedFromUtc.Kind != DateTimeKind.Utc ||
                normalizedFromUtc.Ticks != sampleUtc.Ticks ||
                normalizedFromUnspecified.Kind != DateTimeKind.Utc ||
                normalizedFromUnspecified.Ticks != sampleUtc.Ticks ||
                !normalizedNullable.HasValue ||
                normalizedNullable.Value.Kind != DateTimeKind.Utc ||
                normalizedNullable.Value.Ticks != sampleUtc.Ticks ||
                normalizedNull.HasValue)
            {
                Fail("UTC Kind Helper Tick Preservation", "EnsureUtcKind altered timestamp ticks, failed to set DateTimeKind.Utc, or broke nullable semantics.");
                return;
            }

            var utcQuizRes = await curriculumService.CreateQuizAsync(new QuizCreateViewModel
            {
                CourseId = course1.Id,
                ChapterId = module1.Id,
                Title = "UTC Timer Serialization Quiz",
                TimeLimitMinutes = 15,
                PassingScore = 50.0,
                MaxAttempts = 3,
                IsPublished = true
            });
            int utcQuizItemId = utcQuizRes.EntityId!.Value;
            var utcQuizEntity = await context.Quizzes.FirstAsync(q => q.CourseItemId == utcQuizItemId);
            await curriculumService.AddQuestionAsync(utcQuizEntity.Id, new QuestionViewModel
            {
                QuizId = utcQuizEntity.Id,
                Text = "UTC Q1?",
                Points = 10.0,
                Order = 1,
                Type = QuestionType.MultipleChoice,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "True", IsCorrect = true },
                    new() { Text = "False", IsCorrect = false }
                }
            });

            var utcStartLoad = (await studentACtrl.Quiz(course1.Id, utcQuizItemId) as ViewResult)?.Model as StudentQuizPlayerViewModel;
            if (utcStartLoad == null ||
                !utcStartLoad.ActiveAttemptId.HasValue ||
                !utcStartLoad.AttemptStartedAtUtc.HasValue ||
                !utcStartLoad.AttemptDeadlineUtc.HasValue ||
                utcStartLoad.AttemptStartedAtUtc.Value.Kind != DateTimeKind.Utc ||
                utcStartLoad.AttemptDeadlineUtc.Value.Kind != DateTimeKind.Utc ||
                utcStartLoad.ServerNowUtc.Kind != DateTimeKind.Utc ||
                !utcStartLoad.AttemptDeadlineUtc.Value.ToString("O").EndsWith("Z") ||
                !utcStartLoad.ServerNowUtc.ToString("O").EndsWith("Z") ||
                !utcStartLoad.RemainingSeconds.HasValue ||
                utcStartLoad.RemainingSeconds.Value <= 800)
            {
                Fail("UTC Quiz Start Kind & Format", "Newly started attempt timestamps must use DateTimeKind.Utc, emit 'Z' suffix in ToString(\"O\"), and report positive RemainingSeconds.");
                return;
            }

            int utcAttemptId = utcStartLoad.ActiveAttemptId.Value;

            // Reload directly from SQL Server in a fresh scope to verify EF Core ValueConverter restores DateTimeKind.Utc and exact ticks
            using (var scopeUtc1 = serviceProvider.CreateScope())
            {
                var dbUtc1 = scopeUtc1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var reloadedRow = await dbUtc1.QuizAttempts.AsNoTracking().FirstAsync(qa => qa.Id == utcAttemptId);
                if (reloadedRow.StartedAtUtc.Kind != DateTimeKind.Utc ||
                    reloadedRow.StartedAtUtc.Ticks != utcStartLoad.AttemptStartedAtUtc.Value.Ticks ||
                    !reloadedRow.DeadlineUtc.HasValue ||
                    reloadedRow.DeadlineUtc.Value.Kind != DateTimeKind.Utc ||
                    reloadedRow.DeadlineUtc.Value.Ticks != utcStartLoad.AttemptDeadlineUtc.Value.Ticks ||
                    reloadedRow.FinalizedAtUtc.HasValue)
                {
                    Fail("SQL Server QuizAttempt UTC Reload", "Reloading QuizAttempt from SQL Server did not preserve ticks with DateTimeKind.Utc and null FinalizedAtUtc.");
                    return;
                }
            }

            // Simulate legacy/raw SQL Server datetime2 row written without EF write converter (Unspecified ticks)
            // and verify reload interprets them as UTC without offset shift.
            var expectedStartUtc = DateTime.UtcNow.AddMinutes(-2);
            var expectedDeadlineUtc = DateTime.UtcNow.AddMinutes(13);
            var unspecStart = new DateTime(expectedStartUtc.Ticks, DateTimeKind.Unspecified);
            var unspecDeadline = new DateTime(expectedDeadlineUtc.Ticks, DateTimeKind.Unspecified);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE QuizAttempts SET StartedAtUtc = {unspecStart}, DeadlineUtc = {unspecDeadline} WHERE Id = {utcAttemptId}");
            context.ChangeTracker.Clear();

            using (var scopeUtc2 = serviceProvider.CreateScope())
            {
                var dbUtc2 = scopeUtc2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var curSvcUtc2 = scopeUtc2.ServiceProvider.GetRequiredService<ICurriculumService>();
                var reloadedAfterUnspec = await dbUtc2.QuizAttempts.AsNoTracking().FirstAsync(qa => qa.Id == utcAttemptId);
                var refreshPlayer = await curSvcUtc2.GetStudentQuizPlayerAsync(course1.Id, utcQuizItemId, studentA.Id);

                if (reloadedAfterUnspec.StartedAtUtc.Kind != DateTimeKind.Utc ||
                    reloadedAfterUnspec.StartedAtUtc.Ticks != expectedStartUtc.Ticks ||
                    !reloadedAfterUnspec.DeadlineUtc.HasValue ||
                    reloadedAfterUnspec.DeadlineUtc.Value.Kind != DateTimeKind.Utc ||
                    reloadedAfterUnspec.DeadlineUtc.Value.Ticks != expectedDeadlineUtc.Ticks ||
                    refreshPlayer == null ||
                    !refreshPlayer.AttemptStartedAtUtc.HasValue ||
                    refreshPlayer.AttemptStartedAtUtc.Value.Kind != DateTimeKind.Utc ||
                    refreshPlayer.AttemptStartedAtUtc.Value.Ticks != expectedStartUtc.Ticks ||
                    !refreshPlayer.AttemptDeadlineUtc.HasValue ||
                    refreshPlayer.AttemptDeadlineUtc.Value.Kind != DateTimeKind.Utc ||
                    refreshPlayer.AttemptDeadlineUtc.Value.Ticks != expectedDeadlineUtc.Ticks ||
                    !refreshPlayer.AttemptDeadlineUtc.Value.ToString("O").EndsWith("Z") ||
                    !refreshPlayer.ServerNowUtc.ToString("O").EndsWith("Z") ||
                    !refreshPlayer.RemainingSeconds.HasValue ||
                    refreshPlayer.RemainingSeconds.Value <= 700)
                {
                    Fail("Quiz Refresh UTC Preservation", "Refreshing active attempt with Unspecified DB timestamps failed to preserve ticks/Utc kind or returned non-positive RemainingSeconds.");
                    return;
                }
            }

            // Genuinely expired attempt: within grace window shows 0s; past grace window auto-finalizes with UTC FinalizedAtUtc
            var attemptGraceDb = await context.QuizAttempts.FirstAsync(qa => qa.Id == utcAttemptId);
            attemptGraceDb.DeadlineUtc = DateTime.UtcNow.AddSeconds(-10);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            using (var scopeGrace = serviceProvider.CreateScope())
            {
                var gracePlayer = await scopeGrace.ServiceProvider.GetRequiredService<ICurriculumService>()
                    .GetStudentQuizPlayerAsync(course1.Id, utcQuizItemId, studentA.Id);
                if (gracePlayer == null || !gracePlayer.RemainingSeconds.HasValue || gracePlayer.RemainingSeconds.Value != 0 ||
                    gracePlayer.ActiveAttemptId != utcAttemptId)
                {
                    Fail("Genuinely Expired (Grace Window) RemainingSeconds", "Attempt past DeadlineUtc inside grace window must report RemainingSeconds == 0.");
                    return;
                }
            }

            var attemptExpiredDb = await context.QuizAttempts.FirstAsync(qa => qa.Id == utcAttemptId);
            attemptExpiredDb.DeadlineUtc = DateTime.UtcNow.AddSeconds(-45);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            using (var scopeExpired = serviceProvider.CreateScope())
            {
                var curSvcExp = scopeExpired.ServiceProvider.GetRequiredService<ICurriculumService>();
                var dbExp = scopeExpired.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var expiredPlayer = await curSvcExp.GetStudentQuizPlayerAsync(course1.Id, utcQuizItemId, studentA.Id);
                var finalizedAttempt = await dbExp.QuizAttempts.AsNoTracking().FirstAsync(qa => qa.Id == utcAttemptId);

                if (expiredPlayer == null ||
                    string.IsNullOrWhiteSpace(expiredPlayer.ExpiredAttemptNotice) ||
                    finalizedAttempt.Status != QuizAttemptStatus.Expired ||
                    !finalizedAttempt.FinalizedAtUtc.HasValue ||
                    finalizedAttempt.FinalizedAtUtc.Value.Kind != DateTimeKind.Utc)
                {
                    Fail("Genuinely Expired Auto-Finalize & FinalizedAtUtc Kind",
                        $"Expected ExpiredAttemptNotice, Expired status, and DateTimeKind.Utc FinalizedAtUtc, got Status={finalizedAttempt.Status}, FinalizedKind={finalizedAttempt.FinalizedAtUtc?.Kind}.");
                    return;
                }
            }

            Pass("Quiz Timer UTC Serialization: Verified DateTimeKind.Utc across Start/SQL Reload/FinalizedAtUtc, tick preservation, future RemainingSeconds on refresh, genuine expiration, and UI checks");

            // -------------------------------------------------------------
            // TEST 21: Explicit Start Next Attempt After Expiration & Concurrency
            // -------------------------------------------------------------
            var expFlowQuizRes = await curriculumService.CreateQuizAsync(new QuizCreateViewModel
            {
                CourseId = course1.Id,
                ChapterId = module1.Id,
                Title = "Explicit Next Attempt Quiz",
                TimeLimitMinutes = 2,
                PassingScore = 50.0,
                MaxAttempts = 3,
                IsPublished = true
            });
            int expFlowItemId = expFlowQuizRes.EntityId!.Value;
            var expFlowQuizEntity = await context.Quizzes.FirstAsync(q => q.CourseItemId == expFlowItemId);

            var eq1Res = await curriculumService.AddQuestionAsync(expFlowQuizEntity.Id, new QuestionViewModel
            {
                QuizId = expFlowQuizEntity.Id,
                Text = "Explicit Flow Q1?",
                Points = 1.0,
                Order = 1,
                Type = QuestionType.MultipleChoice,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "Correct", IsCorrect = true },
                    new() { Text = "Wrong", IsCorrect = false }
                }
            });
            var eq2Res = await curriculumService.AddQuestionAsync(expFlowQuizEntity.Id, new QuestionViewModel
            {
                QuizId = expFlowQuizEntity.Id,
                Text = "Explicit Flow Q2?",
                Points = 1.0,
                Order = 2,
                Type = QuestionType.MultipleChoice,
                Options = new List<QuestionOptionViewModel>
                {
                    new() { Text = "Wrong", IsCorrect = false },
                    new() { Text = "Correct", IsCorrect = true }
                }
            });
            var eq1 = await context.Questions.Include(q => q.Options).FirstAsync(q => q.Id == eq1Res.EntityId!.Value);
            var eq2 = await context.Questions.Include(q => q.Options).FirstAsync(q => q.Id == eq2Res.EntityId!.Value);
            var eq1Correct = eq1.Options.First(o => o.IsCorrect);

            // 21a: Initial GET starts Attempt #1 and saves 1 of 2 answers pre-deadline
            var expStart1 = (await studentACtrl.Quiz(course1.Id, expFlowItemId) as ViewResult)?.Model as StudentQuizPlayerViewModel;
            if (expStart1 == null || !expStart1.ActiveAttemptId.HasValue || expStart1.CurrentAttemptNumber != 1)
            {
                Fail("Explicit Flow Initial Start", "Expected initial Quiz GET to create Attempt #1.");
                return;
            }
            int expAttempt1Id = expStart1.ActiveAttemptId.Value;
            await studentACtrl.SaveQuizAnswer(course1.Id, expFlowItemId, expAttempt1Id, eq1.Id, eq1Correct.Id);

            // 21b: Grace window check: 10s past DeadlineUtc still keeps Attempt #1 InProgress & blocks premature StartNextAttempt
            var expAttempt1Db = await context.QuizAttempts.FirstAsync(qa => qa.Id == expAttempt1Id);
            expAttempt1Db.StartedAtUtc = DateTime.UtcNow.AddMinutes(-2).AddSeconds(-10);
            expAttempt1Db.DeadlineUtc = DateTime.UtcNow.AddSeconds(-10);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var graceStartAttempt = await curriculumService.StartNextQuizAttemptAsync(studentA.Id, course1.Id, expFlowItemId);
            if (graceStartAttempt.Result.Status != CurriculumOperationStatus.Conflict)
            {
                Fail("StartNextAttempt Respects Grace Window", $"Expected Conflict inside 30s grace window, got {graceStartAttempt.Result.Status}.");
                return;
            }

            // 21c: Past 30s grace window (40s expired) -> multiple GET refreshes finalize Attempt #1, preserve result, and NEVER auto-create Attempt #2
            expAttempt1Db = await context.QuizAttempts.FirstAsync(qa => qa.Id == expAttempt1Id);
            expAttempt1Db.StartedAtUtc = DateTime.UtcNow.AddMinutes(-2).AddSeconds(-40);
            expAttempt1Db.DeadlineUtc = DateTime.UtcNow.AddSeconds(-40);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var refreshAfterExp1 = (await studentACtrl.Quiz(course1.Id, expFlowItemId) as ViewResult)?.Model as StudentQuizPlayerViewModel;
            var refreshAfterExp2 = (await studentACtrl.Quiz(course1.Id, expFlowItemId) as ViewResult)?.Model as StudentQuizPlayerViewModel;
            int attemptCountAfterRefreshes = await context.QuizAttempts.CountAsync(qa => qa.QuizId == expFlowQuizEntity.Id && qa.StudentId == studentA.Id);
            var finalizedExp1 = await context.QuizAttempts.AsNoTracking().FirstAsync(qa => qa.Id == expAttempt1Id);
            var subExp1 = await context.QuizSubmissions.AsNoTracking().FirstOrDefaultAsync(qs => qs.Id == finalizedExp1.QuizSubmissionId);

            if (refreshAfterExp1 == null ||
                refreshAfterExp2 == null ||
                attemptCountAfterRefreshes != 1 ||
                finalizedExp1.Status != QuizAttemptStatus.Expired ||
                !finalizedExp1.FinalizedAtUtc.HasValue ||
                subExp1 == null ||
                !subExp1.IsAutoFinalized ||
                Math.Abs(subExp1.Score - 50.0) > 0.01 ||
                refreshAfterExp1.ActiveAttemptId.HasValue ||
                refreshAfterExp2.ActiveAttemptId.HasValue ||
                !refreshAfterExp1.RequiresExplicitNextAttemptStart ||
                !refreshAfterExp2.RequiresExplicitNextAttemptStart ||
                !refreshAfterExp2.LatestIsAutoFinalized ||
                refreshAfterExp2.LatestCorrectQuestionsCount != 1 ||
                refreshAfterExp2.LatestTotalQuestionsCount != 2 ||
                refreshAfterExp2.RemainingAttempts != 2 ||
                refreshAfterExp2.RemainingSeconds.HasValue)
            {
                Fail("Refresh After Expiration Preserves Result Without Creating Attempt #2",
                    $"AttemptCount={attemptCountAfterRefreshes}, Status={finalizedExp1.Status}, SubAuto={subExp1?.IsAutoFinalized}, Score={subExp1?.Score}, Refresh2Active={refreshAfterExp2?.ActiveAttemptId}, Correct={refreshAfterExp2?.LatestCorrectQuestionsCount}/{refreshAfterExp2?.LatestTotalQuestionsCount}.");
                return;
            }

            // 21d: Concurrent StartNextAttempt (simulating double-click) creates exactly ONE Attempt #2 with fresh server deadline & returns JSON
            var concurrentStartTasks = new[]
            {
                Task.Run(async () =>
                {
                    using var s = serviceProvider.CreateScope();
                    return await s.ServiceProvider.GetRequiredService<ICurriculumService>().StartNextQuizAttemptAsync(studentA.Id, course1.Id, expFlowItemId);
                }),
                Task.Run(async () =>
                {
                    using var s = serviceProvider.CreateScope();
                    return await s.ServiceProvider.GetRequiredService<ICurriculumService>().StartNextQuizAttemptAsync(studentA.Id, course1.Id, expFlowItemId);
                })
            };
            var concurrentStartResults = await Task.WhenAll(concurrentStartTasks);
            int totalAfterStart2 = await context.QuizAttempts.CountAsync(qa => qa.QuizId == expFlowQuizEntity.Id && qa.StudentId == studentA.Id);
            int activeAfterStart2 = await context.QuizAttempts.CountAsync(qa => qa.QuizId == expFlowQuizEntity.Id && qa.StudentId == studentA.Id && qa.Status == QuizAttemptStatus.InProgress);

            var startRes0 = concurrentStartResults[0].Attempt;
            var startRes1 = concurrentStartResults[1].Attempt;
            if (!concurrentStartResults[0].Result.IsSuccess ||
                !concurrentStartResults[1].Result.IsSuccess ||
                startRes0 == null ||
                startRes1 == null ||
                startRes0.Id != startRes1.Id ||
                startRes0.AttemptNumber != 2 ||
                startRes0.DeadlineUtc is not DateTime attempt2DeadlineUtc ||
                attempt2DeadlineUtc <= DateTime.UtcNow ||
                totalAfterStart2 != 2 ||
                activeAfterStart2 != 1)
            {
                Fail("Concurrent StartNextAttempt Deduplication",
                    $"Expected 1 shared Attempt #2, got Id1={startRes0?.Id}, Id2={startRes1?.Id}, Total={totalAfterStart2}, Active={activeAfterStart2}.");
                return;
            }

            // Calling StartNextAttempt again (JSON mode) while Attempt #2 is InProgress resumes Attempt #2 instead of creating Attempt #3
            studentACtrl.ControllerContext.HttpContext.Request.Headers["Accept"] = "application/json";
            var jsonResumeRes = await studentACtrl.StartNextAttempt(course1.Id, expFlowItemId) as JsonResult;
            studentACtrl.ControllerContext.HttpContext.Request.Headers.Remove("Accept");
            int totalAfterResume = await context.QuizAttempts.CountAsync(qa => qa.QuizId == expFlowQuizEntity.Id && qa.StudentId == studentA.Id);
            if (jsonResumeRes == null || totalAfterResume != 2)
            {
                Fail("StartNextAttempt Resumes Active Attempt", $"Expected JsonResult and Total=2 attempts, got {jsonResumeRes?.GetType().Name}, Total={totalAfterResume}.");
                return;
            }

            // 21e: Complete Attempt #2 and Attempt #3, then verify MaxAttempts blocks Attempt #4 + unpublished/unenrolled quizzes blocked
            await studentACtrl.SubmitQuiz(course1.Id, expFlowItemId, new Dictionary<int, int> { { eq1.Id, eq1Correct.Id } }, concurrentStartResults[0].Attempt!.Id);
            var start3CtrlRes = await studentACtrl.StartNextAttempt(course1.Id, expFlowItemId);
            var load3Model = (await studentACtrl.Quiz(course1.Id, expFlowItemId) as ViewResult)?.Model as StudentQuizPlayerViewModel;
            if (start3CtrlRes is not RedirectToActionResult || load3Model == null || !load3Model.ActiveAttemptId.HasValue || load3Model.CurrentAttemptNumber != 3)
            {
                Fail("Explicit Start Attempt #3", "Expected Attempt #3 to start explicitly.");
                return;
            }
            await studentACtrl.SubmitQuiz(course1.Id, expFlowItemId, new Dictionary<int, int> { { eq1.Id, eq1Correct.Id } }, load3Model.ActiveAttemptId.Value);

            var exhaustedReload = (await studentACtrl.Quiz(course1.Id, expFlowItemId) as ViewResult)?.Model as StudentQuizPlayerViewModel;
            var start4Blocked = await studentACtrl.StartNextAttempt(course1.Id, expFlowItemId);
            var unpublishedBlocked = await studentACtrl.StartNextAttempt(course1.Id, draftQuizItemId);
            var unenrolledBlocked = await studentBCtrl.StartNextAttempt(course1.Id, expFlowItemId);

            if (exhaustedReload == null ||
                exhaustedReload.CanAttempt ||
                exhaustedReload.RequiresExplicitNextAttemptStart ||
                exhaustedReload.RemainingAttempts != 0 ||
                start4Blocked is not ForbidResult ||
                unpublishedBlocked is not NotFoundObjectResult ||
                unenrolledBlocked is not ForbidResult)
            {
                Fail("MaxAttempts & Access Enforcement on StartNextAttempt",
                    $"Expected CanAttempt=false, start4=Forbid, draft=NotFound, unenrolled=Forbid; got start4={start4Blocked.GetType().Name}, draft={unpublishedBlocked.GetType().Name}, unenrolled={unenrolledBlocked.GetType().Name}.");
                return;
            }

            // 21f: Verify Quiz.cshtml UI markup for Start Next Attempt
            var latestQuizViewText = await System.IO.File.ReadAllTextAsync(quizViewPath);
            if (!latestQuizViewText.Contains("btnStartNextAttempt") ||
                !latestQuizViewText.Contains("startNextAttemptForm") ||
                !latestQuizViewText.Contains("StartNextAttempt") ||
                !latestQuizViewText.Contains("RequiresExplicitNextAttemptStart") ||
                !latestQuizViewText.Contains("RemainingAttempts") ||
                !latestQuizViewText.Contains("LatestCorrectQuestionsCount"))
            {
                Fail("Quiz.cshtml Start Next Attempt UI Verification", "Quiz.cshtml missing required Start Next Attempt form/button/summary markers.");
                return;
            }

            Pass("Explicit Start Next Attempt: Grace window, non-mutating refresh, result summary, double-click/concurrency deduplication, active resume, MaxAttempts, and security verified");
        }
        finally
        {
            // Cleanup test data
            try
            {
                context.ChangeTracker.Clear();
                var testStudentIds = new[] { studentA?.Id, studentB?.Id }.Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToList();
                var testCourseIds = new[] { course1?.Id ?? 0, course2?.Id ?? 0 }.Where(id => id > 0).ToList();

                if (testStudentIds.Any())
                {
                    await context.QuizAttemptAnswers.Where(a => testStudentIds.Contains(a.QuizAttempt.StudentId)).ExecuteDeleteAsync();
                    await context.QuizAttempts.Where(qa => testStudentIds.Contains(qa.StudentId)).ExecuteDeleteAsync();
                    await context.QuizSubmissionAnswers.Where(a => testStudentIds.Contains(a.QuizSubmission.StudentId)).ExecuteDeleteAsync();
                    await context.QuizSubmissions.Where(qs => testStudentIds.Contains(qs.StudentId)).ExecuteDeleteAsync();
                }

                if (testCourseIds.Any())
                {
                    await context.QuizAttemptAnswers.Where(a => testCourseIds.Contains(a.QuizAttempt.Quiz.CourseItem.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.QuizAttempts.Where(qa => testCourseIds.Contains(qa.Quiz.CourseItem.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.QuizSubmissionAnswers.Where(a => testCourseIds.Contains(a.QuizSubmission.Quiz.CourseItem.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.QuizSubmissions.Where(qs => testCourseIds.Contains(qs.Quiz.CourseItem.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.QuestionOptions.Where(o => testCourseIds.Contains(o.Question.Quiz.CourseItem.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.Questions.Where(q => testCourseIds.Contains(q.Quiz.CourseItem.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.Quizzes.Where(q => testCourseIds.Contains(q.CourseItem.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.Lessons.Where(l => testCourseIds.Contains(l.CourseItem.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.Books.Where(b => testCourseIds.Contains(b.CourseItem.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.Assignments.Where(a => testCourseIds.Contains(a.CourseItem.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.CourseItems.Where(ci => testCourseIds.Contains(ci.Chapter.CourseId)).ExecuteDeleteAsync();
                    await context.Chapters.Where(ch => testCourseIds.Contains(ch.CourseId)).ExecuteDeleteAsync();
                    await context.Enrollments.Where(e => testCourseIds.Contains(e.CourseId)).ExecuteDeleteAsync();
                    await context.Courses.Where(c => testCourseIds.Contains(c.Id)).ExecuteDeleteAsync();
                }

                if (studentA != null)
                {
                    var sA = await userManager.FindByIdAsync(studentA.Id);
                    if (sA != null) await userManager.DeleteAsync(sA);
                }
                if (studentB != null)
                {
                    var sB = await userManager.FindByIdAsync(studentB.Id);
                    if (sB != null) await userManager.DeleteAsync(sB);
                }
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
