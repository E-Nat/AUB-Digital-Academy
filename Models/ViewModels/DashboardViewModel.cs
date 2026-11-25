namespace AUBDigitalAcademy.Models.ViewModels;

public class AdminDashboardViewModel
{
    // Institutional KPIs
    public int TotalStudents { get; set; }
    public int TotalCourses { get; set; }
    public int PublishedCourses { get; set; }
    public int DraftCourses { get; set; }
    public int TotalEnrollments { get; set; }
    public int TotalTeachers { get; set; }
    public int TotalModules { get; set; }
    public int TotalQuizzes { get; set; }

    // Term metadata
    public string CurrentSemesterName { get; set; } = "Spring Semester 2026";
    public string CurrentDateFormatted { get; set; } = string.Empty;
    public string AdminFullName { get; set; } = "Administrator";

    // Academic Distribution / Major Statistics (for Donut Chart & Legend)
    public List<MajorEnrollmentStatViewModel> MajorsDistribution { get; set; } = new();

    // Course Status Distribution (for Trend / Bar Chart)
    public List<SemesterCourseStatViewModel> SemesterDistribution { get; set; } = new();

    // Recent Student Enrollments
    public List<DashboardRecentEnrollmentViewModel> RecentEnrollments { get; set; } = new();

    // Upcoming / Published Quizzes & Exams
    public List<DashboardQuizItemViewModel> RecentQuizzes { get; set; } = new();

    // Real Recent Institutional Activities
    public List<DashboardActivityItemViewModel> RecentActivities { get; set; } = new();

    // Financial Intelligence Support Metadata
    public bool HasFinancialData { get; set; } = false;
    public string FinancialStatusNotice { get; set; } = "Financial ledger records are not yet configured in this deployment.";
}

public class MajorEnrollmentStatViewModel
{
    public string MajorName { get; set; } = string.Empty;
    public string MajorCode { get; set; } = string.Empty;
    public int StudentCount { get; set; }
    public int CourseCount { get; set; }
    public double Percentage { get; set; }
    public string ColorHex { get; set; } = "#2563EB";
}

public class SemesterCourseStatViewModel
{
    public string SemesterName { get; set; } = string.Empty;
    public string SemesterCode { get; set; } = string.Empty;
    public int CourseCount { get; set; }
    public int EnrollmentCount { get; set; }
    public bool IsCurrent { get; set; }
}

public class DashboardRecentEnrollmentViewModel
{
    public int EnrollmentId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string UniversityId { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public DateTime EnrolledAt { get; set; }
    public int ProgressPercentage { get; set; }
    public string Status { get; set; } = "Active";
}

public class DashboardQuizItemViewModel
{
    public int QuizId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public int TimeLimitMinutes { get; set; }
    public double PassingScore { get; set; }
    public int QuestionCount { get; set; }
    public bool IsPublished { get; set; }
}

public class DashboardActivityItemViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string IconClass { get; set; } = "bi-activity";
    public string IconColorClass { get; set; } = "icon-blue";
    public string TimeAgo { get; set; } = string.Empty;
}

public class DashboardViewModel : AdminDashboardViewModel
{
}
