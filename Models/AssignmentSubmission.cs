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
    public virtual ApplicationUser Student { get; set; } = null!;

    public string? FileUrl { get; set; }

    public string? TextResponse { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Submitted;

    public double? Score { get; set; }

    public string? Feedback { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public DateTime? GradedAt { get; set; }

    public string? GradedById { get; set; }

    [ForeignKey(nameof(GradedById))]
    public virtual ApplicationUser? GradedBy { get; set; }
}
