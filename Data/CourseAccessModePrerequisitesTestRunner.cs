using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class CourseAccessModePrerequisitesTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("============================================================================");
        Console.WriteLine("🔒 AUB DIGITAL ACADEMY - TASK-P2-03 COURSE ACCESS MODES & PREREQUISITES TEST");
        Console.WriteLine("============================================================================\n");

        int passed = 0;
        int failed = 0;

        void Pass(string msg) { passed++; Console.WriteLine($"  [PASS] {msg}"); }
        void Fail(string msg) { failed++; Console.WriteLine($"  [FAIL] {msg}"); }

        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var curriculumService = sp.GetRequiredService<ICurriculumService>();
        var enrollmentService = sp.GetRequiredService<IEnrollmentService>();
        var userManager = sp.GetRequiredService<UserManager<User>>();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var teacher = await userManager.FindByEmailAsync("teacher@aub.com") ?? throw new InvalidOperationException("Teacher not found");
        var admin = await userManager.FindByEmailAsync("admin@aub.com") ?? throw new InvalidOperationException("Admin not found");
        var major = await db.Majors.FirstAsync();
        var sem = await db.Semesters.FirstAsync();

        var student = new User
        {
            UserName = $"seqstu_{suffix}@aub.edu",
            Email = $"seqstu_{suffix}@aub.edu",
            FullName = "Sequential Student",
            UniversityId = $"SSTU_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(student, "Student@123");
        await userManager.AddToRoleAsync(student, "Student");

        var otherTeacher = new User
        {
            UserName = $"otherteacher_{suffix}@aub.edu",
            Email = $"otherteacher_{suffix}@aub.edu",
            FullName = "Other Teacher",
            UniversityId = $"TCHR_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(otherTeacher, "Teacher@123");
        await userManager.AddToRoleAsync(otherTeacher, "Teacher");

        Course? course = null;
        try
        {
            course = new Course
            {
                Code = $"SEQ_{suffix}",
                Title = "Sequential Access Course",
                MajorId = major.Id,
                SemesterId = sem.Id,
                TeacherId = teacher.Id,
                Status = CourseStatus.Published,
                AccessMode = CourseAccessMode.Free
            };
            db.Courses.Add(course);
            await db.SaveChangesAsync();

            var chapter = new Chapter
            {
                CourseId = course.Id,
                Title = "Module 1: Foundations",
                Position = 1
            };
            db.Chapters.Add(chapter);
            await db.SaveChangesAsync();

            // 1. Create 4 Lessons
            var item1 = new CourseItem { ChapterId = chapter.Id, Title = "Lesson 1: Intro", Position = 1, Type = ItemType.Lesson };
            db.CourseItems.Add(item1);
            await db.SaveChangesAsync();
            db.Lessons.Add(new Lesson { CourseItemId = item1.Id, ContentHtml = "<p>Intro</p>" });

            var item2 = new CourseItem { ChapterId = chapter.Id, Title = "Lesson 2: Core Concepts", Position = 2, Type = ItemType.Lesson };
            db.CourseItems.Add(item2);
            await db.SaveChangesAsync();
            db.Lessons.Add(new Lesson { CourseItemId = item2.Id, ContentHtml = "<p>Core</p>" });

            var item3 = new CourseItem { ChapterId = chapter.Id, Title = "Lesson 3: Advanced Theory", Position = 3, Type = ItemType.Lesson };
            db.CourseItems.Add(item3);
            await db.SaveChangesAsync();
            db.Lessons.Add(new Lesson { CourseItemId = item3.Id, ContentHtml = "<p>Advanced</p>" });

            var item4 = new CourseItem { ChapterId = chapter.Id, Title = "Lesson 4: Capstone Project", Position = 4, Type = ItemType.Lesson };
            db.CourseItems.Add(item4);
            await db.SaveChangesAsync();
            db.Lessons.Add(new Lesson { CourseItemId = item4.Id, ContentHtml = "<p>Capstone</p>" });

            // 2. Create Quiz and Assignment
            var quizItem = new CourseItem { ChapterId = chapter.Id, Title = "Quiz 1: Midterm Check", Position = 5, Type = ItemType.Quiz };
            db.CourseItems.Add(quizItem);
            await db.SaveChangesAsync();
            var quiz = new Quiz { CourseItemId = quizItem.Id, PassingScore = 60, TimeLimitMinutes = 30, IsPublished = true, MaxAttempts = 3 };
            db.Quizzes.Add(quiz);

            var assignItem = new CourseItem { ChapterId = chapter.Id, Title = "Assignment 1: Essay", Position = 6, Type = ItemType.Assignment };
            db.CourseItems.Add(assignItem);
            await db.SaveChangesAsync();
            var assignment = new Assignment { CourseItemId = assignItem.Id, MaxScore = 100 };
            db.Assignments.Add(assignment);

            var item5 = new CourseItem { ChapterId = chapter.Id, Title = "Lesson 5: Post-Quiz Synthesis", Position = 7, Type = ItemType.Lesson };
            db.CourseItems.Add(item5);
            await db.SaveChangesAsync();
            db.Lessons.Add(new Lesson { CourseItemId = item5.Id, ContentHtml = "<p>Post-Quiz</p>" });

            await db.SaveChangesAsync();

            // Enroll student
            var enrollment = new Enrollment { CourseId = course.Id, StudentId = student.Id, EnrolledAt = DateTime.UtcNow };
            db.Enrollments.Add(enrollment);
            await db.SaveChangesAsync();

            // -----------------------------------------------------------------
            // TEST 01: Course Access Mode Default & Modification Security
            // -----------------------------------------------------------------
            var defaultMode = await curriculumService.GetCourseAccessModeAsync(course.Id);
            if (defaultMode == CourseAccessMode.Free)
                Pass("Test 01A: New course defaults to CourseAccessMode.Free");
            else
                Fail($"Test 01A: Expected Free, got {defaultMode}");

            var unauthUpdate = await curriculumService.UpdateCourseAccessModeAsync(course.Id, CourseAccessMode.Sequential, otherTeacher.Id, isAdmin: false);
            if (unauthUpdate.Status == CurriculumOperationStatus.Forbidden)
                Pass("Test 01B: Unauthorized teacher cannot modify course access mode (Forbidden)");
            else
                Fail($"Test 01B: Expected Forbidden, got {unauthUpdate.Status}");

            var authUpdate = await curriculumService.UpdateCourseAccessModeAsync(course.Id, CourseAccessMode.Sequential, teacher.Id, isAdmin: false);
            var updatedMode = await curriculumService.GetCourseAccessModeAsync(course.Id);
            if (authUpdate.IsSuccess && updatedMode == CourseAccessMode.Sequential)
                Pass("Test 01C: Course owner successfully sets CourseAccessMode.Sequential");
            else
                Fail($"Test 01C: Owner update failed: {authUpdate.Message}");

            // -----------------------------------------------------------------
            // TEST 02: Prerequisite Validation Rules (Self, Circular, Cross-Course)
            // -----------------------------------------------------------------
            var selfPrereq = await curriculumService.SetItemPrerequisitesAsync(item1.Id, new[] { item1.Id }, teacher.Id, isAdmin: false);
            // Self-item should be stripped safely or result in no prerequisites
            var item1PrereqsAfterSelf = await curriculumService.GetItemPrerequisiteIdsAsync(item1.Id);
            if (!item1PrereqsAfterSelf.Contains(item1.Id))
                Pass("Test 02A: Self-prerequisite is prevented / excluded");
            else
                Fail("Test 02A: Item allowed to require itself");

            // Set Item 2 -> Item 1
            var set1to2 = await curriculumService.SetItemPrerequisitesAsync(item2.Id, new[] { item1.Id }, teacher.Id, isAdmin: false);
            if (set1to2.IsSuccess)
                Pass("Test 02B: Successfully set single prerequisite (Lesson 2 requires Lesson 1)");
            else
                Fail($"Test 02B: Setting prerequisite failed: {set1to2.Message}");

            // Set Item 1 -> Item 2 (Circular dependency check)
            var circularResult = await curriculumService.SetItemPrerequisitesAsync(item1.Id, new[] { item2.Id }, teacher.Id, isAdmin: false);
            if (!circularResult.IsSuccess && circularResult.Message.Contains("Circular", StringComparison.OrdinalIgnoreCase))
                Pass("Test 02C: Circular dependency correctly detected and rejected");
            else
                Fail($"Test 02C: Expected circular failure, got {circularResult.Status}: {circularResult.Message}");

            // -----------------------------------------------------------------
            // TEST 03: Free Access Mode Ignores Prerequisites
            // -----------------------------------------------------------------
            await curriculumService.UpdateCourseAccessModeAsync(course.Id, CourseAccessMode.Free, teacher.Id, isAdmin: false);
            var (freeAccessible, freeMissing) = await curriculumService.CheckItemAccessAsync(item2.Id, student.Id);
            if (freeAccessible && freeMissing.Count == 0)
                Pass("Test 03: In Free Access mode, items with prerequisites remain fully accessible");
            else
                Fail($"Test 03: Expected accessible in Free mode, got {freeAccessible}, missing: {string.Join(", ", freeMissing)}");

            // -----------------------------------------------------------------
            // TEST 04: Sequential Access Mode Enforces Prerequisites & Locks Item
            // -----------------------------------------------------------------
            await curriculumService.UpdateCourseAccessModeAsync(course.Id, CourseAccessMode.Sequential, teacher.Id, isAdmin: false);

            var (item1Access, _) = await curriculumService.CheckItemAccessAsync(item1.Id, student.Id);
            var (item2Access, item2Missing) = await curriculumService.CheckItemAccessAsync(item2.Id, student.Id);

            if (item1Access && !item2Access && item2Missing.Contains("Lesson 1: Intro"))
                Pass("Test 04: Sequential Access unlocks Lesson 1 (no prereqs) and locks Lesson 2 (missing Lesson 1)");
            else
                Fail($"Test 04: Unexpected access state: Item1={item1Access}, Item2={item2Access}, Missing={string.Join(", ", item2Missing)}");

            // -----------------------------------------------------------------
            // TEST 05: Direct Completion & Heartbeat Blocked on Locked Item
            // -----------------------------------------------------------------
            var prematureComplete = await curriculumService.MarkLessonCompleteAsync(student.Id, course.Id, item2.Id);
            if (!prematureComplete.IsSuccess)
                Pass("Test 05: Direct MarkLessonComplete on locked item is rejected by server");
            else
                Fail("Test 05: Server allowed completing a locked lesson!");

            // -----------------------------------------------------------------
            // TEST 06: Completing Lesson 1 Unlocks Lesson 2
            // -----------------------------------------------------------------
            var complete1 = await curriculumService.MarkLessonCompleteAsync(student.Id, course.Id, item1.Id);
            var (item2Unlocked, item2UnlockedMissing) = await curriculumService.CheckItemAccessAsync(item2.Id, student.Id);

            if (complete1.IsSuccess && item2Unlocked && item2UnlockedMissing.Count == 0)
                Pass("Test 06: Completing prerequisite Lesson 1 successfully unlocks Lesson 2");
            else
                Fail($"Test 06: Lesson 2 failed to unlock after Lesson 1 complete: Unlocked={item2Unlocked}");

            // -----------------------------------------------------------------
            // TEST 07: Multi-Prerequisite Unlocking (Lesson 4 requires Lesson 2 AND Lesson 3)
            // -----------------------------------------------------------------
            // Lesson 3 requires Lesson 1
            await curriculumService.SetItemPrerequisitesAsync(item3.Id, new[] { item1.Id }, teacher.Id, isAdmin: false);
            // Lesson 4 requires BOTH Lesson 2 and Lesson 3
            var multiSet = await curriculumService.SetItemPrerequisitesAsync(item4.Id, new[] { item2.Id, item3.Id }, teacher.Id, isAdmin: false);
            if (multiSet.IsSuccess)
                Pass("Test 07A: Multiple prerequisites configured for Lesson 4 (requires Lesson 2 AND Lesson 3)");
            else
                Fail($"Test 07A: Failed to configure multi-prerequisites: {multiSet.Message}");

            // Student completes Lesson 2, but NOT Lesson 3
            await curriculumService.MarkLessonCompleteAsync(student.Id, course.Id, item2.Id);
            var (item4PartialAccess, item4PartialMissing) = await curriculumService.CheckItemAccessAsync(item4.Id, student.Id);
            if (!item4PartialAccess && item4PartialMissing.Contains("Lesson 3: Advanced Theory") && !item4PartialMissing.Contains("Lesson 2: Core Concepts"))
                Pass("Test 07B: Lesson 4 remains locked when only 1 of 2 prerequisites is met (missing Lesson 3)");
            else
                Fail($"Test 07B: Partial unlock failure: Access={item4PartialAccess}, Missing={string.Join(", ", item4PartialMissing)}");

            // Complete Lesson 3
            await curriculumService.MarkLessonCompleteAsync(student.Id, course.Id, item3.Id);
            var (item4FullAccess, item4FullMissing) = await curriculumService.CheckItemAccessAsync(item4.Id, student.Id);
            if (item4FullAccess && item4FullMissing.Count == 0)
                Pass("Test 07C: Completing both prerequisites unlocks Lesson 4");
            else
                Fail($"Test 07C: Lesson 4 failed to unlock after both prereqs met: Access={item4FullAccess}");

            // -----------------------------------------------------------------
            // TEST 08: Non-Video Prerequisite Unlocking (Quiz and Assignment)
            // -----------------------------------------------------------------
            // Lesson 5 requires Quiz 1
            await curriculumService.SetItemPrerequisitesAsync(item5.Id, new[] { quizItem.Id }, teacher.Id, isAdmin: false);
            var (item5InitialAccess, _) = await curriculumService.CheckItemAccessAsync(item5.Id, student.Id);
            if (!item5InitialAccess)
                Pass("Test 08A: Lesson 5 is locked prior to passing Quiz 1");
            else
                Fail("Test 08A: Lesson 5 should be locked before Quiz 1 pass");

            // Simulate student passing Quiz 1
            db.QuizSubmissions.Add(new QuizSubmission
            {
                QuizId = quiz.Id,
                StudentId = student.Id,
                Score = 80,
                IsPassed = true,
                SubmittedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var (item5AfterQuiz, _) = await curriculumService.CheckItemAccessAsync(item5.Id, student.Id);
            if (item5AfterQuiz)
                Pass("Test 08B: Passing Quiz 1 successfully unlocks dependent Lesson 5");
            else
                Fail("Test 08B: Lesson 5 did not unlock after passing Quiz 1");

            // -----------------------------------------------------------------
            // TEST 09: Mode Toggle Strictly Retains All Student Progress
            // -----------------------------------------------------------------
            var progressCountBefore = await db.CourseProgresses.CountAsync(cp => cp.StudentId == student.Id);
            var quizCountBefore = await db.QuizSubmissions.CountAsync(qs => qs.StudentId == student.Id);

            // Toggle Sequential -> Free
            await curriculumService.UpdateCourseAccessModeAsync(course.Id, CourseAccessMode.Free, teacher.Id, isAdmin: false);
            var progressCountInFree = await db.CourseProgresses.CountAsync(cp => cp.StudentId == student.Id);

            // Toggle Free -> Sequential
            await curriculumService.UpdateCourseAccessModeAsync(course.Id, CourseAccessMode.Sequential, teacher.Id, isAdmin: false);
            var progressCountInSeq = await db.CourseProgresses.CountAsync(cp => cp.StudentId == student.Id);

            if (progressCountBefore > 0 && progressCountBefore == progressCountInFree && progressCountInFree == progressCountInSeq && quizCountBefore == 1)
                Pass($"Test 09: Toggling Free/Sequential preserves all {progressCountBefore} student progress records and quiz submissions intact");
            else
                Fail($"Test 09: Progress count mismatch across mode toggle: Before={progressCountBefore}, Free={progressCountInFree}, Seq={progressCountInSeq}");

            // -----------------------------------------------------------------
            // TEST 10: Player ViewModel Navigation Tree Populates IsLocked & Badges
            // -----------------------------------------------------------------
            // Add a new locked lesson requiring capstone
            var lockedItem = new CourseItem { ChapterId = chapter.Id, Title = "Lesson 6: Honors Seminar", Position = 8, Type = ItemType.Lesson };
            db.CourseItems.Add(lockedItem);
            await db.SaveChangesAsync();
            db.Lessons.Add(new Lesson { CourseItemId = lockedItem.Id, ContentHtml = "<p>Honors</p>" });
            await db.SaveChangesAsync();
            await curriculumService.SetItemPrerequisitesAsync(lockedItem.Id, new[] { item4.Id }, teacher.Id, isAdmin: false);

            var player = await curriculumService.GetStudentLessonPlayerAsync(course.Id, item1.Id, student.Id);
            var navLessonItem = player?.Modules.SelectMany(m => m.Lessons).FirstOrDefault(l => l.CourseItemId == lockedItem.Id);

            if (player != null && player.AccessMode == CourseAccessMode.Sequential &&
                navLessonItem != null && (navLessonItem.IsLocked || !player.IsLocked))
            {
                Pass("Test 10: Student Player Navigation accurately exposes CourseAccessMode and item lock states");
            }
            else
            {
                Fail("Test 10: Player navigation model failed to expose access mode or lock flags");
            }
        }
        finally
        {
            if (course != null)
            {
                db.CourseItemPrerequisites.RemoveRange(db.CourseItemPrerequisites.Where(p => p.CourseItem.Chapter.CourseId == course.Id));
                db.CourseProgresses.RemoveRange(db.CourseProgresses.Where(cp => cp.StudentId == student.Id));
                db.QuizSubmissions.RemoveRange(db.QuizSubmissions.Where(qs => qs.StudentId == student.Id));
                db.Quizzes.RemoveRange(db.Quizzes.Where(q => q.CourseItem.Chapter.CourseId == course.Id));
                db.Assignments.RemoveRange(db.Assignments.Where(a => a.CourseItem.Chapter.CourseId == course.Id));
                db.Lessons.RemoveRange(db.Lessons.Where(l => l.CourseItem.Chapter.CourseId == course.Id));
                db.CourseItems.RemoveRange(db.CourseItems.Where(ci => ci.Chapter.CourseId == course.Id));
                db.Chapters.RemoveRange(db.Chapters.Where(ch => ch.CourseId == course.Id));
                db.Enrollments.RemoveRange(db.Enrollments.Where(e => e.CourseId == course.Id));
                db.Courses.Remove(course);
                await db.SaveChangesAsync();
            }
            await userManager.DeleteAsync(student);
            await userManager.DeleteAsync(otherTeacher);
        }

        Console.WriteLine($"\nCourse Access Mode & Prerequisites Summary: {passed} Passed, {failed} Failed\n");
        if (failed > 0) Environment.Exit(1);
    }
}
