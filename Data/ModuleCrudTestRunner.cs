using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class ModuleCrudTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("📦  AUB DIGITAL ACADEMY - MODULE (CHAPTER) CRUD BACKEND AUDIT");
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
        var curriculumService = scope.ServiceProvider.GetRequiredService<ICurriculumService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        // 1. Verify SQL Server connection
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            Fail("Database Connectivity", "Unable to connect to SQL Server.");
            return;
        }
        Pass("SQL Server connection verified successfully");

        // Load or create test dependencies (Teacher, Major, Semester)
        var teachers = await userManager.GetUsersInRoleAsync("Teacher");
        var teacher = teachers.FirstOrDefault(u => u.IsActive) ?? await context.Users.FirstOrDefaultAsync(u => u.IsActive);
        var major = await context.Majors.FirstOrDefaultAsync();
        var semester = await context.Semesters.FirstOrDefaultAsync();
        var student = await context.Users.FirstOrDefaultAsync(u => u.Email == "student@aub.edu.kh")
                      ?? (await userManager.GetUsersInRoleAsync("Student")).FirstOrDefault();

        if (teacher == null || major == null || semester == null || student == null)
        {
            Fail("Dependency Lookup", "Missing required seed dependencies (Teacher, Major, Semester, or Student).");
            return;
        }
        Pass($"Loaded test dependencies: Teacher={teacher.FullName}, Major={major.Code}, Semester={semester.Code}");

        // Create isolated test courses
        var courseA = new Course
        {
            Code = "TEST-MOD-A",
            Title = "Module Test Course A",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var courseB = new Course
        {
            Code = "TEST-MOD-B",
            Title = "Module Test Course B",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Clean up any previous test courses
        var existingA = await context.Courses.FirstOrDefaultAsync(c => c.Code == "TEST-MOD-A");
        if (existingA != null) context.Courses.Remove(existingA);
        var existingB = await context.Courses.FirstOrDefaultAsync(c => c.Code == "TEST-MOD-B");
        if (existingB != null) context.Courses.Remove(existingB);
        await context.SaveChangesAsync();

        context.Courses.AddRange(courseA, courseB);
        await context.SaveChangesAsync();
        int courseAId = courseA.Id;
        int courseBId = courseB.Id;
        Pass($"Created isolated test courses: Course A (ID {courseAId}) & Course B (ID {courseBId})");

        // =========================================================================
        // TEST B — CREATE MODULE (CHAPTER)
        // =========================================================================
        var createResult = await curriculumService.CreateChapterAsync(courseAId, "Module 1: Introduction to Programming", 1);
        if (!createResult.IsSuccess || createResult.EntityId == null)
        {
            Fail("TEST B (Create Module)", $"Failed to create module: {createResult.Message}");
            return;
        }
        int module1Id = createResult.EntityId.Value;

        // Verify in SQL Server
        var createdChapterInDb = await context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == module1Id);
        if (createdChapterInDb == null || createdChapterInDb.CourseId != courseAId || createdChapterInDb.Title != "Module 1: Introduction to Programming")
        {
            Fail("TEST B (SQL Server Verification)", "Created chapter record not found or fields mismatched in SQL Server.");
            return;
        }
        Pass("TEST B (Create Module): Module created successfully and persisted with correct Course relationship in SQL Server");

        // Create second module with auto-position
        var createResult2 = await curriculumService.CreateChapterAsync(courseAId, "Module 2: Advanced OOP Concepts");
        if (!createResult2.IsSuccess || createResult2.EntityId == null)
        {
            Fail("TEST B2 (Create Second Module)", $"Failed: {createResult2.Message}");
            return;
        }
        int module2Id = createResult2.EntityId.Value;
        var mod2Db = await context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == module2Id);
        if (mod2Db == null || mod2Db.Position != 2)
        {
            Fail("TEST B2 (Auto-position)", $"Expected auto-position 2, got {mod2Db?.Position}");
            return;
        }
        Pass("TEST B2 (Auto-Positioning): Second module automatically assigned Position = 2");

        // Create module under Course B to test cross-course filtering
        var createCourseBMod = await curriculumService.CreateChapterAsync(courseBId, "Course B Module 1", 1);
        int courseBModId = createCourseBMod.EntityId!.Value;

        // =========================================================================
        // TEST A — GET MODULES BY COURSE
        // =========================================================================
        var courseAModules = (await curriculumService.GetChaptersByCourseIdAsync(courseAId)).ToList();
        if (courseAModules.Count != 2 || courseAModules.Any(m => m.CourseId != courseAId))
        {
            Fail("TEST A (Get Modules)", $"Expected 2 modules for Course A, got {courseAModules.Count}");
            return;
        }
        if (courseAModules.Any(m => m.Id == courseBModId))
        {
            Fail("TEST A (Isolation)", "Unrelated Course B module was returned when querying Course A modules!");
            return;
        }
        if (courseAModules[0].Position > courseAModules[1].Position)
        {
            Fail("TEST A (Ordering)", "Modules were not ordered by Position ascending.");
            return;
        }
        Pass("TEST A (Get Modules): Returns only modules belonging to the specified Course, correctly ordered by Position");

        // =========================================================================
        // TEST C — UPDATE MODULE
        // =========================================================================
        var updateResult = await curriculumService.UpdateChapterAsync(module1Id, "Module 1: Foundations of Software Engineering", 1, courseAId);
        if (!updateResult.IsSuccess)
        {
            Fail("TEST C (Update Module)", $"Update failed: {updateResult.Message}");
            return;
        }

        // Verify in SQL Server
        var updatedChapter = await context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == module1Id);
        if (updatedChapter == null || updatedChapter.Title != "Module 1: Foundations of Software Engineering")
        {
            Fail("TEST C (SQL Server Verification)", $"Title not updated in SQL Server: {updatedChapter?.Title}");
            return;
        }
        Pass("TEST C (Update Module): Chapter record updated in-place in SQL Server without creating a duplicate record");

        // =========================================================================
        // TEST E — INVALID COURSE ID
        // =========================================================================
        var invalidCourseResult = await curriculumService.CreateChapterAsync(999999, "Module for Ghost Course");
        if (invalidCourseResult.Status != CurriculumOperationStatus.NotFound || invalidCourseResult.IsSuccess)
        {
            Fail("TEST E (Invalid Course)", $"Expected NotFound status, got {invalidCourseResult.Status}");
            return;
        }
        var ghostCount = await context.Chapters.CountAsync(ch => ch.Title == "Module for Ghost Course");
        if (ghostCount > 0)
        {
            Fail("TEST E (DB Integrity)", "Chapter was created for a non-existent course!");
            return;
        }
        Pass("TEST E (Invalid Course): Rejected with NotFound status; zero database records created");

        // =========================================================================
        // TEST F — INVALID OWNERSHIP (CROSS-COURSE EDIT/DELETE ATTEMPT)
        // =========================================================================
        // Attempt to update Course A's module passing Course B's ID as expected parent
        var crossEditResult = await curriculumService.UpdateChapterAsync(module1Id, "Hacked Title", 1, courseBId);
        if (crossEditResult.Status != CurriculumOperationStatus.ValidationError || crossEditResult.IsSuccess)
        {
            Fail("TEST F (Cross-Course Edit)", $"Expected ValidationError, got {crossEditResult.Status}");
            return;
        }
        var titleAfterCrossEdit = (await context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == module1Id))?.Title;
        if (titleAfterCrossEdit == "Hacked Title")
        {
            Fail("TEST F (DB Corruption)", "Module title was modified via unauthorized cross-course reference!");
            return;
        }
        Pass("TEST F (Ownership Validation): Cross-course module update rejected; database record preserved");

        // =========================================================================
        // TEST D — DELETE EMPTY MODULE (CASE A)
        // =========================================================================
        var emptyModResult = await curriculumService.CreateChapterAsync(courseAId, "Temporary Empty Module", 99);
        int emptyModId = emptyModResult.EntityId!.Value;

        var deleteEmptyResult = await curriculumService.DeleteChapterAsync(emptyModId, courseAId);
        if (!deleteEmptyResult.IsSuccess)
        {
            Fail("TEST D (Delete Empty Module)", $"Failed: {deleteEmptyResult.Message}");
            return;
        }

        var emptyStillInDb = await context.Chapters.AnyAsync(ch => ch.Id == emptyModId);
        if (emptyStillInDb)
        {
            Fail("TEST D (SQL Server Check)", "Empty module still exists in SQL Server after deletion!");
            return;
        }

        // Verify parent course A was NOT deleted!
        var parentCourseAExists = await context.Courses.AnyAsync(c => c.Id == courseAId);
        if (!parentCourseAExists)
        {
            Fail("TEST D (Parent Preservation)", "Deleting a Module incorrectly deleted the parent Course!");
            return;
        }
        Pass("TEST D (Delete Empty Module): Empty module safely removed from SQL Server; parent Course remained untouched");

        // =========================================================================
        // TEST G — SAFE DELETE WITH CONTENT (CASE B: NO STUDENTS)
        // =========================================================================
        var modWithContent = await curriculumService.CreateChapterAsync(courseAId, "Module With Lessons But No Students", 10);
        int modWithContentId = modWithContent.EntityId!.Value;

        // Add CourseItem + Lesson
        var lessonItem = new CourseItem
        {
            ChapterId = modWithContentId,
            Title = "Test Lesson 10.1",
            Position = 1,
            Type = ItemType.Lesson
        };
        context.CourseItems.Add(lessonItem);
        await context.SaveChangesAsync();

        var lesson = new Lesson
        {
            CourseItemId = lessonItem.Id,
            ContentHtml = "<p>Clean lesson</p>"
        };
        context.Lessons.Add(lesson);
        await context.SaveChangesAsync();
        int lessonItemId = lessonItem.Id;

        // Delete module with content (no student data)
        var deleteContentResult = await curriculumService.DeleteChapterAsync(modWithContentId, courseAId);
        if (!deleteContentResult.IsSuccess)
        {
            Fail("TEST G (Case B: Content without students)", $"Deletion failed: {deleteContentResult.Message}");
            return;
        }

        // Check SQL Server: Both Chapter and CourseItem/Lesson deleted cleanly with zero orphans
        var chLeft = await context.Chapters.AnyAsync(ch => ch.Id == modWithContentId);
        var itemLeft = await context.CourseItems.AnyAsync(ci => ci.Id == lessonItemId);
        if (chLeft || itemLeft)
        {
            Fail("TEST G (Case B: Orphan check)", $"Orphans left: Chapter={chLeft}, CourseItem={itemLeft}");
            return;
        }
        Pass("TEST G (Safe Delete Case B): Module with lessons (no student data) deleted cleanly with zero orphaned records");

        // =========================================================================
        // TEST H — SAFE DELETE WITH STUDENT PROGRESS (CASE C: BLOCKED)
        // =========================================================================
        var modWithStudent = await curriculumService.CreateChapterAsync(courseAId, "Module With Active Students", 20);
        int modWithStudentId = modWithStudent.EntityId!.Value;

        var studentItem = new CourseItem
        {
            ChapterId = modWithStudentId,
            Title = "Active Student Lesson",
            Position = 1,
            Type = ItemType.Lesson
        };
        context.CourseItems.Add(studentItem);
        await context.SaveChangesAsync();

        var progressRecord = new CourseProgress
        {
            CourseItemId = studentItem.Id,
            StudentId = student.Id,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        };
        context.CourseProgresses.Add(progressRecord);
        await context.SaveChangesAsync();

        // Attempt deletion of module with student progress
        var deleteBlockedResult = await curriculumService.DeleteChapterAsync(modWithStudentId, courseAId);
        if (deleteBlockedResult.Status != CurriculumOperationStatus.BlockedByStudentData || deleteBlockedResult.IsSuccess)
        {
            Fail("TEST H (Case C: Student data protection)", $"Expected BlockedByStudentData, got {deleteBlockedResult.Status}");
            return;
        }

        // Verify in SQL Server that module and student progress remain untouched
        var modPreserved = await context.Chapters.AnyAsync(ch => ch.Id == modWithStudentId);
        var progressPreserved = await context.CourseProgresses.AnyAsync(cp => cp.Id == progressRecord.Id);
        if (!modPreserved || !progressPreserved)
        {
            Fail("TEST H (Case C: Preservation check)", "Module or student progress record was deleted despite blocking!");
            return;
        }
        Pass("TEST H (Safe Delete Case C): Module deletion blocked by active student records; module and student progress preserved in SQL Server");

        // Clean up test records
        context.Courses.Remove(courseA);
        context.Courses.Remove(courseB);
        await context.SaveChangesAsync();

        Console.WriteLine("\n=================================================================");
        Console.WriteLine($"🎉  ALL {totalTests} MODULE (CHAPTER) CRUD TESTS PASSED SUCCESSFULLY!");
        Console.WriteLine("=================================================================\n");
    }
}
