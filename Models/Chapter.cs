using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class Chapter
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public int Position { get; set; }

    public int CourseId { get; set; }

    [ForeignKey(nameof(CourseId))]
    public virtual Course Course { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<CourseItem> CourseItems { get; set; } = new List<CourseItem>();
}
