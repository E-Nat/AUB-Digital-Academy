using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class Material
{
    public int Id { get; set; }

    public int CourseItemId { get; set; }

    [ForeignKey(nameof(CourseItemId))]
    public virtual CourseItem CourseItem { get; set; } = null!;

    [Required]
    public string FileUrl { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string FileType { get; set; } = string.Empty;

    public long? FileSize { get; set; }
}
