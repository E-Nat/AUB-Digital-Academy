namespace AUBDigitalAcademy.Services.Interfaces;

public enum EnrollmentOperationStatus
{
    Success,
    StudentNotFound,
    CourseNotFound,
    CourseNotAvailable,
    AlreadyEnrolled,
    InvalidStudent,
    Unauthorized,
    BlockedByStudentActivity,
    NotFound,
    Failed
}

public class EnrollmentOperationResult
{
    public EnrollmentOperationStatus Status { get; set; }
    public bool IsSuccess => Status == EnrollmentOperationStatus.Success;
    public string Message { get; set; } = string.Empty;
    public int? EnrollmentId { get; set; }
    public string? StudentId { get; set; }
    public string? StudentName { get; set; }
    public int? CourseId { get; set; }
    public string? CourseTitle { get; set; }
    public int ProgressCount { get; set; }
    public int QuizSubmissionCount { get; set; }
    public int AssignmentSubmissionCount { get; set; }
    public List<string> Errors { get; set; } = new();

    public static EnrollmentOperationResult Success(
        int enrollmentId,
        string studentId,
        int courseId,
        string courseTitle,
        string studentName,
        string message = "Operation completed successfully.") =>
        new()
        {
            Status = EnrollmentOperationStatus.Success,
            EnrollmentId = enrollmentId,
            StudentId = studentId,
            CourseId = courseId,
            CourseTitle = courseTitle,
            StudentName = studentName,
            Message = message
        };

    public static EnrollmentOperationResult StudentNotFound(string message = "Student was not found.") =>
        new()
        {
            Status = EnrollmentOperationStatus.StudentNotFound,
            Message = message
        };

    public static EnrollmentOperationResult CourseNotFound(string message = "Course was not found.") =>
        new()
        {
            Status = EnrollmentOperationStatus.CourseNotFound,
            Message = message
        };

    public static EnrollmentOperationResult CourseNotAvailable(string message = "Course is not available for enrollment.") =>
        new()
        {
            Status = EnrollmentOperationStatus.CourseNotAvailable,
            Message = message
        };

    public static EnrollmentOperationResult AlreadyEnrolled(
        string message = "Student is already enrolled in this course.",
        int? enrollmentId = null) =>
        new()
        {
            Status = EnrollmentOperationStatus.AlreadyEnrolled,
            EnrollmentId = enrollmentId,
            Message = message
        };

    public static EnrollmentOperationResult InvalidStudent(string message = "Invalid student account.") =>
        new()
        {
            Status = EnrollmentOperationStatus.InvalidStudent,
            Message = message
        };

    public static EnrollmentOperationResult Unauthorized(string message = "Unauthorized access.") =>
        new()
        {
            Status = EnrollmentOperationStatus.Unauthorized,
            Message = message
        };

    public static EnrollmentOperationResult NotFound(string message = "Enrollment record not found.") =>
        new()
        {
            Status = EnrollmentOperationStatus.NotFound,
            Message = message
        };

    public static EnrollmentOperationResult BlockedByStudentActivity(
        int enrollmentId,
        string studentId,
        int courseId,
        string courseTitle,
        string studentName,
        string message,
        int progressCount = 0,
        int quizCount = 0,
        int assignmentCount = 0) =>
        new()
        {
            Status = EnrollmentOperationStatus.BlockedByStudentActivity,
            EnrollmentId = enrollmentId,
            StudentId = studentId,
            CourseId = courseId,
            CourseTitle = courseTitle,
            StudentName = studentName,
            Message = message,
            ProgressCount = progressCount,
            QuizSubmissionCount = quizCount,
            AssignmentSubmissionCount = assignmentCount
        };

    public static EnrollmentOperationResult Failed(string message, IEnumerable<string>? errors = null) =>
        new()
        {
            Status = EnrollmentOperationStatus.Failed,
            Message = message,
            Errors = errors?.ToList() ?? new()
        };
}
