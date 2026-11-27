using System.ComponentModel.DataAnnotations;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models.ViewModels;

public class EnrollmentViewModel
{
    public int Id { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public string? UniversityId { get; set; }
    public string? Avatar { get; set; }
    public int CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public DateTime EnrolledAt { get; set; }
    public bool HasAcademicActivity { get; set; }
}

public class StudentEnrollmentItemViewModel
{
    public int EnrollmentId { get; set; }
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Thumbnail { get; set; }
    public CourseStatus Status { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public string MajorName { get; set; } = string.Empty;
    public string SemesterName { get; set; } = string.Empty;
    public DateTime EnrolledAt { get; set; }
    public int TotalLessons { get; set; }
    public int CompletedLessons { get; set; }
    public int ProgressPercentage => TotalLessons > 0 ? (int)Math.Round((double)CompletedLessons / TotalLessons * 100) : 0;
    public int TotalVideoLessons { get; set; }
    public int CompletedVideoLessons { get; set; }
    public int VideoProgressPercentage => TotalVideoLessons > 0 ? Math.Min(100, (int)Math.Round((double)CompletedVideoLessons / TotalVideoLessons * 100)) : 0;
    public DateTime? LastVideoActivityAt { get; set; }
    public bool HasLearningActivity { get; set; }
}

public class CourseStudentEnrollmentViewModel
{
    public int EnrollmentId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? UniversityId { get; set; }
    public string? Avatar { get; set; }
    public bool IsActive { get; set; }
    public DateTime EnrolledAt { get; set; }
    public bool HasLearningActivity { get; set; }
    public int ProgressCount { get; set; }
    public int QuizSubmissionCount { get; set; }
    public int AssignmentSubmissionCount { get; set; }
}

public class CourseEnrollmentListViewModel
{
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string TeacherId { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public CourseStatus Status { get; set; }
    public List<CourseStudentEnrollmentViewModel> Students { get; set; } = new();
    public int TotalEnrollments => Students.Count;

    // Available students not yet enrolled in this course (for modal "+ Enroll Student")
    public List<StudentLookupItemViewModel> AvailableStudents { get; set; } = new();
}

public class StudentLookupItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? UniversityId { get; set; }
    public int EnrolledCoursesCount { get; set; }
}

public class AvailableCourseOptionViewModel
{
    public int CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public CourseStatus Status { get; set; }
    public bool IsAlreadyEnrolled { get; set; }
}

public class AdminStudentEnrollmentsViewModel
{
    // Selected student info (null if no student selected or no students exist)
    public string? SelectedStudentId { get; set; }
    public string? StudentName { get; set; }
    public string? StudentEmail { get; set; }
    public string? UniversityId { get; set; }
    public string? Avatar { get; set; }
    public bool IsActive { get; set; }

    // Enrolled courses for selected student
    public List<StudentEnrollmentItemViewModel> EnrolledCourses { get; set; } = new();

    // Available courses for enrollment dropdown
    public List<AvailableCourseOptionViewModel> AvailableCourses { get; set; } = new();

    // All students for dropdown/selector
    public List<StudentLookupItemViewModel> AllStudents { get; set; } = new();

    // KPI stats
    public int TotalStudentsCount { get; set; }
    public int TotalActiveEnrollmentsCount { get; set; }
    public int SelectedStudentEnrollmentCount => EnrolledCourses.Count;
}

public class EnrollStudentRequest
{
    [Required]
    public string StudentId { get; set; } = string.Empty;

    [Required]
    public int CourseId { get; set; }

    public string? ReturnUrl { get; set; }
}
