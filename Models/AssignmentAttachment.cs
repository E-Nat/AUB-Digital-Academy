using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class AssignmentAttachment
{
    public int Id { get; set; }

    public int SubmissionId { get; set; }

    [ForeignKey(nameof(SubmissionId))]
    public virtual AssignmentSubmission Submission { get; set; } = null!;

    [Required]
    [StringLength(500)]
    public string StoragePath { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Soft delete fields for Admin file retention
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public string? DeletedById { get; set; }

    [ForeignKey(nameof(DeletedById))]
    public virtual User? DeletedBy { get; set; }

    [StringLength(500)]
    public string? DeletionReason { get; set; }

    public DateTime? RestoredAt { get; set; }

    public string? RestoredById { get; set; }

    [ForeignKey(nameof(RestoredById))]
    public virtual User? RestoredBy { get; set; }
}
