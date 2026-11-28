using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using AUBDigitalAcademy.Models;

namespace AUBDigitalAcademy.Data;

public class ApplicationDbContext : IdentityDbContext<User>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories { get; set; }
    public DbSet<Major> Majors { get; set; }
    public DbSet<Semester> Semesters { get; set; }
    public DbSet<Course> Courses { get; set; }
    public DbSet<Chapter> Chapters { get; set; }
    public DbSet<CourseItem> CourseItems { get; set; }
    public DbSet<Lesson> Lessons { get; set; }
    public DbSet<Book> Books { get; set; }
    public DbSet<Quiz> Quizzes { get; set; }
    public DbSet<Question> Questions { get; set; }
    public DbSet<QuestionOption> QuestionOptions { get; set; }
    public DbSet<Assignment> Assignments { get; set; }
    public DbSet<Enrollment> Enrollments { get; set; }
    public DbSet<CourseProgress> CourseProgresses { get; set; }
    public DbSet<VideoLessonProgress> VideoLessonProgresses { get; set; }
    public DbSet<CourseItemPrerequisite> CourseItemPrerequisites { get; set; }
    public DbSet<LessonDiscussionThread> LessonDiscussionThreads { get; set; }
    public DbSet<LessonDiscussionReply> LessonDiscussionReplies { get; set; }
    public DbSet<Announcement> Announcements { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<QuizSubmission> QuizSubmissions { get; set; }
    public DbSet<QuizAttempt> QuizAttempts { get; set; }
    public DbSet<QuizAttemptAnswer> QuizAttemptAnswers { get; set; }
    public DbSet<AssignmentSubmission> AssignmentSubmissions { get; set; }
    public DbSet<AssignmentAttachment> AssignmentAttachments { get; set; }
    public DbSet<QuizSubmissionAnswer> QuizSubmissionAnswers { get; set; }
    public DbSet<CourseAssistant> CourseAssistants { get; set; }
    public DbSet<TelegramVerificationRequest> TelegramVerificationRequests { get; set; }
    public DbSet<TelegramLinkToken> TelegramLinkTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // User relationships
        builder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.UniversityId).IsUnique().HasFilter("[UniversityId] IS NOT NULL");
            entity.HasIndex(u => u.TelegramChatId).IsUnique().HasFilter("[TelegramChatId] IS NOT NULL");
        });

        // TelegramVerificationRequest
        builder.Entity<TelegramVerificationRequest>(entity =>
        {
            entity.HasIndex(r => r.UserId);
            entity.HasIndex(r => r.UniversityId);
            entity.HasIndex(r => new { r.UserId, r.IsUsed, r.ExpiresAt });

            entity.HasOne(r => r.User)
                .WithMany(u => u.TelegramVerificationRequests)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // TelegramLinkToken
        builder.Entity<TelegramLinkToken>(entity =>
        {
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => new { t.UserId, t.IsUsed, t.ExpiresAt });

            entity.HasOne(t => t.User)
                .WithMany(u => u.TelegramLinkTokens)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // VideoLessonProgress
        builder.Entity<VideoLessonProgress>(entity =>
        {
            entity.HasIndex(v => new { v.StudentId, v.CourseItemId }).IsUnique();

            entity.HasOne(v => v.Student)
                .WithMany()
                .HasForeignKey(v => v.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(v => v.CourseItem)
                .WithMany()
                .HasForeignKey(v => v.CourseItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CourseItemPrerequisite
        builder.Entity<CourseItemPrerequisite>(entity =>
        {
            entity.HasIndex(p => new { p.CourseItemId, p.RequiredCourseItemId }).IsUnique();

            entity.HasOne(p => p.CourseItem)
                .WithMany(ci => ci.Prerequisites)
                .HasForeignKey(p => p.CourseItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.RequiredCourseItem)
                .WithMany()
                .HasForeignKey(p => p.RequiredCourseItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // LessonDiscussionThread
        builder.Entity<LessonDiscussionThread>(entity =>
        {
            entity.HasIndex(t => t.CourseItemId);
            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => new { t.CourseItemId, t.IsPinned, t.CreatedAt });

            entity.HasOne(t => t.CourseItem)
                .WithMany(ci => ci.DiscussionThreads)
                .HasForeignKey(t => t.CourseItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.User)
                .WithMany(u => u.DiscussionThreads)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(t => t.DeletedBy)
                .WithMany()
                .HasForeignKey(t => t.DeletedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // LessonDiscussionReply
        builder.Entity<LessonDiscussionReply>(entity =>
        {
            entity.HasIndex(r => r.ThreadId);
            entity.HasIndex(r => r.UserId);
            entity.HasIndex(r => new { r.ThreadId, r.IsPinned, r.CreatedAt });

            entity.HasOne(r => r.Thread)
                .WithMany(t => t.Replies)
                .HasForeignKey(r => r.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.User)
                .WithMany(u => u.DiscussionReplies)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.DeletedBy)
                .WithMany()
                .HasForeignKey(r => r.DeletedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Announcement
        builder.Entity<Announcement>(entity =>
        {
            entity.HasIndex(a => a.CourseId);
            entity.HasIndex(a => a.CreatedById);
            entity.HasIndex(a => new { a.Scope, a.PublishedAt });
            entity.HasIndex(a => new { a.CourseId, a.PublishedAt });

            entity.HasOne(a => a.Course)
                .WithMany(c => c.Announcements)
                .HasForeignKey(a => a.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.CreatedBy)
                .WithMany(u => u.AnnouncementsCreated)
                .HasForeignKey(a => a.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Notification
        builder.Entity<Notification>(entity =>
        {
            entity.HasIndex(n => n.UserId);
            entity.HasIndex(n => n.IsRead);
            entity.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });

            entity.HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Category
        builder.Entity<Category>(entity =>
        {
            entity.HasIndex(c => c.Name).IsUnique();
        });

        // Major
        builder.Entity<Major>(entity =>
        {
            entity.HasIndex(m => m.Code).IsUnique();
            entity.HasOne(m => m.Category)
                .WithMany(c => c.Majors)
                .HasForeignKey(m => m.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Semester
        builder.Entity<Semester>(entity =>
        {
            entity.HasIndex(s => s.Code).IsUnique();
        });

        // Course
        builder.Entity<Course>(entity =>
        {
            entity.HasIndex(c => c.Code).IsUnique();

            entity.HasOne(c => c.Major)
                .WithMany(m => m.Courses)
                .HasForeignKey(c => c.MajorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Semester)
                .WithMany(s => s.Courses)
                .HasForeignKey(c => c.SemesterId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Teacher)
                .WithMany(u => u.TaughtCourses)
                .HasForeignKey(c => c.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Chapter
        builder.Entity<Chapter>(entity =>
        {
            entity.HasOne(ch => ch.Course)
                .WithMany(c => c.Chapters)
                .HasForeignKey(ch => ch.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CourseItem
        builder.Entity<CourseItem>(entity =>
        {
            entity.HasOne(ci => ci.Chapter)
                .WithMany(ch => ch.CourseItems)
                .HasForeignKey(ci => ci.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Lesson (1:1 with CourseItem)
        builder.Entity<Lesson>(entity =>
        {
            entity.HasIndex(l => l.CourseItemId).IsUnique();
            entity.HasOne(l => l.CourseItem)
                .WithOne(ci => ci.Lesson)
                .HasForeignKey<Lesson>(l => l.CourseItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Book (1:1 with CourseItem)
        builder.Entity<Book>(entity =>
        {
            entity.HasIndex(m => m.CourseItemId).IsUnique();
            entity.HasOne(m => m.CourseItem)
                .WithOne(ci => ci.Book)
                .HasForeignKey<Book>(m => m.CourseItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Quiz (1:1 with CourseItem)
        builder.Entity<Quiz>(entity =>
        {
            entity.HasIndex(q => q.CourseItemId).IsUnique();
            entity.HasOne(q => q.CourseItem)
                .WithOne(ci => ci.Quiz)
                .HasForeignKey<Quiz>(q => q.CourseItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Question
        builder.Entity<Question>(entity =>
        {
            entity.HasOne(q => q.Quiz)
                .WithMany(qz => qz.Questions)
                .HasForeignKey(q => q.QuizId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // QuestionOption
        builder.Entity<QuestionOption>(entity =>
        {
            entity.HasOne(o => o.Question)
                .WithMany(q => q.Options)
                .HasForeignKey(o => o.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Assignment (1:1 with CourseItem)
        builder.Entity<Assignment>(entity =>
        {
            entity.HasIndex(a => a.CourseItemId).IsUnique();
            entity.HasOne(a => a.CourseItem)
                .WithOne(ci => ci.Assignment)
                .HasForeignKey<Assignment>(a => a.CourseItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Enrollment (unique constraint: one student per course)
        builder.Entity<Enrollment>(entity =>
        {
            entity.HasIndex(e => new { e.StudentId, e.CourseId }).IsUnique();

            entity.HasOne(e => e.Student)
                .WithMany(u => u.Enrollments)
                .HasForeignKey(e => e.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Course)
                .WithMany(c => c.Enrollments)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // CourseProgress
        builder.Entity<CourseProgress>(entity =>
        {
            entity.HasIndex(cp => new { cp.StudentId, cp.CourseItemId }).IsUnique();

            entity.HasOne(cp => cp.CourseItem)
                .WithMany(ci => ci.Progress)
                .HasForeignKey(cp => cp.CourseItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // QuizSubmission
        builder.Entity<QuizSubmission>(entity =>
        {
            entity.Property(qs => qs.MaxAttemptsAllowed).HasDefaultValue(3);

            entity.HasOne(qs => qs.Quiz)
                .WithMany(q => q.Submissions)
                .HasForeignKey(qs => qs.QuizId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(qs => qs.Student)
                .WithMany(u => u.QuizSubmissions)
                .HasForeignKey(qs => qs.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // QuizAttempt
        builder.Entity<QuizAttempt>(entity =>
        {
            var utcDateTimeConverter = new ValueConverter<DateTime, DateTime>(
                v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            var nullableUtcDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(
                v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            entity.Property(qa => qa.StartedAtUtc)
                .HasConversion(utcDateTimeConverter);

            entity.Property(qa => qa.DeadlineUtc)
                .HasConversion(nullableUtcDateTimeConverter);

            entity.Property(qa => qa.FinalizedAtUtc)
                .HasConversion(nullableUtcDateTimeConverter);

            entity.Property(qa => qa.MaxAttemptsAllowed).HasDefaultValue(3);
            entity.Property(qa => qa.RowVersion).IsRowVersion();

            entity.HasIndex(qa => new { qa.QuizId, qa.StudentId })
                .IsUnique()
                .HasFilter("[Status] = 0");

            entity.HasIndex(qa => new { qa.QuizId, qa.StudentId, qa.AttemptNumber })
                .IsUnique();

            entity.HasIndex(qa => qa.QuizSubmissionId)
                .IsUnique()
                .HasFilter("[QuizSubmissionId] IS NOT NULL");

            entity.HasOne(qa => qa.Quiz)
                .WithMany()
                .HasForeignKey(qa => qa.QuizId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(qa => qa.Student)
                .WithMany()
                .HasForeignKey(qa => qa.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(qa => qa.QuizSubmission)
                .WithOne()
                .HasForeignKey<QuizAttempt>(qa => qa.QuizSubmissionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // QuizAttemptAnswer
        builder.Entity<QuizAttemptAnswer>(entity =>
        {
            entity.HasIndex(a => new { a.QuizAttemptId, a.QuestionId }).IsUnique();

            entity.HasOne(a => a.QuizAttempt)
                .WithMany(qa => qa.DraftAnswers)
                .HasForeignKey(a => a.QuizAttemptId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Question)
                .WithMany()
                .HasForeignKey(a => a.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.SelectedOption)
                .WithMany()
                .HasForeignKey(a => a.SelectedOptionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // AssignmentSubmission
        builder.Entity<AssignmentSubmission>(entity =>
        {
            entity.HasIndex(asub => new { asub.AssignmentId, asub.StudentId, asub.IsLatest });

            entity.HasOne(asub => asub.Assignment)
                .WithMany(a => a.Submissions)
                .HasForeignKey(asub => asub.AssignmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(asub => asub.Student)
                .WithMany(u => u.AssignmentSubmissions)
                .HasForeignKey(asub => asub.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(asub => asub.GradedBy)
                .WithMany(u => u.GradedSubmissions)
                .HasForeignKey(asub => asub.GradedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AssignmentAttachment
        builder.Entity<AssignmentAttachment>(entity =>
        {
            entity.HasIndex(aa => aa.SubmissionId);
            entity.HasIndex(aa => aa.IsDeleted);

            entity.HasOne(aa => aa.Submission)
                .WithMany(s => s.Attachments)
                .HasForeignKey(aa => aa.SubmissionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(aa => aa.DeletedBy)
                .WithMany()
                .HasForeignKey(aa => aa.DeletedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(aa => aa.RestoredBy)
                .WithMany()
                .HasForeignKey(aa => aa.RestoredById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // QuizSubmissionAnswer
        builder.Entity<QuizSubmissionAnswer>(entity =>
        {
            entity.HasIndex(qsa => new { qsa.QuizSubmissionId, qsa.QuestionId });

            entity.HasOne(qsa => qsa.QuizSubmission)
                .WithMany(qs => qs.Answers)
                .HasForeignKey(qsa => qsa.QuizSubmissionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(qsa => qsa.Question)
                .WithMany()
                .HasForeignKey(qsa => qsa.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(qsa => qsa.SelectedOption)
                .WithMany()
                .HasForeignKey(qsa => qsa.SelectedOptionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // CourseAssistant
        builder.Entity<CourseAssistant>(entity =>
        {
            entity.HasIndex(ca => new { ca.CourseId, ca.AssistantId }).IsUnique();
            entity.HasIndex(ca => ca.AssistantId);

            entity.HasOne(ca => ca.Course)
                .WithMany(c => c.CourseAssistants)
                .HasForeignKey(ca => ca.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ca => ca.Assistant)
                .WithMany(u => u.AssistedCourses)
                .HasForeignKey(ca => ca.AssistantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

