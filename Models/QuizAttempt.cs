using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models;

/// <summary>
/// Persists server-controlled timing, status, and draft answer ownership for a single quiz attempt.
/// </summary>
public class QuizAttempt
{
    public int Id { get; set; }

    public int QuizId { get; set; }

    [ForeignKey(nameof(QuizId))]
    public virtual Quiz Quiz { get; set; } = null!;

    [Required]
    public string StudentId { get; set; } = string.Empty;

    [ForeignKey(nameof(StudentId))]
    public virtual User Student { get; set; } = null!;

    public int AttemptNumber { get; set; }

    public QuizAttemptStatus Status { get; set; } = QuizAttemptStatus.InProgress;

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? DeadlineUtc { get; set; }

    public DateTime? FinalizedAtUtc { get; set; }

    public int MaxAttemptsAllowed { get; set; } = 3;

    public int? QuizSubmissionId { get; set; }

    [ForeignKey(nameof(QuizSubmissionId))]
    public virtual QuizSubmission? QuizSubmission { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual ICollection<QuizAttemptAnswer> DraftAnswers { get; set; } = new List<QuizAttemptAnswer>();
}
