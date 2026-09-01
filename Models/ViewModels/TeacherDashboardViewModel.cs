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

    // Quick Stats (Only 3 compact summary cards)
    public int ActiveCoursesCount { get; set; } = 3;
    public int PendingReviewsCount { get; set; } = 14;
    public int UpcomingSessionsCount { get; set; } = 5;

    // Needs Your Attention
    public List<TeacherAttentionItemViewModel> AttentionItems { get; set; } = new();

    // Today's Schedule (1-2 consultations)
    public List<TeacherScheduleItemViewModel> TodaySchedule { get; set; } = new();

    // My Courses (Max 3 simplified courses)
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
    public string Title { get; set; } = string.Empty;
    public string CoverImageUrl { get; set; } = string.Empty;
    public int StudentCount { get; set; }
    public int ModuleCount { get; set; }
    public string ManageUrl { get; set; } = string.Empty;
    public string GradientClass { get; set; } = "course-gradient-1";
    public string IconClass { get; set; } = "bi-code-slash";
}
