using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class NotificationAnnouncementIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("============================================================================");
        Console.WriteLine("📢 AUB DIGITAL ACADEMY - TASK-P2-06 NOTIFICATIONS & ANNOUNCEMENTS TEST");
        Console.WriteLine("============================================================================\n");

        int passed = 0;
        int failed = 0;

        void Pass(string msg) { passed++; Console.WriteLine($"  [PASS] {msg}"); }
        void Fail(string msg) { failed++; Console.WriteLine($"  [FAIL] {msg}"); }

        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var notificationService = sp.GetRequiredService<INotificationService>();
        var curriculumService = sp.GetRequiredService<ICurriculumService>();
        var teacherService = sp.GetRequiredService<ITeacherService>();
        var discussionService = sp.GetRequiredService<IDiscussionService>();
        var enrollmentService = sp.GetRequiredService<IEnrollmentService>();
        var userManager = sp.GetRequiredService<UserManager<User>>();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var major = await db.Majors.FirstAsync();
        var sem = await db.Semesters.FirstAsync();

        // 1. Setup Users
        var admin = await userManager.FindByEmailAsync("admin@aub.com") ?? throw new InvalidOperationException("Admin not found");

        var teacher1 = new User
        {
            UserName = $"notif_t1_{suffix}@aub.edu",
            Email = $"notif_t1_{suffix}@aub.edu",
            FullName = "Teacher One",
            UniversityId = $"T1_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(teacher1, "Teacher@123");
        await userManager.AddToRoleAsync(teacher1, "Teacher");

        var teacher2 = new User
        {
            UserName = $"notif_t2_{suffix}@aub.edu",
            Email = $"notif_t2_{suffix}@aub.edu",
            FullName = "Teacher Two",
            UniversityId = $"T2_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(teacher2, "Teacher@123");
        await userManager.AddToRoleAsync(teacher2, "Teacher");

        var ta = new User
        {
            UserName = $"notif_ta_{suffix}@aub.edu",
            Email = $"notif_ta_{suffix}@aub.edu",
            FullName = "Course TA",
            UniversityId = $"TA_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(ta, "Assistant@123");
        await userManager.AddToRoleAsync(ta, "Assistance");

        var studentA = new User
        {
            UserName = $"notif_stuA_{suffix}@aub.edu",
            Email = $"notif_stuA_{suffix}@aub.edu",
            FullName = "Student Alpha",
            UniversityId = $"SA_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(studentA, "Student@123");
        await userManager.AddToRoleAsync(studentA, "Student");

        var studentB = new User
        {
            UserName = $"notif_stuB_{suffix}@aub.edu",
            Email = $"notif_stuB_{suffix}@aub.edu",
            FullName = "Student Beta",
            UniversityId = $"SB_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(studentB, "Student@123");
        await userManager.AddToRoleAsync(studentB, "Student");

        // 2. Setup Courses
        var courseA = new Course
        {
            Code = $"NTFA_{suffix}",
            Title = $"Notification Course Alpha {suffix}",
            MajorId = major.Id,
            SemesterId = sem.Id,
            TeacherId = teacher1.Id,
            Status = CourseStatus.Published
        };
        db.Courses.Add(courseA);

        var courseB = new Course
        {
            Code = $"NTFB_{suffix}",
            Title = $"Notification Course Beta {suffix}",
            MajorId = major.Id,
            SemesterId = sem.Id,
            TeacherId = teacher2.Id,
            Status = CourseStatus.Published
        };
        db.Courses.Add(courseB);
        await db.SaveChangesAsync();

        // Assign TA to Course A
        db.CourseAssistants.Add(new CourseAssistant
        {
            CourseId = courseA.Id,
            AssistantId = ta.Id,
            CanGrade = true,
            AssignedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Enroll Student A in Course A, Student B in Course B
        await enrollmentService.EnrollStudentAsync(studentA.Id, courseA.Id);
        await enrollmentService.EnrollStudentAsync(studentB.Id, courseB.Id);

        // Add Chapter & Lesson in Course A
        var chapterA = new Chapter { CourseId = courseA.Id, Title = "Module 1", Position = 1 };
        db.Chapters.Add(chapterA);
        await db.SaveChangesAsync();

        var itemA1 = new CourseItem { ChapterId = chapterA.Id, Title = "Lesson 1: Intro", Position = 1, Type = ItemType.Lesson };
        db.CourseItems.Add(itemA1);
        await db.SaveChangesAsync();

        var lessonA1 = new Lesson { CourseItemId = itemA1.Id, VideoUrl = "https://example.com/v1.mp4", ContentHtml = "<p>Intro</p>" };
        db.Lessons.Add(lessonA1);
        await db.SaveChangesAsync();

        Console.WriteLine($"Initialized Notification Test Environment: Course A ({courseA.Id}), Course B ({courseB.Id})\n");

        // --- TEST 1: Admin can publish a Global announcement ---
        var t1 = await notificationService.CreateAnnouncementAsync(
            admin.Id, "Platform Maintenance Notice", "The platform will undergo scheduled maintenance this Sunday.", AnnouncementScope.Global);
        if (t1.Success && t1.AnnouncementId.HasValue)
        {
            Pass($"Test 01: Admin published Global announcement (ID: {t1.AnnouncementId.Value}).");
        }
        else
        {
            Fail($"Test 01: Admin failed to publish Global announcement: {t1.Message}");
        }

        // --- TEST 2: Admin can publish a Course announcement ---
        var t2 = await notificationService.CreateAnnouncementAsync(
            admin.Id, "Admin Course Announcement", "Message from Admin to Course A.", AnnouncementScope.Course, courseA.Id);
        if (t2.Success && t2.AnnouncementId.HasValue)
        {
            Pass("Test 02: Admin published Course announcement for Course A.");
        }
        else
        {
            Fail($"Test 02: Admin failed to publish Course announcement: {t2.Message}");
        }

        // --- TEST 3: Teacher can publish a Course announcement for owned course ---
        var t3 = await notificationService.CreateAnnouncementAsync(
            teacher1.Id, "Welcome to Course A", "Welcome students to our course! Please read the syllabus.", AnnouncementScope.Course, courseA.Id, isPinned: true);
        if (t3.Success && t3.AnnouncementId.HasValue)
        {
            Pass("Test 03: Teacher 1 published Course announcement for owned Course A.");
        }
        else
        {
            Fail($"Test 03: Teacher 1 failed to publish Course announcement: {t3.Message}");
        }
        int annAId = t3.AnnouncementId ?? 0;

        // --- TEST 4: Teacher CANNOT publish a Global announcement ---
        var t4 = await notificationService.CreateAnnouncementAsync(
            teacher1.Id, "Unauthorized Global Notice", "Teacher trying to post global.", AnnouncementScope.Global);
        if (!t4.Success)
        {
            Pass("Test 04: Teacher was rejected from publishing Global announcement.");
        }
        else
        {
            Fail("Test 04: Teacher unexpectedly succeeded in publishing Global announcement.");
        }

        // --- TEST 5: Teacher CANNOT publish Course announcement for another teacher's course ---
        var t5 = await notificationService.CreateAnnouncementAsync(
            teacher1.Id, "Hacked Course B", "Teacher 1 trying to post to Teacher 2's course.", AnnouncementScope.Course, courseB.Id);
        if (!t5.Success)
        {
            Pass("Test 05: Teacher 1 was rejected from publishing announcement to Course B (Teacher 2's course).");
        }
        else
        {
            Fail("Test 05: Teacher 1 unexpectedly posted announcement to Course B.");
        }

        // --- TEST 6: Teaching Assistant CANNOT publish an announcement ---
        var t6 = await notificationService.CreateAnnouncementAsync(
            ta.Id, "TA Announcement", "TA trying to publish announcement.", AnnouncementScope.Course, courseA.Id);
        if (!t6.Success)
        {
            Pass("Test 06: Teaching Assistant was rejected from publishing announcement.");
        }
        else
        {
            Fail("Test 06: TA unexpectedly succeeded in publishing announcement.");
        }

        // --- TEST 7: Student CANNOT publish an announcement ---
        var t7 = await notificationService.CreateAnnouncementAsync(
            studentA.Id, "Student Announcement", "Student trying to publish.", AnnouncementScope.Course, courseA.Id);
        if (!t7.Success)
        {
            Pass("Test 07: Student was rejected from publishing announcement.");
        }
        else
        {
            Fail("Test 07: Student unexpectedly succeeded in publishing announcement.");
        }

        // --- TEST 8: Global announcement is visible to all users ---
        var annsStudentA = await notificationService.GetAnnouncementsForUserAsync(studentA.Id);
        var annsStudentB = await notificationService.GetAnnouncementsForUserAsync(studentB.Id);
        bool studentAHasGlobal = annsStudentA.Any(a => a.Scope == AnnouncementScope.Global && a.Title == "Platform Maintenance Notice");
        bool studentBHasGlobal = annsStudentB.Any(a => a.Scope == AnnouncementScope.Global && a.Title == "Platform Maintenance Notice");

        if (studentAHasGlobal && studentBHasGlobal)
        {
            Pass("Test 08: Global announcement is visible to all active students across different courses.");
        }
        else
        {
            Fail("Test 08: Global announcement was not visible to all students.");
        }

        // --- TEST 9: Course announcement is visible only to users in that course ---
        bool studentAHasCourseA = annsStudentA.Any(a => a.CourseId == courseA.Id && a.Title == "Welcome to Course A");
        bool studentBHasCourseA = annsStudentB.Any(a => a.CourseId == courseA.Id && a.Title == "Welcome to Course A");

        if (studentAHasCourseA && !studentBHasCourseA)
        {
            Pass("Test 09: Course A announcement is visible to Student A and strictly isolated from Student B.");
        }
        else
        {
            Fail("Test 09: Course announcement isolation failed (Student B saw Course A announcement, or Student A did not).");
        }

        // --- TEST 10: Course announcement automatic notification dispatch ---
        var notifsStudentA = await notificationService.GetUserNotificationsAsync(studentA.Id);
        var courseAnnNotif = notifsStudentA.Notifications.FirstOrDefault(n => n.Type == NotificationType.Announcement && n.Title.Contains("Welcome to Course A"));
        if (courseAnnNotif != null)
        {
            Pass("Test 10: Course announcement automatically created in-app Notification for enrolled Student A.");
        }
        else
        {
            Fail("Test 10: Enrolled student did not receive Notification for new course announcement.");
        }

        // --- TEST 11: Discussion Reply automatic notification dispatch ---
        var threadRes = await discussionService.CreateThreadAsync(itemA1.Id, studentA.Id, "Help on Video 1", "How do I run the code?");
        int threadId = threadRes.ThreadId!.Value;

        // TA replies to Student A's question
        var replyRes = await discussionService.CreateReplyAsync(threadId, ta.Id, "Here is how you compile and run the code.");

        // Student A self-replies (should NOT generate another notification to self)
        await discussionService.CreateReplyAsync(threadId, studentA.Id, "Thanks, I got it running!");

        var studentANotifsAfterReply = await notificationService.GetUserNotificationsAsync(studentA.Id);
        var replyNotif = studentANotifsAfterReply.Notifications.FirstOrDefault(n => n.Type == NotificationType.DiscussionReply && n.Title.Contains("Help on Video 1"));
        var taNotifs = await notificationService.GetUserNotificationsAsync(ta.Id);
        var selfReplyNotif = taNotifs.Notifications.FirstOrDefault(n => n.Type == NotificationType.DiscussionReply);
        var replyNotifCount = studentANotifsAfterReply.Notifications.Count(n => n.Type == NotificationType.DiscussionReply);

        if (replyRes.Success && replyNotif != null && selfReplyNotif == null && replyNotifCount == 1)
        {
            Pass("Test 11: Discussion reply triggered notification to thread author (Student A) and not to the replier (TA), with self-replies excluded.");
        }
        else
        {
            Fail($"Test 11: Discussion reply notification trigger failed. ReplySuccess: {replyRes.Success}, NotifFound: {replyNotif != null}, SelfReplyToReplier: {selfReplyNotif != null}, AuthorNotifCount: {replyNotifCount}");
        }

        // --- TEST 12: New Lesson creation automatic notification dispatch ---
        var lessCreateRes = await curriculumService.CreateLessonAsync(chapterA.Id, "Lesson 2: Advanced Topics", "<p>Adv</p>", "https://example.com/v2.mp4");
        var studentANotifsAfterLesson = await notificationService.GetUserNotificationsAsync(studentA.Id);
        var newLessonNotif = studentANotifsAfterLesson.Notifications.FirstOrDefault(n => n.Type == NotificationType.NewLesson && n.Title.Contains("Lesson 2: Advanced Topics"));

        if (lessCreateRes.IsSuccess && newLessonNotif != null)
        {
            Pass("Test 12: Publishing new lesson automatically dispatched NewLesson notification to enrolled student.");
        }
        else
        {
            Fail("Test 12: New lesson creation failed to dispatch notification to enrolled student.");
        }

        // --- TEST 13: Assignment grading automatic notification dispatch ---
        var assignItem = new CourseItem { ChapterId = chapterA.Id, Title = "Assignment 1", Position = 3, Type = ItemType.Assignment };
        db.CourseItems.Add(assignItem);
        await db.SaveChangesAsync();

        var assignment = new Assignment { CourseItemId = assignItem.Id, Instruction = "Do task", MaxScore = 100, DueDate = DateTime.UtcNow.AddDays(7) };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var submission = new AssignmentSubmission
        {
            AssignmentId = assignment.Id,
            StudentId = studentA.Id,
            SubmittedAt = DateTime.UtcNow,
            Status = SubmissionStatus.Submitted,
            IsLatest = true
        };
        db.AssignmentSubmissions.Add(submission);
        await db.SaveChangesAsync();

        await teacherService.GradeSubmissionAsync(submission.Id, 95.0, "Outstanding work!", teacher1.Id);

        var studentANotifsAfterGrading = await notificationService.GetUserNotificationsAsync(studentA.Id);
        var gradeNotif = studentANotifsAfterGrading.Notifications.FirstOrDefault(n => n.Type == NotificationType.GradeReleased && n.Title.Contains("Assignment 1"));

        if (gradeNotif != null && gradeNotif.Message.Contains("95/100"))
        {
            Pass("Test 13: Grading assignment submission automatically dispatched GradeReleased notification with score.");
        }
        else
        {
            Fail("Test 13: Assignment grading failed to dispatch GradeReleased notification.");
        }

        // --- TEST 14: Quiz submission automatic notification dispatch ---
        var quizItem = new CourseItem { ChapterId = chapterA.Id, Title = "Quiz 1", Position = 4, Type = ItemType.Quiz };
        db.CourseItems.Add(quizItem);
        await db.SaveChangesAsync();

        var quiz = new Quiz { CourseItemId = quizItem.Id, MaxAttempts = 3, IsPublished = true };
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync();

        var q1 = new Question { QuizId = quiz.Id, Text = "What is 2+2?", Points = 10, Order = 1, Type = QuestionType.MultipleChoice };
        db.Questions.Add(q1);
        await db.SaveChangesAsync();

        var opt1 = new QuestionOption { QuestionId = q1.Id, Text = "4", IsCorrect = true };
        db.QuestionOptions.Add(opt1);
        await db.SaveChangesAsync();

        await curriculumService.SubmitQuizAsync(studentA.Id, courseA.Id, quizItem.Id, new Dictionary<int, int> { { q1.Id, opt1.Id } });

        var studentANotifsAfterQuiz = await notificationService.GetUserNotificationsAsync(studentA.Id);
        var quizNotif = studentANotifsAfterQuiz.Notifications.FirstOrDefault(n => n.Type == NotificationType.GradeReleased && n.Title.Contains("Quiz 1"));

        if (quizNotif != null && quizNotif.Message.Contains("100%"))
        {
            Pass("Test 14: Submitting quiz automatically dispatched GradeReleased notification with score and status.");
        }
        else
        {
            Fail("Test 14: Quiz submission failed to dispatch GradeReleased notification.");
        }

        // --- TEST 15: Course Milestone (100% completion) notification ---
        // Complete all video lessons in Course A for Student A
        await db.VideoLessonProgresses.AddAsync(new VideoLessonProgress
        {
            StudentId = studentA.Id,
            CourseItemId = itemA1.Id,
            VideoDurationSeconds = 100,
            QualifyingWatchedSeconds = 95,
            IsCompleted = true
        });
        await db.VideoLessonProgresses.AddAsync(new VideoLessonProgress
        {
            StudentId = studentA.Id,
            CourseItemId = lessCreateRes.EntityId!.Value,
            VideoDurationSeconds = 100,
            QualifyingWatchedSeconds = 95,
            IsCompleted = true
        });
        await db.SaveChangesAsync();

        await curriculumService.MarkLessonCompleteAsync(studentA.Id, courseA.Id, itemA1.Id);
        await curriculumService.MarkLessonCompleteAsync(studentA.Id, courseA.Id, lessCreateRes.EntityId!.Value);

        var studentANotifsAfterMilestone = await notificationService.GetUserNotificationsAsync(studentA.Id);
        var milestoneNotifs = studentANotifsAfterMilestone.Notifications.Where(n => n.Type == NotificationType.CourseMilestone).ToList();

        if (milestoneNotifs.Count == 1 && milestoneNotifs[0].Title.Contains("100% Completed"))
        {
            Pass("Test 15: Completing 100% of video lessons triggered CourseMilestone notification.");
        }
        else
        {
            Fail($"Test 15: CourseMilestone notification count expected 1, found {milestoneNotifs.Count}.");
        }

        // Repeat completion to test idempotency
        await curriculumService.MarkLessonCompleteAsync(studentA.Id, courseA.Id, lessCreateRes.EntityId!.Value);
        var studentANotifsAfterRepeat = await notificationService.GetUserNotificationsAsync(studentA.Id);
        var milestoneCountAfterRepeat = studentANotifsAfterRepeat.Notifications.Count(n => n.Type == NotificationType.CourseMilestone);

        if (milestoneCountAfterRepeat == 1)
        {
            Pass("Test 16: CourseMilestone notification is idempotent and not sent repeatedly.");
        }
        else
        {
            Fail("Test 16: Milestone notification sent multiple times.");
        }

        // --- TEST 17: GetUnreadCountAsync accurately reflects unread notifications ---
        var unreadCountBefore = await notificationService.GetUnreadCountAsync(studentA.Id);
        if (unreadCountBefore > 0)
        {
            Pass($"Test 17: GetUnreadCountAsync returned accurate count ({unreadCountBefore} unread).");
        }
        else
        {
            Fail("Test 17: GetUnreadCountAsync returned 0 for user with unread notifications.");
        }

        // --- TEST 18: MarkAsReadAsync marks specific notification as read ---
        var targetNotif = studentANotifsAfterMilestone.Notifications.First(n => !n.IsRead);
        var markRes = await notificationService.MarkAsReadAsync(targetNotif.Id, studentA.Id);
        var notifDb = await db.Notifications.FindAsync(targetNotif.Id);
        var unreadCountAfterSingle = await notificationService.GetUnreadCountAsync(studentA.Id);

        if (markRes && notifDb?.IsRead == true && notifDb.ReadAt != null && unreadCountAfterSingle == unreadCountBefore - 1)
        {
            Pass("Test 18: MarkAsReadAsync successfully marked notification as read and updated unread count.");
        }
        else
        {
            Fail("Test 18: MarkAsReadAsync failed to update notification read state.");
        }

        // --- TEST 19: User A CANNOT mark User B's notification as read ---
        var crossUserMarkRes = await notificationService.MarkAsReadAsync(targetNotif.Id, studentB.Id);
        if (!crossUserMarkRes)
        {
            Pass("Test 19: User B was rejected from marking User A's notification as read (isolation enforced).");
        }
        else
        {
            Fail("Test 19: Cross-user notification state modification was unexpectedly allowed.");
        }

        // --- TEST 20: MarkAllAsReadAsync marks all unread notifications for a user as read ---
        var markAllCount = await notificationService.MarkAllAsReadAsync(studentA.Id);
        var unreadCountFinal = await notificationService.GetUnreadCountAsync(studentA.Id);

        if (markAllCount > 0 && unreadCountFinal == 0)
        {
            Pass($"Test 20: MarkAllAsReadAsync marked {markAllCount} notifications as read (final unread: 0).");
        }
        else
        {
            Fail($"Test 20: MarkAllAsReadAsync failed; remaining unread count: {unreadCountFinal}.");
        }

        // --- TEST 21: Announcement deletion authorization (Teacher Owner / Admin) ---
        var delAnnRes = await notificationService.DeleteAnnouncementAsync(annAId, teacher1.Id);
        var annDb = await db.Announcements.FindAsync(annAId);

        if (delAnnRes.Success && annDb?.IsArchived == true)
        {
            Pass("Test 21: Course Teacher author successfully deleted/archived their course announcement.");
        }
        else
        {
            Fail("Test 21: Course Teacher failed to delete announcement.");
        }

        // --- TEST 22: Unauthorized user cannot delete announcement ---
        var annGlobal = await db.Announcements.FirstAsync(a => a.Scope == AnnouncementScope.Global);
        var unauthDelRes = await notificationService.DeleteAnnouncementAsync(annGlobal.Id, studentA.Id);

        if (!unauthDelRes.Success)
        {
            Pass("Test 22: Student was rejected from deleting Admin's global announcement.");
        }
        else
        {
            Fail("Test 22: Student unexpectedly deleted Admin announcement.");
        }

        Console.WriteLine("\n============================================================================");
        Console.WriteLine($"RESULTS: {passed} Passed, {failed} Failed.");
        Console.WriteLine("============================================================================\n");

        if (failed > 0)
        {
            throw new Exception($"Notification & Announcement test suite failed with {failed} failures.");
        }
    }
}
