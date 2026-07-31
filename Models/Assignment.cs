using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class Assignment
{
    public int Id { get; set; }

    public int CourseItemId { get; set; }

    [ForeignKey(nameof(CourseItemId))]
    public virtual CourseItem CourseItem { get; set; } = null!;

    [Required]
    public string Instruction { get; set; } = string.Empty;

    public double MaxScore { get; set; } = 100.0;

    [Required]
    public DateTime DueDate { get; set; }

    public virtual ICollection<AssignmentSubmission> Submissions { get; set; } = new List<AssignmentSubmission>();
}
