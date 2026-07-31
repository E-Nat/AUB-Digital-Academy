using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class QuestionOption
{
    public int Id { get; set; }

    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public virtual Question Question { get; set; } = null!;

    [Required]
    [StringLength(500)]
    public string Text { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }
}
