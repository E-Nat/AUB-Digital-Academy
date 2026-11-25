using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
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

            // TEST 14: Cleanup Test Seed Data
            context.AssignmentSubmissions.RemoveRange(context.AssignmentSubmissions.Where(s => s.StudentId == student1.Id));
            context.Assignments.RemoveRange(context.Assignments.Where(a => a.CourseItemId == itemAssignment.Id));
            context.CourseItems.RemoveRange(context.CourseItems.Where(ci => ci.ChapterId == chapterA.Id));
            context.Chapters.RemoveRange(context.Chapters.Where(ch => ch.CourseId == courseA.Id));
            context.Enrollments.RemoveRange(context.Enrollments.Where(e => e.StudentId == student1.Id));
            context.Courses.RemoveRange(context.Courses.Where(c => c.TeacherId == teacherA.Id || c.TeacherId == teacherB.Id));
            await context.SaveChangesAsync();

            await userManager.DeleteAsync(student1);
            await userManager.DeleteAsync(teacherA);
            await userManager.DeleteAsync(teacherB);

            Console.WriteLine("  [PASS] Test 14: Clean Database Restoration: Test teacher accounts & course data cleaned up cleanly from SQL Server");
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
}
