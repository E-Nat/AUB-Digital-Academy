using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AUBDigitalAcademy.Controllers;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class TeacherPortalTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var teacherService = scope.ServiceProvider.GetRequiredService<ITeacherService>();

        Console.WriteLine("\n=================================================");
        Console.WriteLine("  STEP 4: TEACHER PORTAL INTEGRATION TEST SUITE  ");
        Console.WriteLine("=================================================\n");

        int passed = 0;
        int failed = 0;

        // Ensure Roles Exist
        foreach (var roleName in new[] { "Admin", "Teacher", "Assistance", "Student" })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        // Setup Test Seed Users: Teacher A, Teacher B, Student 1
        var timestamp = DateTime.UtcNow.Ticks.ToString().Substring(10);
        
        var teacherA = new User
        {
            UserName = $"teacherA_{timestamp}@aub.edu.kh",
            Email = $"teacherA_{timestamp}@aub.edu.kh",
            FullName = "Prof. Alice Smith",
            UniversityId = $"TCH_A_{timestamp}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(teacherA, "Teacher@123");
        await userManager.AddToRoleAsync(teacherA, "Teacher");

        var teacherB = new User
        {
            UserName = $"teacherB_{timestamp}@aub.edu.kh",
            Email = $"teacherB_{timestamp}@aub.edu.kh",
            FullName = "Prof. Bob Jones",
            UniversityId = $"TCH_B_{timestamp}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(teacherB, "Teacher@123");
        await userManager.AddToRoleAsync(teacherB, "Teacher");

        var student1 = new User
        {
            UserName = $"student1_{timestamp}@aub.edu.kh",
            Email = $"student1_{timestamp}@aub.edu.kh",
            FullName = "Charlie Student",
            UniversityId = $"STU_1_{timestamp}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(student1, "Student@123");
        await userManager.AddToRoleAsync(student1, "Student");

        var taUser = new User
        {
            UserName = $"ta_{timestamp}@aub.edu.kh",
            Email = $"ta_{timestamp}@aub.edu.kh",
            FullName = "Alex Assistant",
            UniversityId = $"TA_{timestamp}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(taUser, "Teacher@123");
        await userManager.AddToRoleAsync(taUser, "Assistance");

        // Seed Category, Major, Semester
        var category = await context.Categories.FirstOrDefaultAsync();
        if (category == null)
        {
            category = new Category { Name = $"Category_{timestamp}" };
            context.Categories.Add(category);
            await context.SaveChangesAsync();
        }

        var major = await context.Majors.FirstOrDefaultAsync(m => m.CategoryId == category.Id);
        if (major == null)
        {
            major = new Major { Code = $"MJR_{timestamp}", Name = "Computer Science", CategoryId = category.Id };
            context.Majors.Add(major);
            await context.SaveChangesAsync();
        }

        var semester = await context.Semesters.FirstOrDefaultAsync();
        if (semester == null)
        {
            semester = new Semester { Code = $"SEM_{timestamp}", Name = "Spring 2026", StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddMonths(4), IsCurrent = true };
            context.Semesters.Add(semester);
            await context.SaveChangesAsync();
        }

        // Seed Courses: Course A for Teacher A, Course B for Teacher B
        var courseA = new Course
        {
            Code = $"CS_A_{timestamp}",
            Title = "Teacher A Advanced Algorithms",
            Description = "Algorithms course taught by Teacher A",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacherA.Id,
            Status = CourseStatus.Published
        };
        context.Courses.Add(courseA);

        var courseB = new Course
        {
            Code = $"CS_B_{timestamp}",
            Title = "Teacher B Database Systems",
            Description = "Databases course taught by Teacher B",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacherB.Id,
            Status = CourseStatus.Published
        };
        context.Courses.Add(courseB);
        await context.SaveChangesAsync();

        var assistantA = new CourseAssistant
        {
            CourseId = courseA.Id,
            AssistantId = taUser.Id,
            CanGrade = true,
            AssignedAt = DateTime.UtcNow
        };
        context.CourseAssistants.Add(assistantA);
        await context.SaveChangesAsync();

        // Enroll Student 1 in Course A
        var enrollment = new Enrollment
        {
            StudentId = student1.Id,
            CourseId = courseA.Id,
            EnrolledAt = DateTime.UtcNow
        };
        context.Enrollments.Add(enrollment);

        // Add Chapter & CourseItem for Course A
        var chapterA = new Chapter { CourseId = courseA.Id, Title = "Chapter 1: Basics", Position = 1 };
        context.Chapters.Add(chapterA);
        await context.SaveChangesAsync();

        var itemAssignment = new CourseItem { ChapterId = chapterA.Id, Title = "Assignment 1: Sorting Algorithms", Type = ItemType.Assignment, Position = 1 };
        context.CourseItems.Add(itemAssignment);
        await context.SaveChangesAsync();

        // Create Assignment & Student Submission
        var assignment = new Assignment
        {
            CourseItemId = itemAssignment.Id,
            Instruction = "Implement QuickSort and MergeSort in C#",
            MaxScore = 100.0,
            DueDate = DateTime.UtcNow.AddDays(7)
        };
        context.Assignments.Add(assignment);
        await context.SaveChangesAsync();

        var submission = new AssignmentSubmission
        {
            AssignmentId = assignment.Id,
            StudentId = student1.Id,
            TextResponse = "QuickSort code uploaded to github repo",
            FileUrl = "https://aub.edu.kh/uploads/quicksort.zip",
            Status = SubmissionStatus.Submitted,
            SubmittedAt = DateTime.UtcNow
        };
        context.AssignmentSubmissions.Add(submission);
        await context.SaveChangesAsync();

        try
        {
            // TEST 1: Role check for Teacher A
            var rolesA = await userManager.GetRolesAsync(teacherA);
            if (rolesA.Contains("Teacher"))
            {
                Console.WriteLine("  [PASS] Test 1: Teacher A assigned 'Teacher' identity role correctly");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 1: Teacher A missing 'Teacher' role");
                failed++;
            }

            // TEST 2: Role check for Student 1
            var rolesStudent = await userManager.GetRolesAsync(student1);
            if (!rolesStudent.Contains("Teacher") && rolesStudent.Contains("Student"))
            {
                Console.WriteLine("  [PASS] Test 2: Student 1 has 'Student' role and is NOT authorized as 'Teacher'");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 2: Student role verification failed");
                failed++;
            }

            // TEST 3: GetTeacherCoursesAsync for Teacher A
            var teacherACourses = (await teacherService.GetTeacherCoursesAsync(teacherA.Id)).ToList();
            if (teacherACourses.Count == 1 && teacherACourses[0].Id == courseA.Id)
            {
                Console.WriteLine($"  [PASS] Test 3: Teacher A sees only their assigned course '{courseA.Title}'");
                passed++;
            }
            else
            {
                Console.WriteLine($"  [FAIL] Test 3: Teacher A courses query mismatch (Count={teacherACourses.Count})");
                failed++;
            }

            // TEST 4: Course Ownership Safeguard: Teacher A blocked from Course B details
            var teacherACourseBDetails = await teacherService.GetTeacherCourseDetailsAsync(courseB.Id, teacherA.Id);
            if (teacherACourseBDetails == null)
            {
                Console.WriteLine("  [PASS] Test 4: Ownership Safeguard: Teacher A blocked from viewing Teacher B's course details");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 4: Teacher A was able to access Teacher B's course details!");
                failed++;
            }

            // TEST 5: Teacher A Course Details for Course A
            var teacherACourseADetails = await teacherService.GetTeacherCourseDetailsAsync(courseA.Id, teacherA.Id);
            if (teacherACourseADetails != null && teacherACourseADetails.StudentCount == 1)
            {
                Console.WriteLine($"  [PASS] Test 5: Teacher A can view details and student roster (Enrolled={teacherACourseADetails.StudentCount}) for Course A");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 5: Teacher A Course A details failed");
                failed++;
            }

            // TEST 6: Teacher Dashboard real metrics
            var dashboardA = await teacherService.GetTeacherDashboardAsync(teacherA.Id);
            if (dashboardA.ActiveCoursesCount == 1 && dashboardA.TotalStudentsCount == 1 && dashboardA.PendingReviewsCount == 1)
            {
                Console.WriteLine($"  [PASS] Test 6: Teacher Dashboard calculates real metrics (Courses={dashboardA.ActiveCoursesCount}, Students={dashboardA.TotalStudentsCount}, Pending={dashboardA.PendingReviewsCount})");
                passed++;
            }
            else
            {
                Console.WriteLine($"  [FAIL] Test 6: Dashboard metrics mismatch (Courses={dashboardA.ActiveCoursesCount}, Students={dashboardA.TotalStudentsCount}, Pending={dashboardA.PendingReviewsCount})");
                failed++;
            }

            // TEST 7: Teacher Assignments Query
            var assignmentsA = (await teacherService.GetTeacherAssignmentsAsync(teacherA.Id)).ToList();
            if (assignmentsA.Count == 1 && assignmentsA[0].Id == assignment.Id && assignmentsA[0].PendingSubmissions == 1)
            {
                Console.WriteLine("  [PASS] Test 7: Teacher A retrieves assignments and pending submission counts");
                passed++;
            }
            else
            {
                Console.WriteLine($"  [FAIL] Test 7: Teacher A assignments query failed (Count={assignmentsA.Count})");
                failed++;
            }

            // TEST 8: Assignment Submissions Query
            var submissionsA = (await teacherService.GetAssignmentSubmissionsAsync(assignment.Id, teacherA.Id)).ToList();
            if (submissionsA.Count == 1 && submissionsA[0].StudentId == student1.Id)
            {
                Console.WriteLine($"  [PASS] Test 8: Submissions for assignment {assignment.Id} retrieved cleanly for student '{student1.FullName}'");
                passed++;
            }
            else
            {
                Console.WriteLine($"  [FAIL] Test 8: Submissions query failed (Count={submissionsA.Count})");
                failed++;
            }

            // TEST 9: Grade Submission Workflow (Teacher A grades Student 1)
            var gradeResult = await teacherService.GradeSubmissionAsync(submission.Id, 95.5, "Excellent implementation of QuickSort!", teacherA.Id);
            if (gradeResult.Success)
            {
                Console.WriteLine("  [PASS] Test 9: GradeSubmissionAsync succeeded for Teacher A");
                passed++;
            }
            else
            {
                Console.WriteLine($"  [FAIL] Test 9: GradeSubmissionAsync failed: {gradeResult.Message}");
                failed++;
            }

            // TEST 10: Verify Submission Status & GradedById in SQL Server
            var updatedSubmission = await context.AssignmentSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submission.Id);
            if (updatedSubmission != null && updatedSubmission.Status == SubmissionStatus.Graded && updatedSubmission.Score == 95.5 && updatedSubmission.GradedById == teacherA.Id)
            {
                Console.WriteLine($"  [PASS] Test 10: SQL Server verified: Score=95.5, Status=Graded, GradedById={teacherA.Id}");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 10: SQL Server submission grade status mismatch");
                failed++;
            }

            // TEST 11: Unauthorized Grading Safeguard (Teacher B tries to grade Teacher A's submission)
            var unauthGradeResult = await teacherService.GradeSubmissionAsync(submission.Id, 50.0, "Hacked score", teacherB.Id);
            if (!unauthGradeResult.Success && unauthGradeResult.Message.Contains("Unauthorized"))
            {
                Console.WriteLine("  [PASS] Test 11: Unauthorized Grading Safeguard: Teacher B blocked from grading Teacher A's assignment submission");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 11: Teacher B was able to grade Teacher A's assignment!");
                failed++;
            }

            // TEST 12: Aggregated Teacher Grades Query
            var gradesLedger = (await teacherService.GetTeacherGradesAsync(teacherA.Id)).ToList();
            if (gradesLedger.Count == 1 && gradesLedger[0].Score == 95.5 && gradesLedger[0].IsPassed)
            {
                Console.WriteLine("  [PASS] Test 12: Teacher Grades ledger retrieves graded student submissions with pass status");
                passed++;
            }
            else
            {
                Console.WriteLine($"  [FAIL] Test 12: Teacher Grades query failed (Count={gradesLedger.Count})");
                failed++;
            }

            // TEST 13: Empty State Verification (Teacher B has no assignments or submissions)
            var teacherBAssignments = (await teacherService.GetTeacherAssignmentsAsync(teacherB.Id)).ToList();
            var teacherBGrades = (await teacherService.GetTeacherGradesAsync(teacherB.Id)).ToList();
            var teacherBDashboard = await teacherService.GetTeacherDashboardAsync(teacherB.Id);
            if (!teacherBAssignments.Any() && !teacherBGrades.Any() && teacherBDashboard.ActiveCoursesCount == 1 && teacherBDashboard.PendingReviewsCount == 0)
            {
                Console.WriteLine("  [PASS] Test 13: Empty State Verification: Teacher B with no submissions renders clean empty states without errors");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 13: Teacher B empty state check failed");
                failed++;
            }

            // TEST 14: Pending Reviews KPI (Late + Superseded + Multi-Student)
            var student2 = new User
            {
                UserName = $"student2_{timestamp}@aub.edu.kh",
                Email = $"student2_{timestamp}@aub.edu.kh",
                FullName = "Dana Student",
                UniversityId = $"STU_2_{timestamp}",
                EmailConfirmed = true,
                IsActive = true
            };
            await userManager.CreateAsync(student2, "Student@123");
            await userManager.AddToRoleAsync(student2, "Student");
            context.Enrollments.Add(new Enrollment { StudentId = student2.Id, CourseId = courseA.Id, EnrolledAt = DateTime.UtcNow });

            var itemAssign2 = new CourseItem { ChapterId = chapterA.Id, Title = "Assignment 2: KPI & Gradebook", Type = ItemType.Assignment, Position = 2 };
            var itemQuiz1 = new CourseItem { ChapterId = chapterA.Id, Title = "Quiz 1: Complexity", Type = ItemType.Quiz, Position = 3 };
            context.CourseItems.AddRange(itemAssign2, itemQuiz1);
            await context.SaveChangesAsync();

            var assign2 = new Assignment { CourseItemId = itemAssign2.Id, Instruction = "Analyze Big-O", MaxScore = 100.0, DueDate = DateTime.UtcNow.AddDays(3) };
            var quiz1 = new Quiz { CourseItemId = itemQuiz1.Id, TimeLimitMinutes = 20, PassingScore = 50.0, MaxAttempts = 3, IsPublished = true };
            context.Assignments.Add(assign2);
            context.Quizzes.Add(quiz1);
            await context.SaveChangesAsync();

            var baseTime = DateTime.UtcNow.AddHours(-3);
            // Student 1 on assign2: Attempt 1 Submitted (older), Attempt 2 Late (latest) -> counts as 1 pending review
            var s1a1 = new AssignmentSubmission { AssignmentId = assign2.Id, StudentId = student1.Id, AttemptNumber = 1, IsLatest = false, Status = SubmissionStatus.Submitted, SubmittedAt = baseTime };
            var s1a2 = new AssignmentSubmission { AssignmentId = assign2.Id, StudentId = student1.Id, AttemptNumber = 2, IsLatest = true, Status = SubmissionStatus.Late, SubmittedAt = baseTime.AddMinutes(30) };
            // Student 2 on assign2: Attempt 1 Submitted (older), Attempt 2 Graded (latest) -> 0 pending for student2
            var s2a1 = new AssignmentSubmission { AssignmentId = assign2.Id, StudentId = student2.Id, AttemptNumber = 1, IsLatest = false, Status = SubmissionStatus.Submitted, SubmittedAt = baseTime };
            var s2a2 = new AssignmentSubmission { AssignmentId = assign2.Id, StudentId = student2.Id, AttemptNumber = 2, IsLatest = true, Status = SubmissionStatus.Graded, Score = 88.0, GradedAt = baseTime.AddMinutes(40), SubmittedAt = baseTime.AddMinutes(20) };
            // Student 2 on assignment (Assignment 1): Attempt 1 Late (latest) -> 1 pending for student2 on Assignment 1
            var s2Assign1 = new AssignmentSubmission { AssignmentId = assignment.Id, StudentId = student2.Id, AttemptNumber = 1, IsLatest = true, Status = SubmissionStatus.Late, SubmittedAt = baseTime.AddMinutes(10) };
            context.AssignmentSubmissions.AddRange(s1a1, s1a2, s2a1, s2a2, s2Assign1);
            await context.SaveChangesAsync();

            var kpiDash = await teacherService.GetTeacherDashboardAsync(teacherA.Id);
            var kpiAssigns = (await teacherService.GetTeacherAssignmentsAsync(teacherA.Id)).ToList();
            var kpiA1 = kpiAssigns.FirstOrDefault(a => a.Id == assignment.Id);
            var kpiA2 = kpiAssigns.FirstOrDefault(a => a.Id == assign2.Id);
            // Expected: s1a2 (assign2, Late) + s2Assign1 (assignment, Late) = 2 total pending
            if (kpiDash.PendingReviewsCount == 2 && kpiA1?.PendingSubmissions == 1 && kpiA2?.PendingSubmissions == 1)
            {
                Console.WriteLine("  [PASS] Test 14: Pending Reviews KPI counts latest Submitted/Late per student/assignment and excludes superseded & graded attempts");
                passed++;
            }
            else
            {
                Console.WriteLine($"  [FAIL] Test 14: Pending Reviews KPI mismatch (Dash={kpiDash.PendingReviewsCount}, A1={kpiA1?.PendingSubmissions}, A2={kpiA2?.PendingSubmissions})");
                failed++;
            }

            // TEST 15: Assignment Gradebook Accuracy (Latest Graded + Equal Timestamp Tie-Break + Ungraded Newer Attempt)
            var equalGradeTime = DateTime.UtcNow.AddMinutes(-10);
            // Student 1 on assign2: two older graded attempts with equal timestamps (70 vs 82 -> 82 wins by higher Id), plus an ungraded newer Attempt 5 (Late) -> 82 must remain in gradebook
            var s1g1 = new AssignmentSubmission { AssignmentId = assign2.Id, StudentId = student1.Id, AttemptNumber = 3, IsLatest = false, Status = SubmissionStatus.Graded, Score = 70.0, GradedAt = equalGradeTime, SubmittedAt = equalGradeTime };
            var s1g2 = new AssignmentSubmission { AssignmentId = assign2.Id, StudentId = student1.Id, AttemptNumber = 4, IsLatest = false, Status = SubmissionStatus.Graded, Score = 82.0, GradedAt = equalGradeTime, SubmittedAt = equalGradeTime };
            var s1NewerUngraded = new AssignmentSubmission { AssignmentId = assign2.Id, StudentId = student1.Id, AttemptNumber = 5, IsLatest = true, Status = SubmissionStatus.Late, SubmittedAt = equalGradeTime.AddMinutes(5) };
            context.AssignmentSubmissions.AddRange(s1g1, s1g2, s1NewerUngraded);
            await context.SaveChangesAsync();

            // TEST 16: Quiz Gradebook Accuracy (Highest Score + Deterministic Tie-Break + Separate Students)
            var qTime = DateTime.UtcNow.AddMinutes(-20);
            // Student 1: scores 60, 90 (tie), 90 (newer) -> 90 (1 row)
            var q1s1_a1 = new QuizSubmission { QuizId = quiz1.Id, StudentId = student1.Id, Score = 60.0, IsPassed = true, SubmittedAt = qTime };
            var q1s1_a2 = new QuizSubmission { QuizId = quiz1.Id, StudentId = student1.Id, Score = 90.0, IsPassed = true, SubmittedAt = qTime.AddMinutes(1) };
            var q1s1_a3 = new QuizSubmission { QuizId = quiz1.Id, StudentId = student1.Id, Score = 90.0, IsPassed = true, SubmittedAt = qTime.AddMinutes(2) };
            // Student 2: scores 75, 40 -> 75 (1 row)
            var q1s2_a1 = new QuizSubmission { QuizId = quiz1.Id, StudentId = student2.Id, Score = 75.0, IsPassed = true, SubmittedAt = qTime };
            var q1s2_a2 = new QuizSubmission { QuizId = quiz1.Id, StudentId = student2.Id, Score = 40.0, IsPassed = false, SubmittedAt = qTime.AddMinutes(5) };
            context.QuizSubmissions.AddRange(q1s1_a1, q1s1_a2, q1s1_a3, q1s2_a1, q1s2_a2);
            await context.SaveChangesAsync();

            var fullGrades = (await teacherService.GetTeacherGradesAsync(teacherA.Id)).ToList();
            var assign2S1Rows = fullGrades.Where(g => g.AssessmentType == "Assignment" && g.AssessmentTitle == itemAssign2.Title && g.StudentId == student1.Id).ToList();
            var assign2S2Rows = fullGrades.Where(g => g.AssessmentType == "Assignment" && g.AssessmentTitle == itemAssign2.Title && g.StudentId == student2.Id).ToList();
            var quiz1S1Rows = fullGrades.Where(g => g.AssessmentType == "Quiz" && g.AssessmentTitle == itemQuiz1.Title && g.StudentId == student1.Id).ToList();
            var quiz1S2Rows = fullGrades.Where(g => g.AssessmentType == "Quiz" && g.AssessmentTitle == itemQuiz1.Title && g.StudentId == student2.Id).ToList();
            var teacherBGradesCheck = (await teacherService.GetTeacherGradesAsync(teacherB.Id)).ToList();

            bool assignGradeOk = assign2S1Rows.Count == 1 && assign2S1Rows[0].Score == 82.0 && assign2S2Rows.Count == 1 && assign2S2Rows[0].Score == 88.0;
            bool quizGradeOk = quiz1S1Rows.Count == 1 && quiz1S1Rows[0].Score == 90.0 && quiz1S2Rows.Count == 1 && quiz1S2Rows[0].Score == 75.0;
            if (assignGradeOk && quizGradeOk && !teacherBGradesCheck.Any())
            {
                Console.WriteLine("  [PASS] Test 15 & 16: Assignment Gradebook (latest graded, tie-broken, ungraded newer preserved) and Quiz Gradebook (highest score, 1 row/student/assessment) verified");
                passed++;
            }
            else
            {
                Console.WriteLine($"  [FAIL] Test 15 & 16: Gradebook accuracy mismatch (AssignOk={assignGradeOk}, QuizOk={quizGradeOk}, TeacherBCount={teacherBGradesCheck.Count})");
                failed++;
            }

            // TEST 17: CourseDetails Routing & Controller Action Verification
            var storageService = scope.ServiceProvider.GetRequiredService<IAssignmentStorageService>();
            var teacherLogger = scope.ServiceProvider.GetRequiredService<ILogger<TeacherController>>();
            var htmlSanitizer = scope.ServiceProvider.GetService<IHtmlSanitizationService>();
            var tempDataProvider = scope.ServiceProvider.GetService<ITempDataProvider>() ?? new DummyTempDataProvider();

            TeacherController BuildTeacherController(User? user, string? role)
            {
                var ctrl = new TeacherController(teacherService, storageService, context, userManager, teacherLogger, htmlSanitizer);
                var httpContext = new DefaultHttpContext();
                if (user != null)
                {
                    var claims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, user.Id),
                        new(ClaimTypes.Name, user.UserName ?? user.Email ?? "user"),
                    };
                    if (!string.IsNullOrEmpty(role))
                    {
                        claims.Add(new(ClaimTypes.Role, role));
                    }
                    httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
                }
                ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
                ctrl.TempData = new TempDataDictionary(httpContext, tempDataProvider);
                return ctrl;
            }

            // 17.1: Verify route attributes on CourseDetails action method
            var cdMethod = typeof(TeacherController).GetMethod(nameof(TeacherController.CourseDetails));
            var routeAttrs = cdMethod?.GetCustomAttributes(typeof(RouteAttribute), false)
                .Cast<RouteAttribute>()
                .Select(r => r.Template)
                .ToList() ?? new List<string>();
            bool hasManageRoute = routeAttrs.Contains("Teacher/Courses/Details/{id:int}");
            bool hasLegacyRoute = routeAttrs.Contains("Teacher/CourseDetails/{id:int?}");

            // 17.2: Authorized Teacher accesses owned course (/Teacher/Courses/Details/{id})
            var ctrlA = BuildTeacherController(teacherA, "Teacher");
            var resA = await ctrlA.CourseDetails(courseA.Id);
            bool teacherAOk = resA is ViewResult vrA &&
                              vrA.Model is TeacherCourseDetailsViewModel m1 &&
                              m1.Id == courseA.Id &&
                              m1.RoleLabel == "Instructor";

            // 17.3: Authorized TA accesses assigned course
            var ctrlTA = BuildTeacherController(taUser, "Assistance");
            var resTA = await ctrlTA.CourseDetails(courseA.Id);
            bool taAOk = resTA is ViewResult vrTA &&
                         vrTA.Model is TeacherCourseDetailsViewModel m2 &&
                         m2.Id == courseA.Id &&
                         m2.RoleLabel == "Teaching Assistant" &&
                         m2.CanGrade == true;

            // 17.4: Unauthorized Teacher cannot access unowned course (redirects to Courses)
            var ctrlB = BuildTeacherController(teacherB, "Teacher");
            var resB = await ctrlB.CourseDetails(courseA.Id);
            bool teacherBOk = resB is RedirectToActionResult rtaB &&
                              rtaB.ActionName == nameof(TeacherController.Courses) &&
                              ctrlB.TempData.ContainsKey("ErrorMessage");

            // 17.5: TA cannot access unassigned course (redirects to Courses)
            var resTAB = await ctrlTA.CourseDetails(courseB.Id);
            bool taBOk = resTAB is RedirectToActionResult rtaTAB &&
                         rtaTAB.ActionName == nameof(TeacherController.Courses);

            // 17.6: Invalid course ID (0) safely redirects to Courses
            var resZero = await ctrlA.CourseDetails(0);
            bool zeroOk = resZero is RedirectToActionResult rtaZero &&
                          rtaZero.ActionName == nameof(TeacherController.Courses);

            // 17.7: Unauthenticated request redirects to Login
            var ctrlAnon = BuildTeacherController(null, null);
            var resAnon = await ctrlAnon.CourseDetails(courseA.Id);
            bool anonOk = resAnon is RedirectToActionResult rtaAnon &&
                          rtaAnon.ActionName == "Login" &&
                          rtaAnon.ControllerName == "Account";

            if (hasManageRoute && hasLegacyRoute && teacherAOk && taAOk && teacherBOk && taBOk && zeroOk && anonOk)
            {
                Console.WriteLine("  [PASS] Test 17: TeacherController.CourseDetails verified: /Teacher/Courses/Details/{id:int} & /Teacher/CourseDetails/{id:int?} routes active, authorized instructor & TA access verified, and cross-course/unauthenticated access strictly blocked");
                passed++;
            }
            else
            {
                Console.WriteLine($"  [FAIL] Test 17: CourseDetails controller verification mismatch (ManageRoute={hasManageRoute}, LegacyRoute={hasLegacyRoute}, TeacherA={teacherAOk}, TA={taAOk}, TeacherB={teacherBOk}, TAB={taBOk}, Zero={zeroOk}, Anon={anonOk})");
                failed++;
            }

            // TEST 18: Cleanup Test Seed Data
            context.CourseAssistants.RemoveRange(context.CourseAssistants.Where(ca => ca.AssistantId == taUser.Id));
            context.QuizSubmissions.RemoveRange(context.QuizSubmissions.Where(qs => qs.StudentId == student1.Id || qs.StudentId == student2.Id));
            context.Quizzes.RemoveRange(context.Quizzes.Where(q => q.CourseItemId == itemQuiz1.Id));
            context.AssignmentSubmissions.RemoveRange(context.AssignmentSubmissions.Where(s => s.StudentId == student1.Id || s.StudentId == student2.Id));
            context.Assignments.RemoveRange(context.Assignments.Where(a => a.CourseItemId == itemAssignment.Id || a.CourseItemId == itemAssign2.Id));
            context.CourseItems.RemoveRange(context.CourseItems.Where(ci => ci.ChapterId == chapterA.Id));
            context.Chapters.RemoveRange(context.Chapters.Where(ch => ch.CourseId == courseA.Id));
            context.Enrollments.RemoveRange(context.Enrollments.Where(e => e.StudentId == student1.Id || e.StudentId == student2.Id));
            context.Courses.RemoveRange(context.Courses.Where(c => c.TeacherId == teacherA.Id || c.TeacherId == teacherB.Id));
            await context.SaveChangesAsync();

            await userManager.DeleteAsync(student2);
            await userManager.DeleteAsync(student1);
            await userManager.DeleteAsync(taUser);
            await userManager.DeleteAsync(teacherA);
            await userManager.DeleteAsync(teacherB);

            Console.WriteLine("  [PASS] Test 18: Clean Database Restoration: Test teacher accounts, TA data & course records cleaned up cleanly from SQL Server");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] Test Exception: {ex.Message}\n{ex.StackTrace}");
            failed++;
        }

        Console.WriteLine("\n=================================================");
        Console.WriteLine($"🎉 TEACHER PORTAL TESTS PASSED: {passed}/{passed + failed}");
        Console.WriteLine("=================================================\n");

        if (failed > 0)
        {
            throw new InvalidOperationException($"Teacher Portal Test Suite failed with {failed} failure(s).");
        }
    }

    private sealed class DummyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
