using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models;

public class Course
{
    public int Id { get; set; }

    [Required]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Thumbnail { get; set; }

    public CourseStatus Status { get; set; } = CourseStatus.Draft;

    public int MajorId { get; set; }

    [ForeignKey(nameof(MajorId))]
    public virtual Major Major { get; set; } = null!;

    public int SemesterId { get; set; }

    [ForeignKey(nameof(SemesterId))]
    public virtual Semester Semester { get; set; } = null!;

    [Required]
    public string TeacherId { get; set; } = string.Empty;

    [ForeignKey(nameof(TeacherId))]
    public virtual User Teacher { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();
    public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public virtual ICollection<CourseAssistant> CourseAssistants { get; set; } = new List<CourseAssistant>();
}

