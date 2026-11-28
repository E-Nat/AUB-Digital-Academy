using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class LessonDiscussionReply
{
    public int Id { get; set; }

    public int ThreadId { get; set; }

    [ForeignKey(nameof(ThreadId))]
    public virtual LessonDiscussionThread Thread { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;

    [Required]
    [StringLength(4000)]
    public string Content { get; set; } = string.Empty;

    public bool IsPinned { get; set; } = false;

    public bool IsInstructorAnswer { get; set; } = false;

    public bool IsDeleted { get; set; } = false;

    public string? DeletedById { get; set; }

    [ForeignKey(nameof(DeletedById))]
    public virtual User? DeletedBy { get; set; }

    public DateTime? DeletedAt { get; set; }

    [StringLength(500)]
    public string? ModerationReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
