using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models;

public class User : IdentityUser
{
    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(20)]
    public string? UniversityId { get; set; }

    public string? Avatar { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsTelegramVerified { get; set; } = false;

    public long? TelegramChatId { get; set; }

    [StringLength(100)]
    public string? TelegramUsername { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<Course> TaughtCourses { get; set; } = new List<Course>();
    public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public virtual ICollection<QuizSubmission> QuizSubmissions { get; set; } = new List<QuizSubmission>();
    public virtual ICollection<AssignmentSubmission> AssignmentSubmissions { get; set; } = new List<AssignmentSubmission>();
    public virtual ICollection<AssignmentSubmission> GradedSubmissions { get; set; } = new List<AssignmentSubmission>();
    public virtual ICollection<TelegramVerificationRequest> TelegramVerificationRequests { get; set; } = new List<TelegramVerificationRequest>();
    public virtual ICollection<TelegramLinkToken> TelegramLinkTokens { get; set; } = new List<TelegramLinkToken>();
    public virtual ICollection<CourseAssistant> AssistedCourses { get; set; } = new List<CourseAssistant>();
    public virtual ICollection<LessonDiscussionThread> DiscussionThreads { get; set; } = new List<LessonDiscussionThread>();
    public virtual ICollection<LessonDiscussionReply> DiscussionReplies { get; set; } = new List<LessonDiscussionReply>();
}
