using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models;

public class Question
{
    public int Id { get; set; }

    public int QuizId { get; set; }

    [ForeignKey(nameof(QuizId))]
    public virtual Quiz Quiz { get; set; } = null!;

    [Required]
    public string Text { get; set; } = string.Empty;

    public QuestionType Type { get; set; }

    public double Points { get; set; } = 1.0;

    public int Order { get; set; }

    public virtual ICollection<QuestionOption> Options { get; set; } = new List<QuestionOption>();
}
