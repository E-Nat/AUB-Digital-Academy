using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class CourseAssistant
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    [ForeignKey(nameof(CourseId))]
    public virtual Course Course { get; set; } = null!;

    [Required]
    public string AssistantId { get; set; } = string.Empty;

    [ForeignKey(nameof(AssistantId))]
    public virtual User Assistant { get; set; } = null!;

    public bool CanGrade { get; set; } = false;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
