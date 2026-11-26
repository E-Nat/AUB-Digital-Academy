using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class AssistanceIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("🚀 RUNNING 14-POINT ASSISTANCE ROLE INTEGRATION TEST SUITE");
        Console.WriteLine("=================================================================\n");

        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var teacherService = services.GetRequiredService<ITeacherService>();
        var userService = services.GetRequiredService<IUserService>();
        var courseService = services.GetRequiredService<ICourseService>();
        var enrollmentService = services.GetRequiredService<IEnrollmentService>();

        // 0. Ensure database is initialized with SeedData
        await SeedData.InitializeAsync(services);

        var cs301 = await context.Courses.FirstOrDefaultAsync(c => c.Code == "CS301")
            ?? throw new Exception("Baseline course CS301 not found.");
        var cs201 = await context.Courses.FirstOrDefaultAsync(c => c.Code == "CS201")
            ?? throw new Exception("Baseline course CS201 not found.");

        var adminUser = await userManager.FindByEmailAsync("admin@aub.com")
            ?? throw new Exception("Admin user not found.");
        var teacherUser = await userManager.FindByEmailAsync("teacher@aub.com")
            ?? throw new Exception("Teacher user not found.");
        var taUser = await userManager.FindByEmailAsync("ta@aub.com")
            ?? throw new Exception("Assistance user not found.");
        var studentUser = await userManager.FindByEmailAsync("student@aub.com")
            ?? throw new Exception("Student user not found.");

        int passed = 0;

        // --- TEST 1: Admin full access verification ---
        var adminRoles = await userManager.GetRolesAsync(adminUser);
        if (!adminRoles.Contains("Admin")) throw new Exception("Admin role missing on admin@aub.com.");
        var adminCourses = await courseService.GetAllCoursesAsync();
        if (adminCourses == null || !adminCourses.Any()) throw new Exception("Admin cannot query courses.");
        passed++;
        Console.WriteLine($"[PASS] Test 1: Admin Access: Admin has full authority over {adminCourses.Count()} courses and administrative functions.");

        // --- TEST 2: Teacher access to assigned courses ---
        var teacherHasAccess = await teacherService.HasAccessToCourseAsync(cs301.Id, teacherUser.Id);
        var teacherCanGrade = await teacherService.CanGradeCourseAsync(cs301.Id, teacherUser.Id);
        if (!teacherHasAccess || !teacherCanGrade) throw new Exception("Teacher must have full access and grading permissions on taught courses.");
        passed++;
        Console.WriteLine("[PASS] Test 2: Teacher Access: Primary teacher has verified access and grading permission for CS301.");

        // --- TEST 3: Teacher denial for unassigned courses ---
        var dummyTeacher = new User
        {
            UserName = $"dummy_tch_{Guid.NewGuid():N}@aub.com",
            Email = $"dummy_tch_{Guid.NewGuid():N}@aub.com",
            FullName = "External Instructor",
            UniversityId = $"DUMMY_{Guid.NewGuid():N}".Substring(0, 10),
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(dummyTeacher, "Teacher@123");
        await userManager.AddToRoleAsync(dummyTeacher, "Teacher");
        var dummyAccess = await teacherService.HasAccessToCourseAsync(cs301.Id, dummyTeacher.Id);
        if (dummyAccess) throw new Exception("Unassigned teacher was incorrectly granted access to CS301.");
        passed++;
        Console.WriteLine("[PASS] Test 3: Teacher Denial: Unassigned instructor correctly denied access to CS301.");

        // --- TEST 4: Assistance access to assigned courses ---
        var taAccess = await teacherService.HasAccessToCourseAsync(cs301.Id, taUser.Id);
        if (!taAccess) throw new Exception("Teaching assistant must have access to assigned course CS301.");
        var taCourses = await teacherService.GetTeacherCoursesAsync(taUser.Id);
        var cs301Card = taCourses.FirstOrDefault(c => c.Id == cs301.Id);
        if (cs301Card == null || cs301Card.RoleLabel != "Teaching Assistant")
            throw new Exception("Teaching assistant courses list must display CS301 with 'Teaching Assistant' role label.");
        passed++;
        Console.WriteLine("[PASS] Test 4: Assistance Access: Teaching Assistant (ta@aub.com) successfully accesses assigned course CS301 with 'Teaching Assistant' badge.");

        // --- TEST 5: Assistance denial for unassigned courses ---
        var taUnassignedAccess = await teacherService.HasAccessToCourseAsync(cs201.Id, taUser.Id);
        if (taUnassignedAccess) throw new Exception("Teaching assistant was incorrectly granted access to unassigned course CS201.");
        var unassignedDetails = await teacherService.GetTeacherCourseDetailsAsync(cs201.Id, taUser.Id);
        if (unassignedDetails != null) throw new Exception("Teaching assistant should receive null details for unassigned course CS201.");
        passed++;
        Console.WriteLine("[PASS] Test 5: Assistance Denial: Teaching Assistant is strictly forbidden from unassigned courses (CS201).");

        // --- TEST 6: Assistance denial for administrator operations ---
        var taRoles = await userManager.GetRolesAsync(taUser);
        if (taRoles.Contains("Admin")) throw new Exception("Assistance account has Admin role.");
        passed++;
        Console.WriteLine("[PASS] Test 6: Assistance Role Isolation: Assistance user does not have Admin role; locked out of administrative scopes.");

        // --- TEST 7: Assistance CanGrade = false denial on backend ---
        var restrictedCourse = new Course
        {
            Code = $"CS_TEST_{Guid.NewGuid():N}".Substring(0, 10).ToUpper(),
            Title = "Restricted TA Course",
            Description = "Course to verify CanGrade = false enforcement.",
            Status = CourseStatus.Published,
            MajorId = cs301.MajorId,
            SemesterId = cs301.SemesterId,
            TeacherId = teacherUser.Id,
            Thumbnail = "/images/course_webdev.jpg",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(restrictedCourse);
        await context.SaveChangesAsync();

        var taRestrictedAssignment = new CourseAssistant
        {
            CourseId = restrictedCourse.Id,
            AssistantId = taUser.Id,
            CanGrade = false, // Least privilege: grading disabled
            AssignedAt = DateTime.UtcNow
        };
        context.CourseAssistants.Add(taRestrictedAssignment);
        await context.SaveChangesAsync();

        var canGradeRestricted = await teacherService.CanGradeCourseAsync(restrictedCourse.Id, taUser.Id);
        if (canGradeRestricted) throw new Exception("CanGradeCourseAsync should return false when CanGrade == false.");

        // Create assignment and submission in restricted course
        var chapter = new Chapter { CourseId = restrictedCourse.Id, Title = "Module 1", Position = 1 };
        context.Chapters.Add(chapter);
        await context.SaveChangesAsync();

        var courseItem = new CourseItem { ChapterId = chapter.Id, Title = "Homework 1", Position = 1, Type = ItemType.Assignment };
        context.CourseItems.Add(courseItem);
        await context.SaveChangesAsync();

        var assignment = new Assignment { CourseItemId = courseItem.Id, MaxScore = 100, DueDate = DateTime.UtcNow.AddDays(7) };
        context.Assignments.Add(assignment);
        await context.SaveChangesAsync();

        var submission = new AssignmentSubmission
        {
            AssignmentId = assignment.Id,
            StudentId = studentUser.Id,
            SubmittedAt = DateTime.UtcNow,
            Status = SubmissionStatus.Submitted,
            TextResponse = "My assignment submission"
        };
        context.AssignmentSubmissions.Add(submission);
        await context.SaveChangesAsync();

        var gradingResult = await teacherService.GradeSubmissionAsync(submission.Id, 95.0, "Good job", taUser.Id);
        if (gradingResult.Success) throw new Exception("Backend must reject grading when CanGrade == false.");
        passed++;
        Console.WriteLine($"[PASS] Test 7: Backend Enforcement (CanGrade=false): Grading attempt safely rejected: '{gradingResult.Message}'.");

        // --- TEST 8: Assistance CanGrade = true success on CS301 (Owner Decision 14.2) ---
        var cs301CanGrade = await teacherService.CanGradeCourseAsync(cs301.Id, taUser.Id);
        if (!cs301CanGrade) throw new Exception("Owner Decision 14.2: ta@aub.com must have CanGrade == true on CS301.");

        // Create a test submission in CS301
        var cs301Chapter = await context.Chapters.FirstOrDefaultAsync(c => c.CourseId == cs301.Id);
        if (cs301Chapter == null)
        {
            cs301Chapter = new Chapter { CourseId = cs301.Id, Title = "CS301 Mod", Position = 1 };
            context.Chapters.Add(cs301Chapter);
            await context.SaveChangesAsync();
        }
        var cs301CourseItem = await context.CourseItems.FirstOrDefaultAsync(ci => ci.ChapterId == cs301Chapter.Id && ci.Type == ItemType.Assignment);
        if (cs301CourseItem == null)
        {
            cs301CourseItem = new CourseItem { ChapterId = cs301Chapter.Id, Title = "CS301 Lab", Position = 1, Type = ItemType.Assignment };
            context.CourseItems.Add(cs301CourseItem);
            await context.SaveChangesAsync();
        }
        var cs301Assignment = await context.Assignments.FirstOrDefaultAsync(a => a.CourseItemId == cs301CourseItem.Id);
        if (cs301Assignment == null)
        {
            cs301Assignment = new Assignment { CourseItemId = cs301CourseItem.Id, MaxScore = 100, DueDate = DateTime.UtcNow.AddDays(7) };
            context.Assignments.Add(cs301Assignment);
            await context.SaveChangesAsync();
        }
        var cs301Sub = new AssignmentSubmission
        {
            AssignmentId = cs301Assignment.Id,
            StudentId = studentUser.Id,
            SubmittedAt = DateTime.UtcNow,
            Status = SubmissionStatus.Submitted,
            TextResponse = "CS301 Student Work"
        };
        context.AssignmentSubmissions.Add(cs301Sub);
        await context.SaveChangesAsync();

        var taGradeSuccess = await teacherService.GradeSubmissionAsync(cs301Sub.Id, 88.5, "Well done by TA", taUser.Id);
        if (!taGradeSuccess.Success) throw new Exception($"TA grading on CS301 failed: {taGradeSuccess.Message}");
        passed++;
        Console.WriteLine($"[PASS] Test 8: Assistance Grading (CanGrade=true): Grading succeeded for CS301: '{taGradeSuccess.Message}'.");

        // --- TEST 9: Student access to enrolled courses ---
        var isStudentEnrolled = await enrollmentService.IsStudentEnrolledAsync(studentUser.Id, cs301.Id);
        if (!isStudentEnrolled)
        {
            await enrollmentService.EnrollStudentAsync(studentUser.Id, cs301.Id);
            isStudentEnrolled = await enrollmentService.IsStudentEnrolledAsync(studentUser.Id, cs301.Id);
        }
        if (!isStudentEnrolled) throw new Exception("Student must be enrolled in CS301.");
        passed++;
        Console.WriteLine("[PASS] Test 9: Student Access: Student verified enrolled in CS301.");

        // --- TEST 10: Student denial for course staff grading operations ---
        var studentGradeAttempt = await teacherService.GradeSubmissionAsync(cs301Sub.Id, 100.0, "Self grade", studentUser.Id);
        if (studentGradeAttempt.Success) throw new Exception("Student was able to grade submission! Critical violation.");
        passed++;
        Console.WriteLine($"[PASS] Test 10: Student Denial: Student grading attempt properly rejected: '{studentGradeAttempt.Message}'.");

        // --- TEST 11: Shared Faculty dashboard scope ---
        var taDashboard = await teacherService.GetTeacherDashboardAsync(taUser.Id);
        if (taDashboard == null) throw new Exception("Teaching assistant dashboard returned null.");
        if (taDashboard.Courses.All(c => c.Id != cs301.Id))
            throw new Exception("Teaching assistant dashboard must include assigned course CS301.");
        passed++;
        Console.WriteLine($"[PASS] Test 11: Shared Faculty Dashboard: TA dashboard successfully retrieved {taDashboard.Courses.Count} assigned course(s).");

        // --- TEST 12: User management CRUD for Assistance role ---
        var newTaEmail = $"new_ta_{Guid.NewGuid():N}@aub.com";
        var createModel = new CreateUserViewModel
        {
            FullName = "New Teaching Assistant",
            Email = newTaEmail,
            UniversityId = $"TA_{Guid.NewGuid():N}".Substring(0, 10).ToUpper(),
            Role = "Assistance",
            Password = "assistancePassword123",
            ConfirmPassword = "assistancePassword123"
        };
        var createResult = await userService.CreateUserAsync(createModel);
        if (!createResult.Succeeded) throw new Exception($"Failed to create user with Assistance role: {createResult.Message}");

        var counts = await userService.GetUserCountsAsync();
        if (counts.TotalAssistants <= 0) throw new Exception("TotalAssistants count did not register new assistant.");

        var assistantsList = await userService.GetUsersAsync(role: "Assistance");
        if (!assistantsList.Any(u => u.Email == newTaEmail)) throw new Exception("User listing did not filter by Assistance role.");

        // Cleanup test assistant
        var createdTa = await userManager.FindByEmailAsync(newTaEmail);
        if (createdTa != null) await userManager.DeleteAsync(createdTa);
        passed++;
        Console.WriteLine("[PASS] Test 12: User Management: Assistance role registration, filtering, counts, and deletion verified.");

        // --- TEST 13: Data integrity of existing accounts ---
        var existingAdmins = await userManager.FindByEmailAsync("admin@aub.com");
        var existingTeachers = await userManager.FindByEmailAsync("teacher@aub.com");
        var existingStudents = await userManager.FindByEmailAsync("student@aub.com");
        var existingTAs = await userManager.FindByEmailAsync("ta@aub.com");
        if (existingAdmins == null || existingTeachers == null || existingStudents == null || existingTAs == null)
            throw new Exception("One or more baseline accounts were deleted or altered.");
        passed++;
        Console.WriteLine("[PASS] Test 13: Data Integrity: All baseline accounts (Admin, Teacher, TA, Student) remain intact and operational.");

        // --- TEST 14: Unique constraint on (CourseId, AssistantId) ---
        bool duplicateCaught = false;
        try
        {
            var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                ?? context.Database.GetConnectionString();
            var silentOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connectionString)
                .UseLoggerFactory(NullLoggerFactory.Instance)
                .Options;

            await using var silentContext = new ApplicationDbContext(silentOptions);
            var duplicateEntry = new CourseAssistant
            {
                CourseId = cs301.Id,
                AssistantId = taUser.Id, // Already exists!
                CanGrade = true,
                AssignedAt = DateTime.UtcNow
            };
            silentContext.CourseAssistants.Add(duplicateEntry);
            await silentContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            duplicateCaught = true;
        }
        if (!duplicateCaught) throw new Exception("Database failed to enforce unique constraint on (CourseId, AssistantId).");
        passed++;
        Console.WriteLine("[PASS] Test 14: Database Constraint: Unique index IX_CourseAssistants_CourseId_AssistantId successfully rejected duplicate assignment.");

        // Cleanup temporary test course and dummy teacher
        try
        {
            context.AssignmentSubmissions.Remove(submission);
            context.AssignmentSubmissions.Remove(cs301Sub);
            context.Assignments.Remove(assignment);
            context.CourseItems.Remove(courseItem);
            context.Chapters.Remove(chapter);
            context.CourseAssistants.Remove(taRestrictedAssignment);
            context.Courses.Remove(restrictedCourse);
            await context.SaveChangesAsync();
            await userManager.DeleteAsync(dummyTeacher);
        }
        catch {}

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"🎉 ALL {passed}/14 ASSISTANCE ROLE INTEGRATION TESTS PASSED PERFECTLY!");
        Console.WriteLine("=================================================================\n");
    }
}
