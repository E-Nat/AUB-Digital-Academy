using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Models;

namespace AUBDigitalAcademy.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
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
    public DbSet<Material> Materials { get; set; }
    public DbSet<Quiz> Quizzes { get; set; }
    public DbSet<Question> Questions { get; set; }
    public DbSet<QuestionOption> QuestionOptions { get; set; }
    public DbSet<Assignment> Assignments { get; set; }
    public DbSet<Enrollment> Enrollments { get; set; }
    public DbSet<CourseProgress> CourseProgresses { get; set; }
    public DbSet<QuizSubmission> QuizSubmissions { get; set; }
    public DbSet<AssignmentSubmission> AssignmentSubmissions { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ApplicationUser relationships
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(u => u.UniversityId).IsUnique().HasFilter("[UniversityId] IS NOT NULL");
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

        // Material (1:1 with CourseItem)
        builder.Entity<Material>(entity =>
        {
            entity.HasIndex(m => m.CourseItemId).IsUnique();
            entity.HasOne(m => m.CourseItem)
                .WithOne(ci => ci.Material)
                .HasForeignKey<Material>(m => m.CourseItemId)
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
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.HasOne(qs => qs.Quiz)
                .WithMany(q => q.Submissions)
                .HasForeignKey(qs => qs.QuizId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(qs => qs.Student)
                .WithMany(u => u.QuizSubmissions)
                .HasForeignKey(qs => qs.StudentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AssignmentSubmission
        builder.Entity<AssignmentSubmission>(entity =>
        {
            entity.HasOne(asub => asub.Assignment)
                .WithMany(a => a.Submissions)
                .HasForeignKey(asub => asub.AssignmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(asub => asub.Student)
                .WithMany(u => u.AssignmentSubmissions)
                .HasForeignKey(asub => asub.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(asub => asub.GradedBy)
                .WithMany(u => u.GradedSubmissions)
                .HasForeignKey(asub => asub.GradedById)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
