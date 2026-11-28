using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

/// <summary>
/// Stores a student's pre-deadline auto-saved answer selection for an in-progress quiz attempt.
/// </summary>
public class QuizAttemptAnswer
{
    public int Id { get; set; }

    public int QuizAttemptId { get; set; }

    [ForeignKey(nameof(QuizAttemptId))]
    public virtual QuizAttempt QuizAttempt { get; set; } = null!;

    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public virtual Question Question { get; set; } = null!;

    public int? SelectedOptionId { get; set; }

    [ForeignKey(nameof(SelectedOptionId))]
    public virtual QuestionOption? SelectedOption { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
