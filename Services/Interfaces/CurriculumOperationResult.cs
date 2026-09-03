namespace AUBDigitalAcademy.Services.Interfaces;

public enum CurriculumOperationStatus
{
    Success,
    NotFound,
    ValidationError,
    BlockedByStudentData,
    Failed
}

public class CurriculumOperationResult
{
    public CurriculumOperationStatus Status { get; set; }
    public bool IsSuccess => Status == CurriculumOperationStatus.Success;
    public string Message { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? EntityTitle { get; set; }
    public int? ParentId { get; set; }
    public List<string> Errors { get; set; } = new();
    public int ProgressCount { get; set; }
    public int SubmissionCount { get; set; }

    public static CurriculumOperationResult Success(int id, string title, string message = "Operation completed successfully.", int? parentId = null) =>
        new()
        {
            Status = CurriculumOperationStatus.Success,
            EntityId = id,
            EntityTitle = title,
            ParentId = parentId,
            Message = message
        };

    public static CurriculumOperationResult NotFound(string message = "Resource not found.") =>
        new()
        {
            Status = CurriculumOperationStatus.NotFound,
            Message = message
        };

    public static CurriculumOperationResult ValidationError(string message, IEnumerable<string>? errors = null) =>
        new()
        {
            Status = CurriculumOperationStatus.ValidationError,
            Message = message,
            Errors = errors?.ToList() ?? new()
        };

    public static CurriculumOperationResult BlockedByStudentData(
        int id,
        string title,
        string message,
        int progressCount = 0,
        int submissionCount = 0) =>
        new()
        {
            Status = CurriculumOperationStatus.BlockedByStudentData,
            EntityId = id,
            EntityTitle = title,
            Message = message,
            ProgressCount = progressCount,
            SubmissionCount = submissionCount
        };

    public static CurriculumOperationResult Failed(string message) =>
        new()
        {
            Status = CurriculumOperationStatus.Failed,
            Message = message
        };
}
