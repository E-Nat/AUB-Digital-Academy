using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class QuizSubmission
{
    public int Id { get; set; }

    public int QuizId { get; set; }

    [ForeignKey(nameof(QuizId))]
    public virtual Quiz Quiz { get; set; } = null!;

    [Required]
    public string StudentId { get; set; } = string.Empty;

    [ForeignKey(nameof(StudentId))]
    public virtual ApplicationUser Student { get; set; } = null!;

    public double Score { get; set; }

    public bool IsPassed { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}
