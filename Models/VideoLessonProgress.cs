using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class VideoLessonProgress
{
    public int Id { get; set; }

    [Required]
    public string StudentId { get; set; } = string.Empty;

    [ForeignKey(nameof(StudentId))]
    public virtual User Student { get; set; } = null!;

    public int CourseItemId { get; set; }

    [ForeignKey(nameof(CourseItemId))]
    public virtual CourseItem CourseItem { get; set; } = null!;

    public double QualifyingWatchedSeconds { get; set; }

    public double WatchedDurationSeconds { get; set; }

    public double LastPositionSeconds { get; set; }

    public double VideoDurationSeconds { get; set; }

    public string WatchedIntervalsJson { get; set; } = "[]";

    public bool IsCompleted { get; set; }

    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }
}
