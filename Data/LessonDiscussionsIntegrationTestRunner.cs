using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class LessonDiscussionsIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("============================================================================");
        Console.WriteLine("💬 AUB DIGITAL ACADEMY - TASK-P2-05 LESSON DISCUSSIONS & MODERATION TEST");
        Console.WriteLine("============================================================================\n");

        int passed = 0;
        int failed = 0;

        void Pass(string msg) { passed++; Console.WriteLine($"  [PASS] {msg}"); }
        void Fail(string msg) { failed++; Console.WriteLine($"  [FAIL] {msg}"); }

        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var discussionService = sp.GetRequiredService<IDiscussionService>();
        var enrollmentService = sp.GetRequiredService<IEnrollmentService>();
        var userManager = sp.GetRequiredService<UserManager<User>>();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var major = await db.Majors.FirstAsync();
        var sem = await db.Semesters.FirstAsync();
        var admin = await userManager.FindByEmailAsync("admin@aub.com") ?? throw new InvalidOperationException("Admin not found");

        // 1. Setup Test Users
        var teacherOwner = new User
        {
            UserName = $"disc_teacher_{suffix}@aub.edu",
            Email = $"disc_teacher_{suffix}@aub.edu",
            FullName = "Discussion Teacher",
            UniversityId = $"TCHR_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(teacherOwner, "Teacher@123");
        await userManager.AddToRoleAsync(teacherOwner, "Teacher");

        var assignedTa = new User
        {
            UserName = $"disc_ta_{suffix}@aub.edu",
            Email = $"disc_ta_{suffix}@aub.edu",
            FullName = "Assigned TA",
            UniversityId = $"TA_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(assignedTa, "Assistant@123");
        await userManager.AddToRoleAsync(assignedTa, "Assistance");

        var unassignedTa = new User
        {
            UserName = $"disc_unassigned_ta_{suffix}@aub.edu",
            Email = $"disc_unassigned_ta_{suffix}@aub.edu",
            FullName = "Unassigned TA",
            UniversityId = $"UTA_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(unassignedTa, "Assistant@123");
        await userManager.AddToRoleAsync(unassignedTa, "Assistance");

        var studentA = new User
        {
            UserName = $"disc_student_a_{suffix}@aub.edu",
            Email = $"disc_student_a_{suffix}@aub.edu",
            FullName = "Student Alpha",
            UniversityId = $"STUA_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(studentA, "Student@123");
        await userManager.AddToRoleAsync(studentA, "Student");

        var studentB = new User
        {
            UserName = $"disc_student_b_{suffix}@aub.edu",
            Email = $"disc_student_b_{suffix}@aub.edu",
            FullName = "Student Beta",
            UniversityId = $"STUB_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(studentB, "Student@123");
        await userManager.AddToRoleAsync(studentB, "Student");

        var nonEnrolledStudent = new User
        {
            UserName = $"disc_nonenrolled_{suffix}@aub.edu",
            Email = $"disc_nonenrolled_{suffix}@aub.edu",
            FullName = "Non Enrolled Student",
            UniversityId = $"NOENR_{suffix}",
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(nonEnrolledStudent, "Student@123");
        await userManager.AddToRoleAsync(nonEnrolledStudent, "Student");

        // 2. Setup Course & Lesson Structure
        var course = new Course
        {
            Code = $"DISC{suffix}",
            Title = $"Discussions Test Course {suffix}",
            MajorId = major.Id,
            SemesterId = sem.Id,
            TeacherId = teacherOwner.Id,
            Status = CourseStatus.Published,
            AccessMode = CourseAccessMode.Free
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        // Assign TA
        var courseAssistant = new CourseAssistant
        {
            CourseId = course.Id,
            AssistantId = assignedTa.Id,
            CanGrade = false,
            AssignedAt = DateTime.UtcNow
        };
        db.CourseAssistants.Add(courseAssistant);
        await db.SaveChangesAsync();

        // Enroll Student A and Student B
        await enrollmentService.EnrollStudentAsync(studentA.Id, course.Id);
        await enrollmentService.EnrollStudentAsync(studentB.Id, course.Id);

        var chapter = new Chapter
        {
            CourseId = course.Id,
            Title = "Chapter 1: Foundations",
            Position = 1
        };
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();

        var item = new CourseItem
        {
            ChapterId = chapter.Id,
            Title = "Video Lesson 1: Introduction",
            Position = 1,
            Type = ItemType.Lesson
        };
        db.CourseItems.Add(item);
        await db.SaveChangesAsync();

        var lesson = new Lesson
        {
            CourseItemId = item.Id,
            VideoUrl = "https://example.com/video1.mp4",
            ContentHtml = "<p>Introduction lesson content</p>"
        };
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();

        Console.WriteLine($"Initialized Test Environment: Course ID {course.Id}, CourseItem ID {item.Id}\n");

        // --- TEST 1: Enrolled student can create a question thread ---
        var t1Result = await discussionService.CreateThreadAsync(item.Id, studentA.Id, "How does recursion terminate?", "I understand recursive calls but need clarification on base cases.");
        if (t1Result.Success && t1Result.ThreadId.HasValue)
        {
            Pass($"Test 1: Enrolled Student A created question thread (ID: {t1Result.ThreadId.Value}).");
        }
        else
        {
            Fail($"Test 1: Failed to create question thread: {t1Result.Message}");
        }
        int thread1Id = t1Result.ThreadId ?? 0;

        // --- TEST 2: Non-enrolled student cannot create a question thread ---
        var t2Result = await discussionService.CreateThreadAsync(item.Id, nonEnrolledStudent.Id, "Unauthorized Question", "Should be rejected.");
        if (!t2Result.Success)
        {
            Pass("Test 2: Non-enrolled student was rejected from creating a thread.");
        }
        else
        {
            Fail("Test 2: Non-enrolled student unexpectedly succeeded in creating a thread.");
        }

        // --- TEST 3: XSS sanitization in question title and content ---
        var xssPayload = "<script>alert('XSS')</script><b>Recursion Question</b><img src=x onerror=alert(1)>";
        var t3Result = await discussionService.CreateThreadAsync(item.Id, studentA.Id, "XSS Test <script>alert(1)</script>", xssPayload);
        var xssThread = await db.LessonDiscussionThreads.FindAsync(t3Result.ThreadId);
        if (xssThread != null && !xssThread.Content.Contains("<script>") && !xssThread.Content.Contains("onerror") && !xssThread.Title.Contains("<script>"))
        {
            Pass("Test 3: Thread title and content were sanitized to prevent Stored XSS.");
        }
        else
        {
            Fail("Test 3: Stored XSS payload was not properly sanitized.");
        }

        // --- TEST 4: Open Peer Discussion - Student B can reply to Student A's thread ---
        var t4Result = await discussionService.CreateReplyAsync(thread1Id, studentB.Id, "You need a base case check like if (n <= 1) return 1;");
        if (t4Result.Success && t4Result.ReplyId.HasValue)
        {
            var replyB = await db.LessonDiscussionReplies.FindAsync(t4Result.ReplyId.Value);
            if (replyB != null && !replyB.IsInstructorAnswer)
            {
                Pass($"Test 4: Open Peer Discussion allowed enrolled Student B to reply to Student A's thread (Reply ID: {replyB.Id}).");
            }
            else
            {
                Fail("Test 4: Reply created but IsInstructorAnswer was unexpectedly true for student.");
            }
        }
        else
        {
            Fail($"Test 4: Enrolled student B failed to reply to peer's thread: {t4Result.Message}");
        }
        int replyBId = t4Result.ReplyId ?? 0;

        // --- TEST 5: Non-enrolled student cannot reply ---
        var t5Result = await discussionService.CreateReplyAsync(thread1Id, nonEnrolledStudent.Id, "Unauthorized reply");
        if (!t5Result.Success)
        {
            Pass("Test 5: Non-enrolled student was rejected from replying.");
        }
        else
        {
            Fail("Test 5: Non-enrolled student unexpectedly succeeded in replying.");
        }

        // --- TEST 6: Teacher of the course can reply with IsInstructorAnswer = true ---
        var t6Result = await discussionService.CreateReplyAsync(thread1Id, teacherOwner.Id, "Great question! As Student B noted, always ensure the problem size decreases toward your base case.");
        if (t6Result.Success && t6Result.ReplyId.HasValue)
        {
            var teacherReply = await db.LessonDiscussionReplies.FindAsync(t6Result.ReplyId.Value);
            if (teacherReply != null && teacherReply.IsInstructorAnswer)
            {
                Pass($"Test 6: Course Teacher replied, and IsInstructorAnswer was properly set to true (Reply ID: {teacherReply.Id}).");
            }
            else
            {
                Fail("Test 6: Teacher reply did not set IsInstructorAnswer = true.");
            }
        }
        else
        {
            Fail($"Test 6: Course Teacher failed to reply: {t6Result.Message}");
        }
        int teacherReplyId = t6Result.ReplyId ?? 0;

        // --- TEST 7: Assigned TA can reply with IsInstructorAnswer = true ---
        var t7Result = await discussionService.CreateReplyAsync(thread1Id, assignedTa.Id, "I will also cover this in Friday's lab session.");
        if (t7Result.Success && t7Result.ReplyId.HasValue)
        {
            var taReply = await db.LessonDiscussionReplies.FindAsync(t7Result.ReplyId.Value);
            if (taReply != null && taReply.IsInstructorAnswer)
            {
                Pass($"Test 7: Assigned TA replied, and IsInstructorAnswer was properly set to true (Reply ID: {taReply.Id}).");
            }
            else
            {
                Fail("Test 7: Assigned TA reply did not set IsInstructorAnswer = true.");
            }
        }
        else
        {
            Fail($"Test 7: Assigned TA failed to reply: {t7Result.Message}");
        }

        // --- TEST 8: Unassigned TA is rejected from faculty actions ---
        var canUnassignedModerate = await discussionService.CanUserModerateAsync(item.Id, unassignedTa.Id);
        if (!canUnassignedModerate)
        {
            Pass("Test 8: Unassigned TA cannot moderate discussions in an unassigned course.");
        }
        else
        {
            Fail("Test 8: Unassigned TA was unexpectedly granted moderation access.");
        }

        // --- TEST 9: Teacher can pin and unpin a question thread ---
        var pinResult = await discussionService.TogglePinThreadAsync(thread1Id, teacherOwner.Id);
        var threadAfterPin = await db.LessonDiscussionThreads.FindAsync(thread1Id);
        bool pinnedFirst = pinResult.Success && threadAfterPin?.IsPinned == true;

        var unpinResult = await discussionService.TogglePinThreadAsync(thread1Id, teacherOwner.Id);
        bool unpinnedSecond = unpinResult.Success && threadAfterPin?.IsPinned == false;

        if (pinnedFirst && unpinnedSecond)
        {
            Pass("Test 9: Teacher successfully pinned and unpinned the question thread.");
        }
        else
        {
            Fail("Test 9: Teacher failed to toggle pin on question thread.");
        }

        // Pin it again for ordering tests
        await discussionService.TogglePinThreadAsync(thread1Id, teacherOwner.Id);

        // --- TEST 10: Assigned TA can pin question thread ---
        var taPinResult = await discussionService.TogglePinThreadAsync(thread1Id, assignedTa.Id);
        if (taPinResult.Success)
        {
            Pass("Test 10: Assigned TA successfully toggled pin on question thread.");
        }
        else
        {
            Fail($"Test 10: Assigned TA failed to pin thread: {taPinResult.Message}");
        }
        // Ensure thread1 is pinned
        await db.LessonDiscussionThreads.Where(t => t.Id == thread1Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsPinned, true));

        // --- TEST 11: Regular student cannot pin a thread ---
        var stuPinResult = await discussionService.TogglePinThreadAsync(thread1Id, studentA.Id);
        if (!stuPinResult.Success)
        {
            Pass("Test 11: Regular student was rejected from pinning a thread.");
        }
        else
        {
            Fail("Test 11: Regular student unexpectedly succeeded in pinning a thread.");
        }

        // --- TEST 12: Question author can mark their own question as answered ---
        var ansResult = await discussionService.ToggleAnsweredThreadAsync(thread1Id, studentA.Id);
        var threadAns = await db.LessonDiscussionThreads.FindAsync(thread1Id);
        if (ansResult.Success && threadAns?.IsAnswered == true)
        {
            Pass("Test 12: Question author successfully marked their question as answered.");
        }
        else
        {
            Fail("Test 12: Question author failed to mark their question as answered.");
        }

        // --- TEST 13: Course Teacher can mark question as answered/unanswered ---
        var tchrAnsResult = await discussionService.ToggleAnsweredThreadAsync(thread1Id, teacherOwner.Id);
        var threadUnans = await db.LessonDiscussionThreads.FindAsync(thread1Id);
        if (tchrAnsResult.Success && threadUnans?.IsAnswered == false)
        {
            Pass("Test 13: Course Teacher successfully unmarked question as answered.");
        }
        else
        {
            Fail("Test 13: Course Teacher failed to toggle answered status.");
        }

        // --- TEST 14: Non-author student cannot mark question as answered ---
        var nonAuthorAns = await discussionService.ToggleAnsweredThreadAsync(thread1Id, studentB.Id);
        if (!nonAuthorAns.Success)
        {
            Pass("Test 14: Non-author student was rejected from marking question as answered.");
        }
        else
        {
            Fail("Test 14: Non-author student unexpectedly succeeded in marking question answered.");
        }

        // --- TEST 15: Course Teacher / Assigned TA can pin a helpful reply ---
        var pinReplyResult = await discussionService.TogglePinReplyAsync(teacherReplyId, teacherOwner.Id);
        var replyAfterPin = await db.LessonDiscussionReplies.FindAsync(teacherReplyId);
        if (pinReplyResult.Success && replyAfterPin?.IsPinned == true)
        {
            Pass("Test 15: Course Teacher pinned teacher's answer as helpful answer.");
        }
        else
        {
            Fail("Test 15: Course Teacher failed to pin helpful answer.");
        }

        // --- TEST 16: Regular student cannot pin a reply ---
        var stuPinReply = await discussionService.TogglePinReplyAsync(replyBId, studentA.Id);
        if (!stuPinReply.Success)
        {
            Pass("Test 16: Regular student was rejected from pinning an answer.");
        }
        else
        {
            Fail("Test 16: Regular student unexpectedly succeeded in pinning an answer.");
        }

        // --- TEST 17: Author can delete their own reply ---
        var delReplyAuthorResult = await discussionService.DeleteReplyAsync(replyBId, studentB.Id);
        var delReply = await db.LessonDiscussionReplies.FindAsync(replyBId);
        if (delReplyAuthorResult.Success && delReply?.IsDeleted == true && delReply.DeletedById == studentB.Id)
        {
            Pass("Test 17: Author Student B successfully deleted their own reply (soft delete).");
        }
        else
        {
            Fail("Test 17: Author failed to delete their own reply.");
        }

        // --- TEST 18: Student cannot delete another student's thread or reply ---
        var unauthorizedDelReply = await discussionService.DeleteReplyAsync(teacherReplyId, studentA.Id);
        if (!unauthorizedDelReply.Success)
        {
            Pass("Test 18: Student was rejected from deleting another user's reply.");
        }
        else
        {
            Fail("Test 18: Student unexpectedly deleted another user's reply.");
        }

        // --- TEST 19: Course Teacher can moderate (delete) any student's thread with reason ---
        var modDelThreadResult = await discussionService.DeleteThreadAsync(t3Result.ThreadId!.Value, teacherOwner.Id, "Duplicate / Test topic removed by faculty");
        var modThread = await db.LessonDiscussionThreads.FindAsync(t3Result.ThreadId!.Value);
        if (modDelThreadResult.Success && modThread?.IsDeleted == true && modThread.ModerationReason == "Duplicate / Test topic removed by faculty" && modThread.DeletedById == teacherOwner.Id)
        {
            Pass("Test 19: Course Teacher moderated (soft deleted) student's thread with audit reason.");
        }
        else
        {
            Fail("Test 19: Course Teacher failed to moderate student thread.");
        }

        // --- TEST 20: Assigned TA can moderate (delete) a student thread ---
        var tempThread = await discussionService.CreateThreadAsync(item.Id, studentA.Id, "Spam Question", "Spam content");
        var taModResult = await discussionService.DeleteThreadAsync(tempThread.ThreadId!.Value, assignedTa.Id, "Inappropriate content");
        var taModThread = await db.LessonDiscussionThreads.FindAsync(tempThread.ThreadId!.Value);
        if (taModResult.Success && taModThread?.IsDeleted == true && taModThread.DeletedById == assignedTa.Id)
        {
            Pass("Test 20: Assigned TA successfully moderated (soft-deleted) thread.");
        }
        else
        {
            Fail("Test 20: Assigned TA failed to moderate thread.");
        }

        // --- TEST 21: Student Discussions view filters out soft-deleted threads ---
        var studentDiscussions = await discussionService.GetLessonDiscussionsAsync(item.Id, studentA.Id);
        var visibleThreadIds = studentDiscussions.Threads.Select(t => t.Id).ToList();
        if (!visibleThreadIds.Contains(t3Result.ThreadId!.Value) && !visibleThreadIds.Contains(tempThread.ThreadId!.Value))
        {
            Pass("Test 21: Regular student view cleanly filters out soft-deleted / moderated threads.");
        }
        else
        {
            Fail("Test 21: Regular student view still exposed moderated threads.");
        }

        // --- TEST 22: Faculty / Moderator view includes moderated threads with reason ---
        var facultyDiscussions = await discussionService.GetLessonDiscussionsAsync(item.Id, teacherOwner.Id);
        var moderatedThreadVm = facultyDiscussions.Threads.FirstOrDefault(t => t.Id == t3Result.ThreadId!.Value);
        if (moderatedThreadVm != null && moderatedThreadVm.IsDeleted && moderatedThreadVm.ModerationReason == "Duplicate / Test topic removed by faculty")
        {
            Pass("Test 22: Faculty view includes moderated threads with moderation reason and audit trail.");
        }
        else
        {
            Fail("Test 22: Faculty view failed to display moderated thread details.");
        }

        Console.WriteLine("\n============================================================================");
        Console.WriteLine($"RESULTS: {passed} Passed, {failed} Failed.");
        Console.WriteLine("============================================================================\n");

        if (failed > 0)
        {
            throw new Exception($"Lesson Discussions test suite failed with {failed} failures.");
        }
    }
}
