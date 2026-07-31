using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class Quiz
{
    public int Id { get; set; }

    public int CourseItemId { get; set; }

    [ForeignKey(nameof(CourseItemId))]
    public virtual CourseItem CourseItem { get; set; } = null!;

    public int TimeLimitMinutes { get; set; } = 30;

    public double PassingScore { get; set; } = 60.0;

    public bool IsPublished { get; set; }

    public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
    public virtual ICollection<QuizSubmission> Submissions { get; set; } = new List<QuizSubmission>();
}
