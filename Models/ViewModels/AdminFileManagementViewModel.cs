using System;
using System.Collections.Generic;
using System.Linq;

namespace AUBDigitalAcademy.Models.ViewModels;

public class AdminFileItemViewModel
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public int AssignmentId { get; set; }
    public string AssignmentTitle { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByName { get; set; }
    public string? DeletionReason { get; set; }
    public DateTime? RestoredAt { get; set; }
    public string? RestoredByName { get; set; }
}

public class AdminFileManagementViewModel
{
    public List<AdminFileItemViewModel> Files { get; set; } = new();
    public int TotalFilesCount => Files.Count;
    public int ActiveFilesCount => Files.Count(f => !f.IsDeleted);
    public int SoftDeletedFilesCount => Files.Count(f => f.IsDeleted);
    public long TotalStorageBytes => Files.Sum(f => f.FileSizeBytes);
}
