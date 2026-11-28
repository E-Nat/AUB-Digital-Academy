using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models;

public class Announcement
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Content { get; set; } = string.Empty;

    public AnnouncementScope Scope { get; set; } = AnnouncementScope.Global;

    public int? CourseId { get; set; }

    [ForeignKey(nameof(CourseId))]
    public virtual Course? Course { get; set; }

    [Required]
    public string CreatedById { get; set; } = string.Empty;

    [ForeignKey(nameof(CreatedById))]
    public virtual User CreatedBy { get; set; } = null!;

    public bool IsPinned { get; set; } = false;

    public bool IsArchived { get; set; } = false;

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
