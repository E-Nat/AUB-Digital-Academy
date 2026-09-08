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
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class LessonCrudTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("📘  AUB DIGITAL ACADEMY - LESSON & CURRICULUM CRUD AUDIT SUITE");
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

        using var initialScope = serviceProvider.CreateScope();
        var context = initialScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var curriculumService = initialScope.ServiceProvider.GetRequiredService<ICurriculumService>();
        var courseService = initialScope.ServiceProvider.GetRequiredService<ICourseService>();
        var userManager = initialScope.ServiceProvider.GetRequiredService<UserManager<User>>();

        // 1. Verify SQL Server connection
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            Fail("Database Connectivity", "Unable to connect to SQL Server.");
            return;
        }
        Pass("SQL Server connection verified successfully");

        // Load test dependencies (Teacher, Major, Semester, Student)
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
        Pass($"Loaded test dependencies: Teacher={teacher.FullName}, Major={major.Code}, Semester={semester.Code}, Student={student.FullName}");

        // =========================================================================
        // SETUP: Clean up existing test courses
        // =========================================================================
        var existingA = await context.Courses.FirstOrDefaultAsync(c => c.Code == "TEST-LES-A" || c.Code == "TEST-LES-A-REV");
        if (existingA != null) context.Courses.Remove(existingA);
        var existingB = await context.Courses.FirstOrDefaultAsync(c => c.Code == "TEST-LES-B");
        if (existingB != null) context.Courses.Remove(existingB);
        var existingC = await context.Courses.FirstOrDefaultAsync(c => c.Code == "TEST-LES-C");
        if (existingC != null) context.Courses.Remove(existingC);
        await context.SaveChangesAsync();

        // Create Course A and Course B
        var courseA = new Course
        {
            Code = "TEST-LES-A",
            Title = "Lesson Test Course A",
            Description = "Initial Course Description",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var courseB = new Course
        {
            Code = "TEST-LES-B",
            Title = "Lesson Test Course B",
            Description = "Course B Description",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Courses.AddRange(courseA, courseB);
        await context.SaveChangesAsync();
        int courseAId = courseA.Id;
        int courseBId = courseB.Id;

        // Create Module A in Course A and Module B in Course B
        var modAResult = await curriculumService.CreateChapterAsync(courseAId, "Module A: Fundamentals", 1);
        var modBResult = await curriculumService.CreateChapterAsync(courseBId, "Module B: Advanced Topics", 1);

        int moduleAId = modAResult.EntityId!.Value;
        int moduleBId = modBResult.EntityId!.Value;

        // Create Lesson A in Module A
        var lessonACreate = new LessonCreateViewModel
        {
            ChapterId = moduleAId,
            CourseId = courseAId,
            Title = "Lesson A: Introduction to Module A",
            ContentHtml = "<p>Lesson A Content</p>",
            VideoUrl = "https://youtube.com/watch?v=lessonA",
            Position = 1
        };
        var lessonAResult = await curriculumService.CreateLessonAsync(lessonACreate);
        int lessonACourseItemId = lessonAResult.EntityId!.Value;

        // Create Lesson B in Module B
        var lessonBCreate = new LessonCreateViewModel
        {
            ChapterId = moduleBId,
            CourseId = courseBId,
            Title = "Lesson B: Introduction to Module B",
            ContentHtml = "<p>Lesson B Content</p>",
            VideoUrl = "https://youtube.com/watch?v=lessonB",
            Position = 1
        };
        var lessonBResult = await curriculumService.CreateLessonAsync(lessonBCreate);
        int lessonBCourseItemId = lessonBResult.EntityId!.Value;

        Pass($"Setup Complete: Course A ({courseAId}) -> Module A ({moduleAId}) -> Lesson A ({lessonACourseItemId}); Course B ({courseBId}) -> Module B ({moduleBId}) -> Lesson B ({lessonBCourseItemId})");

        // =========================================================================
        // 7. MODULE ISOLATION TEST
        // =========================================================================
        // Module A
        // └── Lesson A
        // Module B
        // └── Lesson B
        // Expected:
        // Module A: Only Lesson A
        // Module B: Only Lesson B
        // Module A must never display Lessons from Module B.
        // =========================================================================
        var lessonsInModA = (await curriculumService.GetLessonsByChapterIdAsync(moduleAId))?.ToList();
        var lessonsInModB = (await curriculumService.GetLessonsByChapterIdAsync(moduleBId))?.ToList();

        if (lessonsInModA == null || lessonsInModA.Count != 1 || lessonsInModA[0].Id != lessonACourseItemId)
        {
            Fail("7. MODULE ISOLATION TEST", $"Module A expected only Lesson A ({lessonACourseItemId}), but got {lessonsInModA?.Count} lessons.");
            return;
        }
        if (lessonsInModA.Any(l => l.Id == lessonBCourseItemId || l.ChapterId == moduleBId))
        {
            Fail("7. MODULE ISOLATION TEST", "CRITICAL: Module A displayed Lesson B from Module B!");
            return;
        }

        if (lessonsInModB == null || lessonsInModB.Count != 1 || lessonsInModB[0].Id != lessonBCourseItemId)
        {
            Fail("7. MODULE ISOLATION TEST", $"Module B expected only Lesson B ({lessonBCourseItemId}), but got {lessonsInModB?.Count} lessons.");
            return;
        }
        if (lessonsInModB.Any(l => l.Id == lessonACourseItemId || l.ChapterId == moduleAId))
        {
            Fail("7. MODULE ISOLATION TEST", "CRITICAL: Module B displayed Lesson A from Module A!");
            return;
        }

        // Also verify via GetCurriculumAsync
        var curriculumA = await curriculumService.GetCurriculumAsync(courseAId);
        var curriculumModA = curriculumA?.Modules.FirstOrDefault(m => m.Id == moduleAId);
        if (curriculumModA == null || curriculumModA.Items.Count != 1 || curriculumModA.Items[0].Id != lessonACourseItemId)
        {
            Fail("7. MODULE ISOLATION TEST (Curriculum A)", "Course A curriculum Module A did not contain only Lesson A.");
            return;
        }
        if (curriculumModA.Items.Any(i => i.Id == lessonBCourseItemId || i.ChapterId != moduleAId))
        {
            Fail("7. MODULE ISOLATION TEST (Curriculum A)", "Course A curriculum Module A contains items belonging to Module B!");
            return;
        }

        // Test CurriculumModuleViewModel.Items isolation filter defense
        var testModVm = new CurriculumModuleViewModel
        {
            Id = moduleAId,
            Items = new System.Collections.Generic.List<CurriculumItemViewModel>
            {
                new() { Id = 100, ChapterId = moduleAId, Title = "Valid Lesson A" },
                new() { Id = 200, ChapterId = moduleBId, Title = "Leaked Lesson B" }
            }
        };
        if (testModVm.Items.Count != 1 || testModVm.Items.Any(i => i.ChapterId == moduleBId))
        {
            Fail("7. MODULE ISOLATION TEST (ViewModel Isolation Guard)", "CurriculumModuleViewModel.Items getter failed to filter out foreign module items.");
            return;
        }

        Pass("7. MODULE ISOLATION TEST: Module A contains ONLY Lesson A; Module B contains ONLY Lesson B; foreign lessons strictly excluded");

        // =========================================================================
        // 8. EDIT COURSE TEST
        // =========================================================================
        // Edit Course information.
        // Verify:
        // - Existing Modules remain
        // - Existing Lessons remain
        // - Curriculum relationships remain valid
        // Changing Course information must not break Curriculum.
        // =========================================================================
        var courseAEntity = await context.Courses.FindAsync(courseAId);
        if (courseAEntity == null)
        {
            Fail("8. EDIT COURSE TEST", "Course A not found in database.");
            return;
        }

        courseAEntity.Title = "Lesson Test Course A (Updated Title)";
        courseAEntity.Description = "Updated Course A Description";
        courseAEntity.Code = "TEST-LES-A-REV";

        var courseUpdated = await courseService.UpdateCourseAsync(courseAEntity);
        if (!courseUpdated)
        {
            Fail("8. EDIT COURSE TEST", "UpdateCourseAsync returned false.");
            return;
        }

        // Verify Course updated
        var refreshedCourseA = await context.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseAId);
        if (refreshedCourseA == null || refreshedCourseA.Title != "Lesson Test Course A (Updated Title)" || refreshedCourseA.Code != "TEST-LES-A-REV")
        {
            Fail("8. EDIT COURSE TEST", "Course details not updated in SQL Server.");
            return;
        }

        // Verify existing Modules remain
        var modulesAfterCourseEdit = await context.Chapters.AsNoTracking().Where(ch => ch.CourseId == courseAId).ToListAsync();
        if (modulesAfterCourseEdit.Count != 1 || modulesAfterCourseEdit[0].Id != moduleAId)
        {
            Fail("8. EDIT COURSE TEST", "Existing Module A was lost or corrupted after editing Course!");
            return;
        }

        // Verify existing Lessons remain
        var lessonsAfterCourseEdit = await context.CourseItems.Include(ci => ci.Lesson).AsNoTracking().Where(ci => ci.ChapterId == moduleAId).ToListAsync();
        if (lessonsAfterCourseEdit.Count != 1 || lessonsAfterCourseEdit[0].Id != lessonACourseItemId || lessonsAfterCourseEdit[0].Lesson == null)
        {
            Fail("8. EDIT COURSE TEST", "Existing Lesson A was lost or unlinked after editing Course!");
            return;
        }

        // Verify Curriculum relationships remain valid
        var curriculumAfterCourseEdit = await curriculumService.GetCurriculumAsync(courseAId);
        if (curriculumAfterCourseEdit == null || curriculumAfterCourseEdit.Modules.Count != 1 ||
            curriculumAfterCourseEdit.Modules[0].Items.Count != 1 ||
            curriculumAfterCourseEdit.Modules[0].Items[0].Id != lessonACourseItemId)
        {
            Fail("8. EDIT COURSE TEST", "Curriculum hierarchy broken after editing Course information!");
            return;
        }

        Pass("8. EDIT COURSE TEST: Course info updated successfully; existing Modules and Lessons remain intact with valid hierarchy");

        // =========================================================================
        // 9. EDIT MODULE TEST
        // =========================================================================
        // Edit Module information.
        // Verify:
        // - Same Module record updated
        // - Lessons remain attached
        // - No new Module created
        // - Position remains valid if applicable
        // =========================================================================
        int totalModulesBeforeModEdit = await context.Chapters.CountAsync();

        var editModModel = new ModuleEditViewModel
        {
            Id = moduleAId,
            CourseId = courseAId,
            Title = "Module A: Modern C# & .NET 10 (Updated)",
            Position = 1
        };

        var editModResult = await curriculumService.UpdateChapterAsync(editModModel);
        if (!editModResult.IsSuccess)
        {
            Fail("9. EDIT MODULE TEST", $"UpdateChapterAsync failed: {editModResult.Message}");
            return;
        }

        int totalModulesAfterModEdit = await context.Chapters.CountAsync();
        if (totalModulesAfterModEdit != totalModulesBeforeModEdit)
        {
            Fail("9. EDIT MODULE TEST", "New Module was created instead of updating existing Module record!");
            return;
        }

        var updatedModuleInDb = await context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == moduleAId);
        if (updatedModuleInDb == null || updatedModuleInDb.Title != editModModel.Title || updatedModuleInDb.Position != 1)
        {
            Fail("9. EDIT MODULE TEST", "Module record was not updated correctly in SQL Server.");
            return;
        }

        // Verify Lessons remain attached
        var lessonsAttached = await context.CourseItems.AsNoTracking().Where(ci => ci.ChapterId == moduleAId).ToListAsync();
        if (lessonsAttached.Count != 1 || lessonsAttached[0].Id != lessonACourseItemId)
        {
            Fail("9. EDIT MODULE TEST", "Lessons were detached from Module after editing Module information!");
            return;
        }

        Pass("9. EDIT MODULE TEST: Same Module record updated in-place; Lessons remain attached; no new Module created; Position valid");

        // =========================================================================
        // 10. EDIT LESSON TEST
        // =========================================================================
        // Edit:
        // - Lesson Title
        // - ContentHtml if supported
        // - VideoUrl if supported
        // Verify:
        // - Same CourseItem record updated
        // - Same Lesson record updated
        // - Parent Module remains unchanged
        // - No duplicate Lesson created
        // =========================================================================
        int totalCourseItemsBeforeLessonEdit = await context.CourseItems.CountAsync();
        int totalLessonsBeforeLessonEdit = await context.Lessons.CountAsync();

        var lessonAItem = await context.CourseItems.Include(ci => ci.Lesson).AsNoTracking().FirstOrDefaultAsync(ci => ci.Id == lessonACourseItemId);
        int lessonAChildLessonId = lessonAItem?.Lesson?.Id ?? 0;

        var editLessonModel = new LessonEditViewModel
        {
            CourseItemId = lessonACourseItemId,
            LessonId = lessonAChildLessonId,
            ChapterId = moduleAId,
            CourseId = courseAId,
            Title = "Lesson A: C# & .NET 10 Deep Dive (Updated)",
            ContentHtml = "<p>Updated comprehensive C# .NET 10 guide.</p>",
            VideoUrl = "https://youtube.com/watch?v=updatedLessonA",
            Position = 1
        };

        var editLessonResult = await curriculumService.UpdateLessonAsync(editLessonModel);
        if (!editLessonResult.IsSuccess)
        {
            Fail("10. EDIT LESSON TEST", $"UpdateLessonAsync failed: {editLessonResult.Message}");
            return;
        }

        int totalCourseItemsAfterLessonEdit = await context.CourseItems.CountAsync();
        int totalLessonsAfterLessonEdit = await context.Lessons.CountAsync();
        if (totalCourseItemsAfterLessonEdit != totalCourseItemsBeforeLessonEdit || totalLessonsAfterLessonEdit != totalLessonsBeforeLessonEdit)
        {
            Fail("10. EDIT LESSON TEST", "Duplicate CourseItem or Lesson row created instead of in-place update!");
            return;
        }

        var updatedLessonItemInDb = await context.CourseItems.Include(ci => ci.Lesson).AsNoTracking().FirstOrDefaultAsync(ci => ci.Id == lessonACourseItemId);
        if (updatedLessonItemInDb == null ||
            updatedLessonItemInDb.Title != editLessonModel.Title ||
            updatedLessonItemInDb.ChapterId != moduleAId ||
            updatedLessonItemInDb.Lesson == null ||
            updatedLessonItemInDb.Lesson.Id != lessonAChildLessonId ||
            updatedLessonItemInDb.Lesson.ContentHtml != editLessonModel.ContentHtml ||
            updatedLessonItemInDb.Lesson.VideoUrl != editLessonModel.VideoUrl)
        {
            Fail("10. EDIT LESSON TEST", "Lesson fields or parent relationship mismatch after edit in SQL Server.");
            return;
        }

        Pass("10. EDIT LESSON TEST: Same CourseItem and Lesson rows updated in-place; parent Module unchanged; no duplicate records");

        // =========================================================================
        // 11. REFRESH TEST
        // =========================================================================
        // After every important operation:
        // Refresh browser / fetch fresh context.
        // Verify data remains.
        // Test: Course, Module, Lesson.
        // No data should disappear after refresh.
        // =========================================================================
        // Simulate fresh HTTP request / browser refresh by using a new DbContext scope
        using (var refreshScope = serviceProvider.CreateScope())
        {
            var freshContext = refreshScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var freshCurriculumService = refreshScope.ServiceProvider.GetRequiredService<ICurriculumService>();

            var freshCourse = await freshContext.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseAId);
            if (freshCourse == null || freshCourse.Title != "Lesson Test Course A (Updated Title)")
            {
                Fail("11. REFRESH TEST (Course)", "Course data missing or not persisted after context refresh!");
                return;
            }

            var freshModule = await freshContext.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == moduleAId);
            if (freshModule == null || freshModule.Title != "Module A: Modern C# & .NET 10 (Updated)")
            {
                Fail("11. REFRESH TEST (Module)", "Module data missing or not persisted after context refresh!");
                return;
            }

            var freshLesson = await freshContext.CourseItems.Include(ci => ci.Lesson).AsNoTracking().FirstOrDefaultAsync(ci => ci.Id == lessonACourseItemId);
            if (freshLesson == null || freshLesson.Title != "Lesson A: C# & .NET 10 Deep Dive (Updated)" || freshLesson.Lesson == null)
            {
                Fail("11. REFRESH TEST (Lesson)", "Lesson data missing or not persisted after context refresh!");
                return;
            }

            var freshCurriculum = await freshCurriculumService.GetCurriculumAsync(courseAId);
            if (freshCurriculum == null || freshCurriculum.Modules.Count != 1 || freshCurriculum.Modules[0].Items.Count != 1)
            {
                Fail("11. REFRESH TEST (Curriculum)", "Curriculum graph failed to load properly after context refresh!");
                return;
            }
        }
        Pass("11. REFRESH TEST: Course, Module, and Lesson data verified persistent across fresh DbContext instances; zero data loss");

        // =========================================================================
        // 12. APPLICATION RESTART TEST
        // =========================================================================
        // 1. Create Course
        // 2. Create Module
        // 3. Create Lesson
        // 4. Stop application (dispose scopes)
        // 5. Restart application (create fresh root scope)
        // 6. Login again / Open Curriculum
        // Expected: All data remains. This confirms SQL Server persistence.
        // =========================================================================
        int courseCId = 0;
        int moduleCId = 0;
        int lessonCCourseItemId = 0;

        using (var restartScope1 = serviceProvider.CreateScope())
        {
            var ctx1 = restartScope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var currSvc1 = restartScope1.ServiceProvider.GetRequiredService<ICurriculumService>();

            var courseC = new Course
            {
                Code = "TEST-LES-C",
                Title = "Restart Test Course C",
                Description = "Testing persistence across application restart",
                MajorId = major.Id,
                SemesterId = semester.Id,
                TeacherId = teacher.Id,
                Status = CourseStatus.Draft,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx1.Courses.Add(courseC);
            await ctx1.SaveChangesAsync();
            courseCId = courseC.Id;

            var modCResult = await currSvc1.CreateChapterAsync(courseCId, "Module C1: Lifecycle", 1);
            moduleCId = modCResult.EntityId!.Value;

            var lessonCResult = await currSvc1.CreateLessonAsync(moduleCId, "Lesson C1: Persistence Proof", "<p>Persisted</p>", "https://youtube.com/watch?v=persisted", 1, courseCId);
            lessonCCourseItemId = lessonCResult.EntityId!.Value;
        } // Scope 1 disposed, DbContext disposed (Simulating application shutdown)

        // Simulate application restart: New root scope, new DbContext, new service resolution
        using (var restartScope2 = serviceProvider.CreateScope())
        {
            var ctx2 = restartScope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var currSvc2 = restartScope2.ServiceProvider.GetRequiredService<ICurriculumService>();

            var reloadedCurriculum = await currSvc2.GetCurriculumAsync(courseCId);
            if (reloadedCurriculum == null)
            {
                Fail("12. APPLICATION RESTART TEST", "Course C curriculum not found after simulated application restart!");
                return;
            }
            if (reloadedCurriculum.Modules.Count != 1 || reloadedCurriculum.Modules[0].Id != moduleCId)
            {
                Fail("12. APPLICATION RESTART TEST", "Module C1 not persisted or corrupted after simulated application restart!");
                return;
            }
            if (reloadedCurriculum.Modules[0].Items.Count != 1 || reloadedCurriculum.Modules[0].Items[0].Id != lessonCCourseItemId)
            {
                Fail("12. APPLICATION RESTART TEST", "Lesson C1 not persisted or unlinked after simulated application restart!");
                return;
            }

            // Cleanup Course C fixtures
            var courseCToDelete = await ctx2.Courses.FindAsync(courseCId);
            if (courseCToDelete != null)
            {
                ctx2.Courses.Remove(courseCToDelete);
                await ctx2.SaveChangesAsync();
            }
        }
        Pass("12. APPLICATION RESTART TEST: Data persisted across application process/context lifecycle in SQL Server");

        // =========================================================================
        // 13. CROSS-COURSE OWNERSHIP TEST
        // =========================================================================
        // Create: Course A, Course B
        // Create: Module A in Course A
        // Attempt: Access / edit / delete Module A through Course B.
        // Expected: Rejected. No modification allowed.
        // =========================================================================
        // Update Module A expecting Course B
        var crossCourseModUpdate = await curriculumService.UpdateChapterAsync(moduleAId, "Cross-Course Hack", 1, expectedCourseId: courseBId);
        if (crossCourseModUpdate.IsSuccess || crossCourseModUpdate.Status != CurriculumOperationStatus.ValidationError)
        {
            Fail("13. CROSS-COURSE OWNERSHIP TEST (Update)", "UpdateChapterAsync allowed updating Module A under Course B context!");
            return;
        }

        // Delete Module A expecting Course B
        var crossCourseModDelete = await curriculumService.DeleteChapterAsync(moduleAId, expectedCourseId: courseBId);
        if (crossCourseModDelete.IsSuccess || crossCourseModDelete.Status != CurriculumOperationStatus.ValidationError)
        {
            Fail("13. CROSS-COURSE OWNERSHIP TEST (Delete)", "DeleteChapterAsync allowed deleting Module A under Course B context!");
            return;
        }

        // Verify Module A unchanged in SQL Server
        var modAInDb = await context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == moduleAId);
        if (modAInDb == null || modAInDb.CourseId != courseAId)
        {
            Fail("13. CROSS-COURSE OWNERSHIP TEST (Integrity)", "Module A ownership corrupted in SQL Server!");
            return;
        }

        Pass("13. CROSS-COURSE OWNERSHIP TEST: Module A access/modification through Course B strictly rejected with ValidationError");

        // =========================================================================
        // 14. CROSS-MODULE LESSON TEST
        // =========================================================================
        // Create: Module A, Module B
        // Create: Lesson A in Module A
        // Attempt: Edit or delete Lesson A using Module B context.
        // Expected: Rejected. No modification allowed.
        // =========================================================================
        // Edit Lesson A specifying Module B
        var crossModLessonEdit = new LessonEditViewModel
        {
            CourseItemId = lessonACourseItemId,
            ChapterId = moduleBId, // Mismatched Module!
            CourseId = courseAId,
            Title = "Cross-Module Hack Title",
            Position = 1
        };
        var crossModEditResult = await curriculumService.UpdateLessonAsync(crossModLessonEdit);
        if (crossModEditResult.IsSuccess || crossModEditResult.Status != CurriculumOperationStatus.ValidationError)
        {
            Fail("14. CROSS-MODULE LESSON TEST (Edit)", "UpdateLessonAsync allowed editing Lesson A using Module B context!");
            return;
        }

        // Delete Lesson A specifying Module B
        var crossModLessonDelete = await curriculumService.DeleteLessonAsync(lessonACourseItemId, expectedChapterId: moduleBId, expectedCourseId: courseAId);
        if (crossModLessonDelete.IsSuccess || crossModLessonDelete.Status != CurriculumOperationStatus.ValidationError)
        {
            Fail("14. CROSS-MODULE LESSON TEST (Delete)", "DeleteLessonAsync allowed deleting Lesson A using Module B context!");
            return;
        }

        // Get Lesson A specifying Module B
        var crossModLessonGet = await curriculumService.GetLessonByIdAsync(lessonACourseItemId, expectedChapterId: moduleBId);
        if (crossModLessonGet != null)
        {
            Fail("14. CROSS-MODULE LESSON TEST (Get)", "GetLessonByIdAsync returned Lesson A when expectedChapterId was Module B!");
            return;
        }

        // Verify Lesson A unchanged in SQL Server
        var lessonAInDb = await context.CourseItems.AsNoTracking().FirstOrDefaultAsync(ci => ci.Id == lessonACourseItemId);
        if (lessonAInDb == null || lessonAInDb.ChapterId != moduleAId)
        {
            Fail("14. CROSS-MODULE LESSON TEST (Integrity)", "Lesson A parent relationship was corrupted in SQL Server!");
            return;
        }

        Pass("14. CROSS-MODULE LESSON TEST: Lesson A edit, delete, and get through Module B context strictly rejected; relationships preserved");

        // =========================================================================
        // 15. INVALID ID TEST
        // =========================================================================
        // Test invalid:
        // - Course ID
        // - Module/Chapter ID
        // - CourseItem ID
        // - Lesson ID
        // Expected: User-friendly response, No crash, No raw exception, No SQL error exposed.
        // =========================================================================
        try
        {
            // Invalid Course ID
            var invalidCourse1 = await curriculumService.GetCurriculumAsync(-1);
            var invalidCourse2 = await curriculumService.GetCurriculumAsync(999999);
            if (invalidCourse1 != null || invalidCourse2 != null)
            {
                Fail("15. INVALID ID TEST (Course)", "Expected null for invalid course IDs.");
                return;
            }

            // Invalid Module/Chapter ID
            var invalidModLessons = await curriculumService.GetLessonsByChapterIdAsync(999999);
            if (invalidModLessons != null)
            {
                Fail("15. INVALID ID TEST (Module Lessons)", "Expected null for non-existent module.");
                return;
            }

            var invalidModCreate = await curriculumService.CreateLessonAsync(999999, "Invalid Mod Lesson");
            if (invalidModCreate.IsSuccess || invalidModCreate.Status != CurriculumOperationStatus.NotFound)
            {
                Fail("15. INVALID ID TEST (Create in Ghost Module)", "Expected NotFound for ghost module.");
                return;
            }

            var invalidModEdit = await curriculumService.UpdateChapterAsync(999999, "Ghost Mod Edit", 1);
            if (invalidModEdit.IsSuccess || invalidModEdit.Status != CurriculumOperationStatus.NotFound)
            {
                Fail("15. INVALID ID TEST (Edit Ghost Module)", "Expected NotFound for editing ghost module.");
                return;
            }

            var invalidModDelete = await curriculumService.DeleteChapterAsync(999999);
            if (invalidModDelete.IsSuccess || invalidModDelete.Status != CurriculumOperationStatus.NotFound)
            {
                Fail("15. INVALID ID TEST (Delete Ghost Module)", "Expected NotFound for deleting ghost module.");
                return;
            }

            // Invalid CourseItem / Lesson ID
            var invalidLessonGet = await curriculumService.GetLessonByIdAsync(999999);
            if (invalidLessonGet != null)
            {
                Fail("15. INVALID ID TEST (Get Ghost Lesson)", "Expected null for ghost lesson ID.");
                return;
            }

            var invalidLessonEdit = await curriculumService.UpdateLessonAsync(new LessonEditViewModel
            {
                CourseItemId = 999999,
                ChapterId = moduleAId,
                CourseId = courseAId,
                Title = "Ghost Lesson Update"
            });
            if (invalidLessonEdit.IsSuccess || invalidLessonEdit.Status != CurriculumOperationStatus.NotFound)
            {
                Fail("15. INVALID ID TEST (Edit Ghost Lesson)", "Expected NotFound for ghost lesson update.");
                return;
            }

            var invalidLessonDelete = await curriculumService.DeleteLessonAsync(999999, moduleAId, courseAId);
            if (invalidLessonDelete.IsSuccess || invalidLessonDelete.Status != CurriculumOperationStatus.NotFound)
            {
                Fail("15. INVALID ID TEST (Delete Ghost Lesson)", "Expected NotFound for ghost lesson delete.");
                return;
            }
        }
        catch (Exception ex)
        {
            Fail("15. INVALID ID TEST (Crash/Exception)", $"Unhandled exception exposed: {ex.Message}");
            return;
        }

        Pass("15. INVALID ID TEST: All invalid IDs return user-friendly NotFound or null responses; no crashes or exposed SQL exceptions");

        // =========================================================================
        // 16. SAFE LESSON DELETE
        // =========================================================================
        // CASE A: Lesson has no CourseProgress.
        // Expected: Lesson can be deleted safely.
        // Verify: Lesson deleted, CourseItem handled correctly, No orphan records.
        // -------------------------------------------------------------------------
        // CASE B: Lesson has CourseProgress.
        // Expected: Deletion blocked.
        // Verify: Lesson remains, CourseItem remains, CourseProgress remains, User receives meaningful message.
        // =========================================================================
        // CASE A: Clean deletion
        var tempLessonResult = await curriculumService.CreateLessonAsync(moduleAId, "Temporary Lesson Case A", "<p>No progress</p>", null, 2, courseAId);
        int tempCourseItemId = tempLessonResult.EntityId!.Value;
        int tempLessonId = tempLessonResult.SecondaryId!.Value;

        var deleteCaseAResult = await curriculumService.DeleteLessonAsync(tempCourseItemId, moduleAId, courseAId);
        if (!deleteCaseAResult.IsSuccess)
        {
            Fail("16. SAFE LESSON DELETE (CASE A)", $"Failed to delete empty lesson: {deleteCaseAResult.Message}");
            return;
        }

        var deletedItem = await context.CourseItems.FindAsync(tempCourseItemId);
        var deletedChildLesson = await context.Lessons.FindAsync(tempLessonId);
        if (deletedItem != null || deletedChildLesson != null)
        {
            Fail("16. SAFE LESSON DELETE (CASE A)", "CourseItem or child Lesson row was not cleanly deleted from SQL Server.");
            return;
        }

        // CASE B: Protected deletion
        var protectedLessonResult = await curriculumService.CreateLessonAsync(moduleAId, "Protected Lesson Case B", "<p>Has progress</p>", null, 2, courseAId);
        int protectedCourseItemId = protectedLessonResult.EntityId!.Value;
        int protectedLessonId = protectedLessonResult.SecondaryId!.Value;

        var progressRecord = new CourseProgress
        {
            CourseItemId = protectedCourseItemId,
            StudentId = student.Id,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        };
        context.CourseProgresses.Add(progressRecord);
        await context.SaveChangesAsync();

        var deleteCaseBResult = await curriculumService.DeleteLessonAsync(protectedCourseItemId, moduleAId, courseAId);
        if (deleteCaseBResult.IsSuccess || deleteCaseBResult.Status != CurriculumOperationStatus.BlockedByStudentData)
        {
            Fail("16. SAFE LESSON DELETE (CASE B)", $"Expected BlockedByStudentData, got status {deleteCaseBResult.Status}");
            return;
        }
        if (string.IsNullOrWhiteSpace(deleteCaseBResult.Message) || !deleteCaseBResult.Message.Contains("progress"))
        {
            Fail("16. SAFE LESSON DELETE (CASE B)", "User did not receive a meaningful progress protection message.");
            return;
        }

        // Verify in SQL Server: all preserved!
        var preservedItem = await context.CourseItems.FindAsync(protectedCourseItemId);
        var preservedLesson = await context.Lessons.FindAsync(protectedLessonId);
        var preservedProgress = await context.CourseProgresses.FirstOrDefaultAsync(cp => cp.CourseItemId == protectedCourseItemId);

        if (preservedItem == null || preservedLesson == null || preservedProgress == null)
        {
            Fail("16. SAFE LESSON DELETE (CASE B)", "Protected lesson, item, or progress was deleted from SQL Server!");
            return;
        }

        // Cleanup progress record & lesson
        context.CourseProgresses.Remove(preservedProgress);
        await context.SaveChangesAsync();
        await curriculumService.DeleteLessonAsync(protectedCourseItemId, moduleAId, courseAId);

        Pass("16. SAFE LESSON DELETE: CASE A deleted cleanly without orphans; CASE B blocked by student progress and user received meaningful message");

        // =========================================================================
        // 17. SAFE MODULE DELETE TEST
        // =========================================================================
        // CASE A: Module has no content.
        // Expected: Deletion allowed.
        // -------------------------------------------------------------------------
        // CASE B: Module contains Lessons.
        // Test existing safe deletion behavior:
        // Verify: No orphan CourseItems, No orphan Lessons, Student data protected.
        // =========================================================================
        // CASE A: Empty Module Deletion
        var emptyModResult = await curriculumService.CreateChapterAsync(courseAId, "Empty Module Case A", 99);
        int emptyModId = emptyModResult.EntityId!.Value;

        var deleteEmptyModResult = await curriculumService.DeleteChapterAsync(emptyModId, courseAId);
        if (!deleteEmptyModResult.IsSuccess)
        {
            Fail("17. SAFE MODULE DELETE TEST (CASE A)", $"Empty module deletion failed: {deleteEmptyModResult.Message}");
            return;
        }
        var emptyModInDb = await context.Chapters.FindAsync(emptyModId);
        if (emptyModInDb != null)
        {
            Fail("17. SAFE MODULE DELETE TEST (CASE A)", "Empty module still exists in SQL Server after deletion.");
            return;
        }

        // CASE B1: Module contains Lessons with Student Progress -> Blocked!
        var modWithContentResult = await curriculumService.CreateChapterAsync(courseAId, "Content Module Case B", 98);
        int modWithContentId = modWithContentResult.EntityId!.Value;

        var modLessonResult = await curriculumService.CreateLessonAsync(modWithContentId, "Mod B Lesson", "<p>Content</p>", null, 1, courseAId);
        int modLessonCourseItemId = modLessonResult.EntityId!.Value;
        int modLessonChildId = modLessonResult.SecondaryId!.Value;

        var modProgress = new CourseProgress
        {
            CourseItemId = modLessonCourseItemId,
            StudentId = student.Id,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        };
        context.CourseProgresses.Add(modProgress);
        await context.SaveChangesAsync();

        var deleteBlockedModResult = await curriculumService.DeleteChapterAsync(modWithContentId, courseAId);
        if (deleteBlockedModResult.IsSuccess || deleteBlockedModResult.Status != CurriculumOperationStatus.BlockedByStudentData)
        {
            Fail("17. SAFE MODULE DELETE TEST (CASE B1 - Blocked)", $"Expected BlockedByStudentData for module with student activity, got {deleteBlockedModResult.Status}");
            return;
        }

        // Verify module and items preserved
        var preservedMod = await context.Chapters.FindAsync(modWithContentId);
        var preservedModItem = await context.CourseItems.FindAsync(modLessonCourseItemId);
        if (preservedMod == null || preservedModItem == null)
        {
            Fail("17. SAFE MODULE DELETE TEST (CASE B1 - Preservation)", "Module or items were deleted despite active student progress!");
            return;
        }

        // CASE B2: Remove student progress, then delete module -> Clean cascade!
        context.CourseProgresses.Remove(modProgress);
        await context.SaveChangesAsync();

        var deleteCascadeModResult = await curriculumService.DeleteChapterAsync(modWithContentId, courseAId);
        if (!deleteCascadeModResult.IsSuccess)
        {
            Fail("17. SAFE MODULE DELETE TEST (CASE B2 - Clean Cascade)", $"Failed to delete module: {deleteCascadeModResult.Message}");
            return;
        }

        // Verify in SQL Server: Module deleted, CourseItem deleted, child Lesson deleted, NO orphan records!
        var cascadeModInDb = await context.Chapters.FindAsync(modWithContentId);
        var cascadeItemInDb = await context.CourseItems.FindAsync(modLessonCourseItemId);
        var cascadeLessonInDb = await context.Lessons.FindAsync(modLessonChildId);

        if (cascadeModInDb != null)
        {
            Fail("17. SAFE MODULE DELETE TEST (CASE B2 - Cascade Check)", "Module row still exists in SQL Server.");
            return;
        }
        if (cascadeItemInDb != null)
        {
            Fail("17. SAFE MODULE DELETE TEST (CASE B2 - Cascade Check)", "CourseItem left as orphan in SQL Server.");
            return;
        }
        if (cascadeLessonInDb != null)
        {
            Fail("17. SAFE MODULE DELETE TEST (CASE B2 - Cascade Check)", "Child Lesson row left as orphan in SQL Server.");
            return;
        }

        Pass("17. SAFE MODULE DELETE TEST: CASE A allowed for empty module; CASE B protected student data when progress existed, and cleanly cascade deleted Module, CourseItems, and Lessons without orphans when safe");

        // =========================================================================
        // 18. CASCADE DATA INTEGRITY TEST
        // =========================================================================
        // Inspect actual database behavior.
        // Verify:
        // Course
        // ↓
        // Chapter
        // ↓
        // CourseItem
        // ↓
        // Lesson
        // No orphan records after allowed deletion.
        // Check:
        // - Chapters
        // - CourseItems
        // - Lessons
        // - CourseProgresses
        // =========================================================================
        var cascadeCourse = new Course
        {
            Code = "TEST-CASCADE-X",
            Title = "Cascade Data Integrity Test Course",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(cascadeCourse);
        await context.SaveChangesAsync();
        int cascadeCourseId = cascadeCourse.Id;

        var cascadeModResult = await curriculumService.CreateChapterAsync(cascadeCourseId, "Cascade Module", 1);
        int cascadeModId = cascadeModResult.EntityId!.Value;

        var cascadeLes1 = await curriculumService.CreateLessonAsync(cascadeModId, "Cascade Item 1", "<p>Item 1</p>", null, 1, cascadeCourseId);
        int cascadeItem1Id = cascadeLes1.EntityId!.Value;
        int cascadeLesson1Id = cascadeLes1.SecondaryId!.Value;

        var cascadeLes2 = await curriculumService.CreateLessonAsync(cascadeModId, "Cascade Item 2", "<p>Item 2</p>", null, 2, cascadeCourseId);
        int cascadeItem2Id = cascadeLes2.EntityId!.Value;
        int cascadeLesson2Id = cascadeLes2.SecondaryId!.Value;

        // Step 1: Delete Lesson 1 -> Verifies CourseItem -> Lesson cascade
        var delLesResult = await curriculumService.DeleteLessonAsync(cascadeItem1Id, cascadeModId, cascadeCourseId);
        if (!delLesResult.IsSuccess)
        {
            Fail("18. CASCADE DATA INTEGRITY TEST (Lesson Delete)", $"Failed: {delLesResult.Message}");
            return;
        }
        var orphanLesson1 = await context.Lessons.AnyAsync(l => l.Id == cascadeLesson1Id || l.CourseItemId == cascadeItem1Id);
        var orphanItem1 = await context.CourseItems.AnyAsync(ci => ci.Id == cascadeItem1Id);
        if (orphanLesson1 || orphanItem1)
        {
            Fail("18. CASCADE DATA INTEGRITY TEST (Lesson Cascade)", "Orphan row detected in Lessons or CourseItems after deleting Lesson 1!");
            return;
        }

        // Step 2: Delete Module -> Verifies Chapter -> CourseItem -> Lesson cascade
        var delModResult = await curriculumService.DeleteChapterAsync(cascadeModId, cascadeCourseId);
        if (!delModResult.IsSuccess)
        {
            Fail("18. CASCADE DATA INTEGRITY TEST (Module Delete)", $"Failed: {delModResult.Message}");
            return;
        }
        var orphanChapter = await context.Chapters.AnyAsync(ch => ch.Id == cascadeModId);
        var orphanItem2 = await context.CourseItems.AnyAsync(ci => ci.Id == cascadeItem2Id || ci.ChapterId == cascadeModId);
        var orphanLesson2 = await context.Lessons.AnyAsync(l => l.Id == cascadeLesson2Id || l.CourseItemId == cascadeItem2Id);
        var orphanProgress = await context.CourseProgresses.AnyAsync(cp => cp.CourseItemId == cascadeItem1Id || cp.CourseItemId == cascadeItem2Id);

        if (orphanChapter || orphanItem2 || orphanLesson2 || orphanProgress)
        {
            Fail("18. CASCADE DATA INTEGRITY TEST (Module Cascade)", "Orphan row detected in Chapters, CourseItems, Lessons, or CourseProgresses!");
            return;
        }

        // Clean up course container
        context.Courses.Remove(cascadeCourse);
        await context.SaveChangesAsync();

        Pass("18. CASCADE DATA INTEGRITY TEST: Database inspected directly; verified Course -> Chapter -> CourseItem -> Lesson clean cascade without orphans in Chapters, CourseItems, Lessons, or CourseProgresses");

        // =========================================================================
        // 19. COURSE DELETE REGRESSION TEST
        // =========================================================================
        // Verify:
        // - Deletion blocked if student data exists (enrollments, progress) with meaningful feedback
        // - Deletion allowed when safe, cleanly removing Chapters, CourseItems, Lessons
        // - Sibling courses and curriculums remain unaffected
        // =========================================================================
        var courseReg = new Course
        {
            Code = "TEST-COURSE-REG",
            Title = "Regression Delete Test Course",
            MajorId = major.Id,
            SemesterId = semester.Id,
            TeacherId = teacher.Id,
            Status = CourseStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Courses.Add(courseReg);
        await context.SaveChangesAsync();
        int courseRegId = courseReg.Id;

        var regMod1Result = await curriculumService.CreateChapterAsync(courseRegId, "Reg Module 1", 1);
        int regMod1Id = regMod1Result.EntityId!.Value;
        var regLes1Result = await curriculumService.CreateLessonAsync(regMod1Id, "Reg Lesson 1", "<p>Content</p>", null, 1, courseRegId);
        int regItem1Id = regLes1Result.EntityId!.Value;

        // Subcase A: Deletion blocked when student data exists
        var studentProgReg = new CourseProgress
        {
            CourseItemId = regItem1Id,
            StudentId = student.Id,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        };
        context.CourseProgresses.Add(studentProgReg);
        await context.SaveChangesAsync();

        var blockedDeleteResult = await courseService.DeleteCourseAsync(courseRegId);
        if (blockedDeleteResult.IsSuccess || !blockedDeleteResult.IsBlockedByStudentData)
        {
            Fail("19. COURSE DELETE REGRESSION TEST (Blocked)", "Course deletion should be blocked when student progress exists!");
            return;
        }
        if (string.IsNullOrWhiteSpace(blockedDeleteResult.Message) || !blockedDeleteResult.Message.Contains("progress record"))
        {
            Fail("19. COURSE DELETE REGRESSION TEST (Feedback)", "User feedback for blocked course deletion did not explain student activity dependency.");
            return;
        }

        // Verify in SQL Server: Course and curriculum preserved
        var preservedRegCourse = await context.Courses.FindAsync(courseRegId);
        var preservedRegChapter = await context.Chapters.FindAsync(regMod1Id);
        var preservedRegItem = await context.CourseItems.FindAsync(regItem1Id);
        if (preservedRegCourse == null || preservedRegChapter == null || preservedRegItem == null)
        {
            Fail("19. COURSE DELETE REGRESSION TEST (Preservation)", "Course or curriculum rows were deleted despite student data dependency!");
            return;
        }

        // Subcase B: Allowed deletion when safe
        context.CourseProgresses.Remove(studentProgReg);
        await context.SaveChangesAsync();

        var safeCourseDelete = await courseService.DeleteCourseAsync(courseRegId);
        if (!safeCourseDelete.IsSuccess)
        {
            Fail("19. COURSE DELETE REGRESSION TEST (Allowed)", $"Course deletion failed: {safeCourseDelete.Message}");
            return;
        }

        // Verify in SQL Server: Course, Chapter, CourseItem, Lesson all cleanly deleted
        var deletedRegCourse = await context.Courses.FindAsync(courseRegId);
        var deletedRegChapter = await context.Chapters.FindAsync(regMod1Id);
        var deletedRegItem = await context.CourseItems.FindAsync(regItem1Id);
        var deletedRegLesson = await context.Lessons.AnyAsync(l => l.CourseItemId == regItem1Id);

        if (deletedRegCourse != null || deletedRegChapter != null || deletedRegItem != null || deletedRegLesson)
        {
            Fail("19. COURSE DELETE REGRESSION TEST (Cascade Check)", "Course deletion left behind orphan Chapter, CourseItem, or Lesson records!");
            return;
        }

        // Verify other courses (Course A and Course B) are completely intact
        var checkCourseA = await context.Courses.FindAsync(courseAId);
        var checkCourseB = await context.Courses.FindAsync(courseBId);
        if (checkCourseA == null || checkCourseB == null)
        {
            Fail("19. COURSE DELETE REGRESSION TEST (Sibling Course Check)", "Course deletion accidentally corrupted or deleted sibling courses!");
            return;
        }

        Pass("19. COURSE DELETE REGRESSION TEST: Safe course deletion policy verified; deletion blocked with user feedback when dependencies exist; clean cascade without orphan records when safe; sibling courses unaffected");

        // =========================================================================
        // FINAL CLEANUP
        // =========================================================================
        var courseAFinal = await context.Courses.FirstOrDefaultAsync(c => c.Id == courseAId);
        if (courseAFinal != null) context.Courses.Remove(courseAFinal);
        var courseBFinal = await context.Courses.FirstOrDefaultAsync(c => c.Id == courseBId);
        if (courseBFinal != null) context.Courses.Remove(courseBFinal);
        await context.SaveChangesAsync();

        Pass("Final test fixtures cleaned up; SQL Server database restored to pristine state");

        // =========================================================================
        // SUMMARY
        // =========================================================================
        Console.WriteLine("\n=================================================================");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"🎉  ALL {testsPassed}/{totalTests} LESSON & CURRICULUM AUDIT TESTS PASSED SUCCESSFULLY!");
        Console.ResetColor();
        Console.WriteLine("=================================================================\n");
    }
}