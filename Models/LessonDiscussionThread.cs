using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class LessonDiscussionThread
{
    public int Id { get; set; }

    public int CourseItemId { get; set; }

    [ForeignKey(nameof(CourseItemId))]
    public virtual CourseItem CourseItem { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Content { get; set; } = string.Empty;

    public bool IsPinned { get; set; } = false;

    public bool IsAnswered { get; set; } = false;

    public bool IsDeleted { get; set; } = false;

    public string? DeletedById { get; set; }

    [ForeignKey(nameof(DeletedById))]
    public virtual User? DeletedBy { get; set; }

    public DateTime? DeletedAt { get; set; }

    [StringLength(500)]
    public string? ModerationReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<LessonDiscussionReply> Replies { get; set; } = new List<LessonDiscussionReply>();
}
