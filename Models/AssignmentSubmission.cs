using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models;

public class AssignmentSubmission
{
    public int Id { get; set; }

    public int AssignmentId { get; set; }

    [ForeignKey(nameof(AssignmentId))]
    public virtual Assignment Assignment { get; set; } = null!;

    [Required]
    public string StudentId { get; set; } = string.Empty;

    [ForeignKey(nameof(StudentId))]
    public virtual User Student { get; set; } = null!;

    public string? FileUrl { get; set; }

    [StringLength(1000)]
    public string? ExternalUrl { get; set; }

    public string? TextResponse { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Submitted;

    public int AttemptNumber { get; set; } = 1;

    public bool IsLatest { get; set; } = true;

    [StringLength(255)]
    public string? OriginalFileName { get; set; }

    public long? FileSizeBytes { get; set; }

    [StringLength(100)]
    public string? ContentType { get; set; }

    public double? Score { get; set; }

    public string? Feedback { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public DateTime? GradedAt { get; set; }

    public string? GradedById { get; set; }

    [ForeignKey(nameof(GradedById))]
    public virtual User? GradedBy { get; set; }

    public virtual ICollection<AssignmentAttachment> Attachments { get; set; } = new List<AssignmentAttachment>();
}

