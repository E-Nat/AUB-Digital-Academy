namespace AUBDigitalAcademy.Services.Interfaces;

public enum CourseDeleteStatus
{
    Success,
    NotFound,
    BlockedByStudentData,
    Failed
}

public class CourseDeleteResult
{
    public CourseDeleteStatus Status { get; set; }
    public bool IsSuccess => Status == CourseDeleteStatus.Success;
    public bool IsBlockedByStudentData => Status == CourseDeleteStatus.BlockedByStudentData;
    public string Message { get; set; } = string.Empty;
    public int? CourseId { get; set; }
    public string? CourseTitle { get; set; }
    public int EnrollmentCount { get; set; }
    public int ProgressCount { get; set; }
    public int QuizSubmissionCount { get; set; }
    public int AssignmentSubmissionCount { get; set; }

    public static CourseDeleteResult Success(int courseId, string courseTitle, string message = "Course deleted successfully.") =>
        new()
        {
            Status = CourseDeleteStatus.Success,
            CourseId = courseId,
            CourseTitle = courseTitle,
            Message = message
        };

    public static CourseDeleteResult NotFound(string message = "Course not found.") =>
        new()
        {
            Status = CourseDeleteStatus.NotFound,
            Message = message
        };

    public static CourseDeleteResult BlockedByStudentData(
        int courseId,
        string courseTitle,
        string message,
        int enrollmentCount = 0,
        int progressCount = 0,
        int quizSubmissionCount = 0,
        int assignmentSubmissionCount = 0) =>
        new()
        {
            Status = CourseDeleteStatus.BlockedByStudentData,
            CourseId = courseId,
            CourseTitle = courseTitle,
            Message = message,
            EnrollmentCount = enrollmentCount,
            ProgressCount = progressCount,
            QuizSubmissionCount = quizSubmissionCount,
            AssignmentSubmissionCount = assignmentSubmissionCount
        };

    public static CourseDeleteResult Failed(string message) =>
        new()
        {
            Status = CourseDeleteStatus.Failed,
            Message = message
        };
}
