using System.ComponentModel.DataAnnotations;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models.ViewModels;

public class NotificationItemViewModel
{
    public int Id { get; set; }
    public NotificationType Type { get; set; }
    public string TypeDisplay => Type switch
    {
        NotificationType.DiscussionReply => "Discussion Reply",
        NotificationType.NewLesson => "New Lesson",
        NotificationType.Announcement => "Announcement",
        NotificationType.GradeReleased => "Grade Released",
        NotificationType.CourseMilestone => "Course Milestone",
        _ => "System Notice"
    };

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TargetUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }

    public string RelativeTime
    {
        get
        {
            var diff = DateTime.UtcNow - CreatedAt;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return CreatedAt.ToString("MMM dd, yyyy");
        }
    }

    public string IconClass => Type switch
    {
        NotificationType.DiscussionReply => "bi-chat-dots-fill text-primary",
        NotificationType.NewLesson => "bi-play-circle-fill text-info",
        NotificationType.Announcement => "bi-megaphone-fill text-warning",
        NotificationType.GradeReleased => "bi-clipboard-check-fill text-success",
        NotificationType.CourseMilestone => "bi-trophy-fill text-warning",
        _ => "bi-bell-fill text-secondary"
    };

    public string BadgeBgClass => Type switch
    {
        NotificationType.DiscussionReply => "bg-primary-subtle text-primary border border-primary-subtle",
        NotificationType.NewLesson => "bg-info-subtle text-info border border-info-subtle",
        NotificationType.Announcement => "bg-warning-subtle text-dark border border-warning-subtle",
        NotificationType.GradeReleased => "bg-success-subtle text-success border border-success-subtle",
        NotificationType.CourseMilestone => "bg-warning-subtle text-dark border border-warning-subtle",
        _ => "bg-secondary-subtle text-secondary border"
    };
}

public class NotificationFeedViewModel
{
    public int UnreadCount { get; set; }
    public List<NotificationItemViewModel> Notifications { get; set; } = new();
}

public class AnnouncementItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public AnnouncementScope Scope { get; set; }
    public int? CourseId { get; set; }
    public string? CourseCode { get; set; }
    public string? CourseTitle { get; set; }
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorRole { get; set; } = "Teacher";
    public bool IsPinned { get; set; }
    public DateTime PublishedAt { get; set; }
    public bool CanDelete { get; set; }
}

public class CreateAnnouncementInputModel
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Title must be between 3 and 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Content is required.")]
    [StringLength(4000, MinimumLength = 3, ErrorMessage = "Content must be between 3 and 4000 characters.")]
    public string Content { get; set; } = string.Empty;

    public AnnouncementScope Scope { get; set; } = AnnouncementScope.Global;

    public int? CourseId { get; set; }

    public bool IsPinned { get; set; } = false;
}

public class AnnouncementOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? AnnouncementId { get; set; }

    public static AnnouncementOperationResult Ok(string message, int? id = null) => new() { Success = true, Message = message, AnnouncementId = id };
    public static AnnouncementOperationResult Fail(string message) => new() { Success = false, Message = message };
}
