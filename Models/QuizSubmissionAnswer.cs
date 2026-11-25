using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class QuizSubmissionAnswer
{
    public int Id { get; set; }

    public int QuizSubmissionId { get; set; }

    [ForeignKey(nameof(QuizSubmissionId))]
    public virtual QuizSubmission QuizSubmission { get; set; } = null!;

    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public virtual Question Question { get; set; } = null!;

    public int? SelectedOptionId { get; set; }

    [ForeignKey(nameof(SelectedOptionId))]
    public virtual QuestionOption? SelectedOption { get; set; }

    public string? AnswerText { get; set; }

    public double PointsEarned { get; set; } = 0.0;

    public bool? IsCorrect { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
