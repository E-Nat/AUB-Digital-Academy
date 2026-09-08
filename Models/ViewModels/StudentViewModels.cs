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
    public int TotalLessons => Modules.Sum(m => m.Lessons.Count);
    public int CompletedLessons => Modules.Sum(m => m.Lessons.Count(l => l.IsCompleted));
    public int ProgressPercentage => TotalLessons > 0 ? (int)Math.Round((double)CompletedLessons / TotalLessons * 100) : 0;

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
    public int TotalLessons => Lessons.Count;
    public int CompletedLessons => Lessons.Count(l => l.IsCompleted);
    public int ProgressPercentage => TotalLessons > 0 ? (int)Math.Round((double)CompletedLessons / TotalLessons * 100) : 0;
}

public class StudentCourseLessonOverviewViewModel
{
    public int CourseItemId { get; set; }
    public int LessonId { get; set; }
    public int? QuizId { get; set; }
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
    public AUBDigitalAcademy.Models.Enums.ItemType Type { get; set; } = AUBDigitalAcademy.Models.Enums.ItemType.Lesson;
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
    public bool HasVideo { get; set; }
    public bool HasContent { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsCompleted { get; set; }
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
    public double PassingScore { get; set; } = 60.0;
    public bool IsPublished { get; set; }

    // Submission Context
    public bool HasSubmission { get; set; }
    public int? LatestSubmissionId { get; set; }
    public double? LatestScore { get; set; }
    public bool? LatestIsPassed { get; set; }
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
    public double Score { get; set; }
    public bool IsPassed { get; set; }
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
