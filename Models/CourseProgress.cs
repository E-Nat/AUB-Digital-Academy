using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class CourseProgress
{
    public int Id { get; set; }

    [Required]
    public string StudentId { get; set; } = string.Empty;

    public int CourseItemId { get; set; }

    [ForeignKey(nameof(CourseItemId))]
    public virtual CourseItem CourseItem { get; set; } = null!;

    public bool IsCompleted { get; set; } = true;

    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
