using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class VideoWatchTrackingTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("🎬  AUB DIGITAL ACADEMY - TASK-P2-02 VIDEO WATCH TRACKING AUDIT");
        Console.WriteLine("=================================================================\n");

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
        var major = await db.Majors.FirstAsync();
        var sem = await db.Semesters.FirstAsync();

        var student = new User
        {
            UserName = $"vidstu_{suffix}@aub.edu",
            Email = $"vidstu_{suffix}@aub.edu",
            FullName = "Video Student",
            UniversityId = $"VSTU_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(student, "Student@123");
        await userManager.AddToRoleAsync(student, "Student");

        Course? course = null;
        try
        {
            course = new Course
            {
                Code = $"VID_{suffix}",
                Title = "Video Tracking Course",
                MajorId = major.Id,
                SemesterId = sem.Id,
                TeacherId = teacher.Id,
                Status = CourseStatus.Published
            };
            db.Courses.Add(course);
            await db.SaveChangesAsync();

            var ch = new Chapter { CourseId = course.Id, Title = "Ch 1", Position = 1 };
            db.Chapters.Add(ch);
            await db.SaveChangesAsync();

            var videoItem = new CourseItem { ChapterId = ch.Id, Title = "Video Lesson", Position = 1, Type = ItemType.Lesson };
            var textItem = new CourseItem { ChapterId = ch.Id, Title = "Text Lesson", Position = 2, Type = ItemType.Lesson };
            db.CourseItems.AddRange(videoItem, textItem);
            await db.SaveChangesAsync();

            db.Lessons.AddRange(
                new Lesson { CourseItemId = videoItem.Id, ContentHtml = "<p>Watch video</p>", VideoUrl = "/Student/Video/sample.mp4" },
                new Lesson { CourseItemId = textItem.Id, ContentHtml = "<p>Read notes</p>", VideoUrl = null }
            );
            await db.SaveChangesAsync();

            var enrollRes = await enrollmentService.EnrollStudentAsync(student.Id, course.Id);
            if (!enrollRes.IsSuccess) Fail($"Enrollment failed: {enrollRes.Message}");

            // 1. Premature CompleteLesson rejected for video lesson
            var premature = await curriculumService.MarkLessonCompleteAsync(student.Id, course.Id, videoItem.Id);
            if (!premature.IsSuccess) Pass("Test 01: Premature CompleteLesson blocked on unwatched video lesson");
            else Fail("Test 01: Premature CompleteLesson should have been blocked");

            // 2. Anti-seek jump [0, 95] on 100s video rejected (qualifying stays 0%)
            var seekJump = await curriculumService.RecordVideoHeartbeatAsync(student.Id, course.Id, videoItem.Id, 95, 100, 0, 95, true);
            if (seekJump.Success && seekJump.QualifyingWatchedSeconds == 0 && !seekJump.IsCompleted)
                Pass("Test 02: Anti-seek jump (95s span in single heartbeat) rejected; qualifying stays 0s");
            else
                Fail($"Test 02: Anti-seek failed (Success={seekJump.Success}, Err={seekJump.ErrorMessage}, Qualifying={seekJump.QualifyingWatchedSeconds})");

            // 3. Re-watching same [0, 10] interval twice does not double-count
            await curriculumService.RecordVideoHeartbeatAsync(student.Id, course.Id, videoItem.Id, 10, 100, 0, 10, true);
            var repeat = await curriculumService.RecordVideoHeartbeatAsync(student.Id, course.Id, videoItem.Id, 10, 100, 0, 10, true);
            if (repeat.QualifyingWatchedSeconds == 10 && repeat.WatchedPercentage == 10 && !repeat.IsCompleted)
                Pass("Test 03: Duplicate interval [0,10] merged without double counting (10s / 10%)");
            else
                Fail($"Test 03: Interval merge failed (Qualifying={repeat.QualifyingWatchedSeconds})");

            // 4. Resume position persisted in GetStudentLessonPlayerAsync
            var playerBefore = await curriculumService.GetStudentLessonPlayerAsync(course.Id, videoItem.Id, student.Id);
            if (playerBefore != null && playerBefore.LastVideoPositionSeconds == 10 && playerBefore.VideoQualifyingPercentage == 10)
                Pass("Test 04: Resume position (10s) and watch percentage (10%) restored in Lesson Player");
            else
                Fail($"Test 04: Resume position mismatch ({playerBefore?.LastVideoPositionSeconds}s / {playerBefore?.VideoQualifyingPercentage}%)");

            // 5. Continuous playback reaching >= 90% auto-completes VideoLessonProgress and CourseProgress
            VideoHeartbeatResult? finalHb = null;
            for (int start = 10; start < 90; start += 10)
            {
                finalHb = await curriculumService.RecordVideoHeartbeatAsync(student.Id, course.Id, videoItem.Id, start + 10, 100, start, start + 10, true);
            }
            bool cpExists = await db.CourseProgresses.AnyAsync(cp => cp.StudentId == student.Id && cp.CourseItemId == videoItem.Id && cp.IsCompleted);
            if (finalHb != null && finalHb.IsCompleted && finalHb.WatchedPercentage >= 90 && cpExists)
                Pass("Test 05: >=90% continuous playback marked VideoLessonProgress & CourseProgress complete");
            else
                Fail($"Test 05: 90% completion failed (IsCompleted={finalHb?.IsCompleted}, Pct={finalHb?.WatchedPercentage}, CP={cpExists})");

            // 6. Text-only lesson completes without video watch requirement
            var textComplete = await curriculumService.MarkLessonCompleteAsync(student.Id, course.Id, textItem.Id);
            if (textComplete.IsSuccess) Pass("Test 06: Text-only lesson completes without video requirement");
            else Fail($"Test 06: Text-only lesson completion failed: {textComplete.Message}");

            // 7. Student & Faculty Video Progress Dashboards + Scope Isolation (TASK-P2-04)
            var teacherService = sp.GetRequiredService<ITeacherService>();
            var ta = await userManager.FindByEmailAsync("ta@aub.com");
            var admin = await userManager.FindByEmailAsync("admin@aub.com");

            var stuVideoMap = await curriculumService.GetStudentCoursesVideoProgressAsync(student.Id, new[] { course.Id });
            var (vTotal, vDone, vLastAt) = stuVideoMap.GetValueOrDefault(course.Id, (0, 0, null));
            if (vTotal == 1 && vDone == 1 && vLastAt.HasValue)
                Pass("Test 07: Student Dashboard video progress counts Completed video lessons / Total video lessons (1/1)");
            else
                Fail($"Test 07: Student video progress mismatch ({vDone}/{vTotal}, LastAt={vLastAt})");

            var teacherDetails = await teacherService.GetTeacherCourseDetailsAsync(course.Id, teacher.Id);
            var rosterEntry = teacherDetails?.EnrolledStudents.FirstOrDefault(s => s.StudentId == student.Id);
            var teacherDrilldown = await teacherService.GetStudentCourseVideoProgressAsync(course.Id, student.Id, teacher.Id);
            var taUnassignedDrilldown = ta != null
                ? await teacherService.GetStudentCourseVideoProgressAsync(course.Id, student.Id, ta.Id)
                : null;
            var otherStudentDrilldown = await teacherService.GetStudentCourseVideoProgressAsync(course.Id, student.Id, student.Id);
            var adminDrilldown = admin != null
                ? await teacherService.GetStudentCourseVideoProgressAsync(course.Id, student.Id, admin.Id, isAdmin: true)
                : null;

            if (rosterEntry != null && rosterEntry.CompletedVideoLessons == 1 && rosterEntry.TotalVideoLessons == 1 &&
                rosterEntry.VideoProgressPercentage == 100 && rosterEntry.LastActivityAt.HasValue &&
                teacherDrilldown != null && teacherDrilldown.Count == 1 && teacherDrilldown[0].IsCompleted &&
                taUnassignedDrilldown == null && otherStudentDrilldown == null && adminDrilldown != null)
            {
                Pass("Test 08: Faculty Roster & Lesson Drilldown accurate and strictly scoped (Teacher/Admin allowed, unassigned TA/other user blocked)");
            }
            else
            {
                Fail("Test 08: Faculty Roster or Drilldown scope isolation failed");
            }
        }
        finally
        {
            if (course != null)
            {
                db.VideoLessonProgresses.RemoveRange(db.VideoLessonProgresses.Where(v => v.StudentId == student.Id));
                db.CourseProgresses.RemoveRange(db.CourseProgresses.Where(cp => cp.StudentId == student.Id));
                db.Lessons.RemoveRange(db.Lessons.Where(l => l.CourseItem.Chapter.CourseId == course.Id));
                db.CourseItems.RemoveRange(db.CourseItems.Where(ci => ci.Chapter.CourseId == course.Id));
                db.Chapters.RemoveRange(db.Chapters.Where(ch => ch.CourseId == course.Id));
                db.Enrollments.RemoveRange(db.Enrollments.Where(e => e.CourseId == course.Id));
                db.Courses.Remove(course);
                await db.SaveChangesAsync();
            }
            await userManager.DeleteAsync(student);
        }

        Console.WriteLine($"\nVideo Watch Tracking Summary: {passed} Passed, {failed} Failed\n");
        if (failed > 0) Environment.Exit(1);
    }
}
