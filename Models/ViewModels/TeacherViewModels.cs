using System.ComponentModel.DataAnnotations;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models.ViewModels;

public class TeacherDashboardViewModel
{
    public string CurrentTerm { get; set; } = "Spring Semester 2026";
    public List<string> AvailableTerms { get; set; } = new()
    {
        "Spring Semester 2026",
        "Fall Semester 2025",
        "Summer Term 2025"
    };

    // Quick Stats
    public int ActiveCoursesCount { get; set; }
    public int PendingReviewsCount { get; set; }
    public int ActiveQuizzesCount { get; set; }
    public int TotalStudentsCount { get; set; }

    // Needs Your Attention
    public List<TeacherAttentionItemViewModel> AttentionItems { get; set; } = new();

    // Today's Schedule / Consultations
    public List<TeacherScheduleItemViewModel> TodaySchedule { get; set; } = new();

    // My Courses
    public List<TeacherCourseItemViewModel> Courses { get; set; } = new();
}

public class TeacherAttentionItemViewModel
{
    public int AssignmentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public int PendingCount { get; set; }
    public bool IsUrgent { get; set; }
    public string DueDateFormatted { get; set; } = string.Empty;
    public string ReviewUrl { get; set; } = string.Empty;
}

public class TeacherScheduleItemViewModel
{
    public int Id { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentAvatar { get; set; } = string.Empty;
    public string StudentInitials { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string TimeDisplay { get; set; } = string.Empty;
    public string SessionUrl { get; set; } = string.Empty;
    public bool IsToday { get; set; }
}

public class TeacherCourseItemViewModel
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MajorName { get; set; } = string.Empty;
    public string SemesterName { get; set; } = string.Empty;
    public string StatusDisplay { get; set; } = "Published";
    public string CoverImageUrl { get; set; } = string.Empty;
    public int StudentCount { get; set; }
    public int ModuleCount { get; set; }
    public string ManageUrl { get; set; } = string.Empty;
    public string GradientClass { get; set; } = "course-gradient-1";
    public string IconClass { get; set; } = "bi-code-slash";
    public string RoleLabel { get; set; } = "Instructor";
    public bool CanGrade { get; set; } = true;
}

public class TeacherCourseDetailsViewModel
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string MajorName { get; set; } = string.Empty;
    public string SemesterName { get; set; } = string.Empty;
    public string Status { get; set; } = "Published";
    public int StudentCount { get; set; }
    public int ChapterCount { get; set; }
    public int AssignmentCount { get; set; }
    public int QuizCount { get; set; }
    public string RoleLabel { get; set; } = "Instructor";
    public bool CanGrade { get; set; } = true;

    public List<TeacherChapterViewModel> Chapters { get; set; } = new();
    public List<TeacherStudentRosterViewModel> EnrolledStudents { get; set; } = new();
}

public class TeacherChapterViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
    public int ItemCount { get; set; }
    public List<TeacherCourseItemDetailViewModel> Items { get; set; } = new();
}

public class TeacherCourseItemDetailViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public ItemType Type { get; set; }
    public string TypeDisplay => Type.ToString();
}

public class TeacherStudentRosterViewModel
{
    public string StudentId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? UniversityId { get; set; }
    public string Avatar { get; set; } = string.Empty;
    public DateTime EnrolledAt { get; set; }
}

public class TeacherAssignmentItemViewModel
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public double MaxScore { get; set; }
    public int TotalSubmissions { get; set; }
    public int PendingSubmissions { get; set; }
    public int GradedSubmissions { get; set; }
    public bool IsPastDue => DateTime.UtcNow > DueDate;
}

public class TeacherSubmissionItemViewModel
{
    public int SubmissionId { get; set; }
    public int AssignmentId { get; set; }
    public string AssignmentTitle { get; set; } = string.Empty;
    public double MaxScore { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public string? StudentUniversityId { get; set; }
    public string StudentAvatar { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public string? OriginalFileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ExternalUrl { get; set; }
    public List<TeacherAttachmentItemViewModel> Attachments { get; set; } = new();
    public int AttemptNumber { get; set; } = 1;
    public bool IsLatest { get; set; } = true;
    public string? TextResponse { get; set; }
    public SubmissionStatus Status { get; set; }
    public double? Score { get; set; }
    public string? Feedback { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? GradedAt { get; set; }
    public string? GradedByName { get; set; }
    public int CourseId { get; set; }
    public bool CanGrade { get; set; }
}

public class TeacherAttachmentItemViewModel
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

public class GradeSubmissionInputModel
{
    [Required]
    public int SubmissionId { get; set; }

    [Required]
    [Range(0, 100000, ErrorMessage = "Score must be a valid positive number.")]
    public double Score { get; set; }

    [StringLength(1000, ErrorMessage = "Feedback cannot exceed 1000 characters.")]
    public string? Feedback { get; set; }
}

public class TeacherGradingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int SubmissionId { get; set; }
}

public class TeacherGradeItemViewModel
{
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public string? StudentUniversityId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string AssessmentTitle { get; set; } = string.Empty;
    public string AssessmentType { get; set; } = "Assignment"; // Assignment vs Quiz
    public double Score { get; set; }
    public double MaxScore { get; set; }
    public bool IsPassed { get; set; }
    public DateTime Date { get; set; }
}

public class TeacherQuizItemViewModel
{
    public int QuizId { get; set; }
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string QuizTitle { get; set; } = string.Empty;
    public int TimeLimitMinutes { get; set; }
    public double PassingScore { get; set; }
    public bool IsPublished { get; set; }
    public int QuestionCount { get; set; }
    public int TotalAttempts { get; set; }
    public double AverageScore { get; set; }
}

public class TeacherBookItemViewModel
{
    public int BookId { get; set; }
    public int CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string ItemTitle { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long? FileSize { get; set; }
}
