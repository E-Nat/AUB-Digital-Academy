using System;
using System.Collections.Generic;
using System.Linq;

namespace AUBDigitalAcademy.Models.ViewModels;

public class StudentDashboardViewModel
{
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string? UniversityId { get; set; }
    public string StudentEmail { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public int EnrolledCoursesCount => EnrolledCourses.Count;
    public int CompletedLessonsCount { get; set; }
    public string CurrentTermName { get; set; } = "Current Term";
    public List<StudentEnrollmentItemViewModel> EnrolledCourses { get; set; } = new();
}

public class StudentMyCoursesViewModel
{
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string? UniversityId { get; set; }
    public List<StudentEnrollmentItemViewModel> Courses { get; set; } = new();
    public int TotalCourses => Courses.Count;
}

public class StudentCourseOverviewViewModel
{
    public int CourseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Thumbnail { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public string TeacherEmail { get; set; } = string.Empty;
    public string MajorName { get; set; } = string.Empty;
    public string SemesterName { get; set; } = string.Empty;
    public DateTime EnrolledAt { get; set; }
    public List<StudentCourseModuleOverviewViewModel> Modules { get; set; } = new();
    public int TotalModules => Modules.Count;
    public int TotalLessons => Modules.Sum(m => m.TotalLessons);
    public int CompletedLessons => Modules.Sum(m => m.CompletedLessons);
    public int ProgressPercentage => TotalLessons > 0 ? Math.Min(100, (int)Math.Round((double)CompletedLessons / TotalLessons * 100)) : 0;

    /// <summary>
    /// ID of the first available lesson in curriculum order, used for the "Start Learning" action.
    /// </summary>
    public int? FirstCourseItemId { get; set; }
    public string? FirstLessonTitle { get; set; }
    public bool HasContent => FirstCourseItemId.HasValue;
}

public class StudentCourseModuleOverviewViewModel
{
    public int ModuleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
    public List<StudentCourseLessonOverviewViewModel> Lessons { get; set; } = new();
    public int TotalLessons => Lessons.Count(l => l.Type == AUBDigitalAcademy.Models.Enums.ItemType.Lesson);
    public int CompletedLessons => Lessons.Count(l => l.Type == AUBDigitalAcademy.Models.Enums.ItemType.Lesson && l.IsCompleted);
    public int ProgressPercentage => TotalLessons > 0 ? Math.Min(100, (int)Math.Round((double)CompletedLessons / TotalLessons * 100)) : 0;
}

public class StudentCourseLessonOverviewViewModel
{
    public int CourseItemId { get; set; }
    public int LessonId { get; set; }
    public int? QuizId { get; set; }
    public int? AssignmentId { get; set; }
    public int? BookId { get; set; }
    public AUBDigitalAcademy.Models.Enums.ItemType Type { get; set; } = AUBDigitalAcademy.Models.Enums.ItemType.Lesson;
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
    public bool HasVideo { get; set; }
    public bool HasContent { get; set; }
    public bool IsCompleted { get; set; }
    public bool HasPassedQuiz { get; set; }
    public double? BestQuizScore { get; set; }
}

/// <summary>
/// Main presentation ViewModel for the Student Lesson Player.
/// Contains current lesson content, video source, navigation links (Previous / Next),
/// completion state, progress tracking, and the complete course curriculum navigation tree.
/// </summary>
public class StudentLessonPlayerViewModel
{
    // Course Context
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string? TeacherName { get; set; }

    // Current Module
    public int ModuleId { get; set; }
    public string ModuleTitle { get; set; } = string.Empty;
    public int ModulePosition { get; set; }

    // Current Lesson Details
    public int CourseItemId { get; set; }
    public int LessonId { get; set; }
    public string LessonTitle { get; set; } = string.Empty;
    public int LessonPosition { get; set; }
    public string? ContentHtml { get; set; }
    public string? VideoUrl { get; set; }
    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoUrl);
    public bool HasContent => !string.IsNullOrWhiteSpace(ContentHtml);
    public bool IsCompleted { get; set; }
    public bool IsLocked { get; set; }
    public List<string> MissingPrerequisites { get; set; } = new();
    public AUBDigitalAcademy.Models.Enums.CourseAccessMode AccessMode { get; set; } = AUBDigitalAcademy.Models.Enums.CourseAccessMode.Free;
    public double LastVideoPositionSeconds { get; set; }
    public int VideoQualifyingPercentage { get; set; }

    // Curriculum Position & Progress Tracking
    public int CurrentLessonIndex { get; set; } // 1-based index (e.g. Lesson 2 of 10)
    public int TotalLessonsCount { get; set; }
    public int CompletedLessonsCount { get; set; }
    public int CourseProgressPercentage => TotalLessonsCount > 0 ? (int)Math.Round((double)CompletedLessonsCount / TotalLessonsCount * 100) : 0;

    // Navigation Links
    public StudentLessonNavigationItemViewModel? PreviousLesson { get; set; }
    public StudentLessonNavigationItemViewModel? NextLesson { get; set; }
    public bool HasPrevious => PreviousLesson != null;
    public bool HasNext => NextLesson != null;

    // Curriculum Navigation Sidebar
    public List<StudentPlayerModuleViewModel> Modules { get; set; } = new();

    // Lesson Discussions (Phase 5B)
    public LessonDiscussionsViewModel Discussions { get; set; } = new();
}

public class StudentLessonNavigationItemViewModel
{
    public int CourseItemId { get; set; }
    public int LessonId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int ModulePosition { get; set; }
    public int LessonPosition { get; set; }
}

public class StudentPlayerModuleViewModel
{
    public int ModuleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
    public bool IsCurrentModule { get; set; }
    public List<StudentPlayerLessonItemViewModel> Lessons { get; set; } = new();
    public int LessonCount => Lessons.Count;
    public int CompletedCount => Lessons.Count(l => l.IsCompleted);
}

public class StudentPlayerLessonItemViewModel
{
    public int CourseItemId { get; set; }
    public int LessonId { get; set; }
    public int? QuizId { get; set; }
    public int? AssignmentId { get; set; }
    public int? BookId { get; set; }
    public AUBDigitalAcademy.Models.Enums.ItemType Type { get; set; } = AUBDigitalAcademy.Models.Enums.ItemType.Lesson;
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
    public bool HasVideo { get; set; }
    public bool HasContent { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsLocked { get; set; }
    public List<string> MissingPrerequisites { get; set; } = new();
    public bool HasPassedQuiz { get; set; }
    public double? BestQuizScore { get; set; }
}

public class StudentQuizPlayerViewModel
{
    // Course Context
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string? TeacherName { get; set; }

    // Module Context
    public int ModuleId { get; set; }
    public string ModuleTitle { get; set; } = string.Empty;
    public int ModulePosition { get; set; }

    // Quiz Details
    public int CourseItemId { get; set; }
    public int QuizId { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public int QuizPosition { get; set; }
    public int TimeLimitMinutes { get; set; } = 30;
    public int? RemainingSeconds { get; set; }
    public int? ActiveAttemptId { get; set; }
    public DateTime? AttemptStartedAtUtc { get; set; }
    public DateTime? AttemptDeadlineUtc { get; set; }
    public DateTime ServerNowUtc { get; set; } = DateTime.UtcNow;
    public string? ExpiredAttemptNotice { get; set; }
    public Dictionary<int, int> SavedAnswers { get; set; } = new();
    public int MaxAttempts { get; set; } = 3;
    public double PassingScore { get; set; } = 60.0;
    public bool IsPublished { get; set; }

    // Submission Context
    public bool IsAdminPreview { get; set; }
    public bool IsLocked { get; set; }
    public List<string> MissingPrerequisites { get; set; } = new();
    public bool HasSubmission { get; set; }
    public int AttemptCount { get; set; }
    public int CurrentAttemptNumber => AttemptCount + 1;
    public int RemainingAttempts => Math.Max(0, MaxAttempts - AttemptCount);
    public bool CanAttempt => CurrentAttemptNumber <= MaxAttempts;
    public bool RequiresExplicitNextAttemptStart => !IsAdminPreview && !ActiveAttemptId.HasValue && HasSubmission && CanAttempt;
    public int? LatestSubmissionId { get; set; }
    public double? LatestScore { get; set; }
    public double? BestScore { get; set; }
    public bool? LatestIsPassed { get; set; }
    public bool LatestIsAutoFinalized { get; set; }
    public int? LatestCorrectQuestionsCount { get; set; }
    public int? LatestTotalQuestionsCount { get; set; }
    public DateTime? LatestSubmittedAt { get; set; }

    // Questions
    public List<StudentQuizQuestionViewModel> Questions { get; set; } = new();
    public int TotalQuestions => Questions.Count;
    public double TotalPoints => Questions.Sum(q => q.Points);

    // Curriculum Navigation Sidebar
    public List<StudentPlayerModuleViewModel> Modules { get; set; } = new();
    public int CourseProgressPercentage { get; set; }
    public int CompletedLessonsCount { get; set; }
    public int TotalLessonsCount { get; set; }
}

public class StudentQuizQuestionViewModel
{
    public int QuestionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public double Points { get; set; } = 1.0;
    public int Order { get; set; }
    public AUBDigitalAcademy.Models.Enums.QuestionType Type { get; set; }
    public List<StudentQuizOptionViewModel> Options { get; set; } = new();
}

public class StudentQuizOptionViewModel
{
    public int OptionId { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class StudentQuizResultViewModel
{
    // Course Context
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;

    // Quiz Context
    public int CourseItemId { get; set; }
    public int QuizId { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public double PassingScore { get; set; }

    // Submission Outcome
    public int SubmissionId { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public int MaxAttempts { get; set; } = 3;
    public bool CanRetake => AttemptNumber < MaxAttempts;
    public bool AreAttemptsExhausted { get; set; }
    public double Score { get; set; }
    public double? BestScore { get; set; }
    public bool IsPassed { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public bool IsAutoFinalized { get; set; }
    public DateTime SubmittedAt { get; set; }
    public double TotalPoints { get; set; }
    public double EarnedPoints { get; set; }
    public int TotalQuestions { get; set; }
    public int CorrectQuestionsCount { get; set; }

    // Question-by-Question Breakdown
    public List<StudentQuestionResultItemViewModel> Questions { get; set; } = new();
}

public class StudentQuestionResultItemViewModel
{
    public int QuestionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public double Points { get; set; }
    public double EarnedPoints { get; set; }
    public bool IsCorrect { get; set; }
    public int? SelectedOptionId { get; set; }
    public string? SelectedOptionText { get; set; }
    public List<StudentOptionResultItemViewModel> Options { get; set; } = new();
}

public class StudentOptionResultItemViewModel
{
    public int OptionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public bool IsSelected { get; set; }
}

// =========================================================================
// PHASE 4 — ASSIGNMENT, GRADEBOOK, AND BOOK VIEWMODELS
// =========================================================================

public class StudentAssignmentViewModel
{
    // Course Context
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string? TeacherName { get; set; }

    // Module Context
    public int ModuleId { get; set; }
    public string ModuleTitle { get; set; } = string.Empty;
    public int ModulePosition { get; set; }

    // Assignment Details
    public int CourseItemId { get; set; }
    public int AssignmentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
    public string Instruction { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public double MaxScore { get; set; } = 100.0;
    public bool IsPastDue => DateTime.UtcNow > DueDate;

    // Latest Submission Context
    public bool IsLocked { get; set; }
    public List<string> MissingPrerequisites { get; set; } = new();
    public bool HasSubmission { get; set; }
    public int? LatestSubmissionId { get; set; }
    public int LatestAttemptNumber { get; set; }
    public AUBDigitalAcademy.Models.Enums.SubmissionStatus? LatestStatus { get; set; }
    public double? LatestScore { get; set; }
    public string? LatestFeedback { get; set; }
    public DateTime? LatestSubmittedAt { get; set; }
    public DateTime? LatestGradedAt { get; set; }
    public string? LatestGradedByName { get; set; }
    public string? LatestFileUrl { get; set; }
    public string? LatestOriginalFileName { get; set; }
    public string? LatestExternalUrl { get; set; }
    public string? LatestTextResponse { get; set; }
    public List<StudentAttachmentViewModel> LatestAttachments { get; set; } = new();

    // Version / Resubmission History
    public List<StudentAssignmentAttemptViewModel> SubmissionHistory { get; set; } = new();

    // Curriculum Navigation Sidebar
    public List<StudentPlayerModuleViewModel> Modules { get; set; } = new();
    public int CourseProgressPercentage { get; set; }
    public int CompletedLessonsCount { get; set; }
    public int TotalLessonsCount { get; set; }
}

public class StudentAttachmentViewModel
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

public class StudentAssignmentAttemptViewModel
{
    public int SubmissionId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime SubmittedAt { get; set; }
    public AUBDigitalAcademy.Models.Enums.SubmissionStatus Status { get; set; }
    public double? Score { get; set; }
    public double MaxScore { get; set; }
    public string? Feedback { get; set; }
    public string? FileUrl { get; set; }
    public string? OriginalFileName { get; set; }
    public string? ExternalUrl { get; set; }
    public string? TextResponse { get; set; }
    public bool IsLatest { get; set; }
    public List<StudentAttachmentViewModel> Attachments { get; set; } = new();
}

public class StudentBookViewModel
{
    // Course Context
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string? TeacherName { get; set; }

    // Module Context
    public int ModuleId { get; set; }
    public string ModuleTitle { get; set; } = string.Empty;
    public int ModulePosition { get; set; }

    // Book Details
    public int CourseItemId { get; set; }
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public string FileType { get; set; } = "PDF";
    public long? FileSize { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsLocked { get; set; }
    public List<string> MissingPrerequisites { get; set; } = new();

    // Curriculum Navigation Sidebar
    public List<StudentPlayerModuleViewModel> Modules { get; set; } = new();
    public int CourseProgressPercentage { get; set; }
    public int CompletedLessonsCount { get; set; }
    public int TotalLessonsCount { get; set; }
}

public class StudentGradesViewModel
{
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string? UniversityId { get; set; }
    public string StudentEmail { get; set; } = string.Empty;
    public string? Avatar { get; set; }

    // Course Filtering
    public int? SelectedCourseId { get; set; }
    public List<StudentGradeCourseOptionViewModel> EnrolledCourses { get; set; } = new();

    // Summary Metrics
    public int TotalAssessmentsCount => GradeItems.Count;
    public int CompletedAssessmentsCount => GradeItems.Count(g => g.IsCompleted);
    public int PassedAssessmentsCount => GradeItems.Count(g => g.IsPassed);
    public double OverallAveragePercentage => GradeItems.Any(g => g.ScorePercentage.HasValue)
        ? Math.Round(GradeItems.Where(g => g.ScorePercentage.HasValue).Average(g => g.ScorePercentage!.Value), 1)
        : 0.0;

    // Detail Items
    public List<StudentGradeItemViewModel> GradeItems { get; set; } = new();
}

public class StudentGradeCourseOptionViewModel
{
    public int CourseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public class StudentGradeItemViewModel
{
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;

    public int AssessmentId { get; set; }
    public int CourseItemId { get; set; }
    public string AssessmentTitle { get; set; } = string.Empty;
    public string AssessmentType { get; set; } = "Assignment"; // "Assignment" | "Quiz"

    public double? Score { get; set; }
    public double MaxScore { get; set; }
    public double? ScorePercentage => Score.HasValue && MaxScore > 0 ? Math.Round((Score.Value / MaxScore) * 100.0, 1) : null;
    public bool IsPassed { get; set; }
    public bool IsCompleted { get; set; }
    public string StatusDisplay { get; set; } = "Pending";

    public DateTime Date { get; set; }
    public string? Feedback { get; set; }
    public string? GradedByName { get; set; }
    public string DetailUrl { get; set; } = string.Empty;
}
