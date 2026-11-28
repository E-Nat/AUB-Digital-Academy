using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly IHtmlSanitizationService _htmlSanitizer;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext context,
        UserManager<User> userManager,
        IHtmlSanitizationService htmlSanitizer,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _userManager = userManager;
        _htmlSanitizer = htmlSanitizer;
        _logger = logger;
    }

    public async Task<NotificationFeedViewModel> GetUserNotificationsAsync(
        string userId,
        int take = 15,
        bool unreadOnly = false,
        NotificationType? filterType = null)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return new NotificationFeedViewModel();
        }

        var query = _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        if (filterType.HasValue)
        {
            query = query.Where(n => n.Type == filterType.Value);
        }

        var rawNotifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();

        var unreadCount = await _context.Notifications
            .CountAsync(n => n.UserId == userId && !n.IsRead);

        var list = rawNotifications.Select(n => new NotificationItemViewModel
        {
            Id = n.Id,
            Type = n.Type,
            Title = n.Title,
            Message = n.Message,
            TargetUrl = n.TargetUrl,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt
        }).ToList();

        return new NotificationFeedViewModel
        {
            UnreadCount = unreadCount,
            Notifications = list
        };
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return 0;

        return await _context.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<bool> MarkAsReadAsync(int notificationId, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification == null) return false;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return true;
    }

    public async Task<int> MarkAllAsReadAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return 0;

        return await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow));
    }

    public async Task<Notification> CreateNotificationAsync(
        string userId,
        NotificationType type,
        string title,
        string message,
        string? targetUrl = null)
    {
        var cleanTitle = _htmlSanitizer.SanitizeLessonHtml(title) ?? string.Empty;
        cleanTitle = System.Text.RegularExpressions.Regex.Replace(cleanTitle, "<.*?>", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = title.Trim();
        if (cleanTitle.Length > 200) cleanTitle = cleanTitle.Substring(0, 200);

        var cleanMessage = _htmlSanitizer.SanitizeLessonHtml(message) ?? string.Empty;
        cleanMessage = System.Text.RegularExpressions.Regex.Replace(cleanMessage, "<.*?>", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanMessage)) cleanMessage = message.Trim();
        if (cleanMessage.Length > 1000) cleanMessage = cleanMessage.Substring(0, 1000);

        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            Title = cleanTitle,
            Message = cleanMessage,
            TargetUrl = targetUrl,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        return notification;
    }

    public async Task<int> CreateBulkNotificationsAsync(
        IEnumerable<string> userIds,
        NotificationType type,
        string title,
        string message,
        string? targetUrl = null)
    {
        var distinctIds = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (distinctIds.Count == 0) return 0;

        var cleanTitle = _htmlSanitizer.SanitizeLessonHtml(title) ?? string.Empty;
        cleanTitle = System.Text.RegularExpressions.Regex.Replace(cleanTitle, "<.*?>", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = title.Trim();
        if (cleanTitle.Length > 200) cleanTitle = cleanTitle.Substring(0, 200);

        var cleanMessage = _htmlSanitizer.SanitizeLessonHtml(message) ?? string.Empty;
        cleanMessage = System.Text.RegularExpressions.Regex.Replace(cleanMessage, "<.*?>", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanMessage)) cleanMessage = message.Trim();
        if (cleanMessage.Length > 1000) cleanMessage = cleanMessage.Substring(0, 1000);

        var now = DateTime.UtcNow;
        var notifications = distinctIds.Select(uid => new Notification
        {
            UserId = uid,
            Type = type,
            Title = cleanTitle,
            Message = cleanMessage,
            TargetUrl = targetUrl,
            IsRead = false,
            CreatedAt = now
        }).ToList();

        _context.Notifications.AddRange(notifications);
        await _context.SaveChangesAsync();

        return notifications.Count;
    }

    public async Task<bool> CanUserPublishAnnouncementAsync(string userId, AnnouncementScope scope, int? courseId)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive) return false;

        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return true;

        if (scope == AnnouncementScope.Global)
        {
            // Only Admin can publish Global announcements
            return false;
        }

        if (scope == AnnouncementScope.Course)
        {
            if (!courseId.HasValue || courseId.Value <= 0) return false;

            // Teacher who owns the course can publish course announcements
            var isTeacherOwner = await _context.Courses
                .AnyAsync(c => c.Id == courseId.Value && c.TeacherId == userId);

            return isTeacherOwner;
        }

        return false;
    }

    public async Task<AnnouncementOperationResult> CreateAnnouncementAsync(
        string authorUserId,
        string title,
        string content,
        AnnouncementScope scope,
        int? courseId = null,
        bool isPinned = false)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length < 3)
        {
            return AnnouncementOperationResult.Fail("Title must be between 3 and 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length < 3)
        {
            return AnnouncementOperationResult.Fail("Content must be between 3 and 4000 characters.");
        }

        if (scope == AnnouncementScope.Course && (!courseId.HasValue || courseId.Value <= 0))
        {
            return AnnouncementOperationResult.Fail("A valid course must be selected for course-scoped announcements.");
        }

        var canPublish = await CanUserPublishAnnouncementAsync(authorUserId, scope, courseId);
        if (!canPublish)
        {
            return AnnouncementOperationResult.Fail("You do not have permission to publish this announcement.");
        }

        Course? course = null;
        if (scope == AnnouncementScope.Course)
        {
            course = await _context.Courses.FindAsync(courseId!.Value);
            if (course == null)
            {
                return AnnouncementOperationResult.Fail("Selected course not found.");
            }
        }

        var cleanTitle = _htmlSanitizer.SanitizeLessonHtml(title) ?? string.Empty;
        cleanTitle = System.Text.RegularExpressions.Regex.Replace(cleanTitle, "<.*?>", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = title.Trim();
        if (cleanTitle.Length > 200) cleanTitle = cleanTitle.Substring(0, 200);

        var cleanContent = _htmlSanitizer.SanitizeLessonHtml(content) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanContent)) cleanContent = content.Trim();

        var announcement = new Announcement
        {
            Title = cleanTitle,
            Content = cleanContent,
            Scope = scope,
            CourseId = scope == AnnouncementScope.Course ? courseId : null,
            CreatedById = authorUserId,
            IsPinned = isPinned,
            IsArchived = false,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _context.Announcements.Add(announcement);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Announcement {AnnouncementId} published by {AuthorId} with scope {Scope}.",
            announcement.Id, authorUserId, scope);

        // Dispatch notifications automatically to relevant users
        if (scope == AnnouncementScope.Course && courseId.HasValue)
        {
            var studentIds = await _context.Enrollments
                .Where(e => e.CourseId == courseId.Value && e.StudentId != authorUserId)
                .Select(e => e.StudentId)
                .ToListAsync();

            var assistantIds = await _context.CourseAssistants
                .Where(ca => ca.CourseId == courseId.Value && ca.AssistantId != authorUserId)
                .Select(ca => ca.AssistantId)
                .ToListAsync();

            var recipientIds = studentIds.Concat(assistantIds).Distinct();

            var courseCode = course?.Code ?? "Course";
            await CreateBulkNotificationsAsync(
                recipientIds,
                NotificationType.Announcement,
                $"Announcement: {cleanTitle}",
                $"{courseCode}: {cleanTitle}",
                $"/Student/Course/{courseId.Value}");
        }
        else if (scope == AnnouncementScope.Global)
        {
            var allActiveUserIds = await _context.Users
                .Where(u => u.IsActive && u.Id != authorUserId)
                .Select(u => u.Id)
                .ToListAsync();

            await CreateBulkNotificationsAsync(
                allActiveUserIds,
                NotificationType.Announcement,
                $"Notice: {cleanTitle}",
                cleanTitle,
                "/Notifications");
        }

        return AnnouncementOperationResult.Ok("Announcement published successfully.", announcement.Id);
    }

    public async Task<AnnouncementOperationResult> DeleteAnnouncementAsync(int announcementId, string userId)
    {
        var announcement = await _context.Announcements
            .Include(a => a.Course)
            .FirstOrDefaultAsync(a => a.Id == announcementId);

        if (announcement == null)
        {
            return AnnouncementOperationResult.Fail("Announcement not found.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return AnnouncementOperationResult.Fail("User not found.");

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        var isAuthor = announcement.CreatedById == userId;
        var isTeacherOwner = announcement.Course != null && announcement.Course.TeacherId == userId;

        if (!isAdmin && !isAuthor && !isTeacherOwner)
        {
            return AnnouncementOperationResult.Fail("You do not have permission to delete this announcement.");
        }

        announcement.IsArchived = true;
        announcement.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Announcement {AnnouncementId} archived by {UserId}.", announcementId, userId);

        return AnnouncementOperationResult.Ok("Announcement deleted.");
    }

    public async Task<List<AnnouncementItemViewModel>> GetAnnouncementsForUserAsync(
        string userId,
        int take = 20,
        int? courseId = null)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return new List<AnnouncementItemViewModel>();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive) return new List<AnnouncementItemViewModel>();

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        var isTeacher = await _userManager.IsInRoleAsync(user, "Teacher");
        var isAssistant = await _userManager.IsInRoleAsync(user, "Assistance");

        var query = _context.Announcements
            .Include(a => a.Course)
            .Include(a => a.CreatedBy)
            .Where(a => !a.IsArchived)
            .AsNoTracking();

        if (courseId.HasValue && courseId.Value > 0)
        {
            query = query.Where(a => a.CourseId == courseId.Value);
        }
        else
        {
            if (isAdmin)
            {
                // Admins see all announcements
            }
            else if (isTeacher)
            {
                var taughtCourseIds = await _context.Courses
                    .Where(c => c.TeacherId == userId)
                    .Select(c => c.Id)
                    .ToListAsync();

                query = query.Where(a => a.Scope == AnnouncementScope.Global ||
                    (a.Scope == AnnouncementScope.Course && a.CourseId.HasValue && taughtCourseIds.Contains(a.CourseId.Value)));
            }
            else if (isAssistant)
            {
                var assistedCourseIds = await _context.CourseAssistants
                    .Where(ca => ca.AssistantId == userId)
                    .Select(ca => ca.CourseId)
                    .ToListAsync();

                query = query.Where(a => a.Scope == AnnouncementScope.Global ||
                    (a.Scope == AnnouncementScope.Course && a.CourseId.HasValue && assistedCourseIds.Contains(a.CourseId.Value)));
            }
            else
            {
                // Students see Global announcements and announcements for courses they are actively enrolled in
                var enrolledCourseIds = await _context.Enrollments
                    .Where(e => e.StudentId == userId)
                    .Select(e => e.CourseId)
                    .ToListAsync();

                query = query.Where(a => a.Scope == AnnouncementScope.Global ||
                    (a.Scope == AnnouncementScope.Course && a.CourseId.HasValue && enrolledCourseIds.Contains(a.CourseId.Value)));
            }
        }

        var rawList = await query
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.PublishedAt)
            .Take(take)
            .ToListAsync();

        return rawList.Select(a =>
        {
            var isAuthor = a.CreatedById == userId;
            var isTeacherOwner = a.Course != null && a.Course.TeacherId == userId;
            var canDelete = isAdmin || isAuthor || isTeacherOwner;

            return new AnnouncementItemViewModel
            {
                Id = a.Id,
                Title = a.Title,
                Content = a.Content,
                Scope = a.Scope,
                CourseId = a.CourseId,
                CourseCode = a.Course?.Code,
                CourseTitle = a.Course?.Title,
                AuthorId = a.CreatedById,
                AuthorName = a.CreatedBy?.FullName ?? "Staff",
                AuthorRole = a.Scope == AnnouncementScope.Global ? "Admin" : "Teacher",
                IsPinned = a.IsPinned,
                PublishedAt = a.PublishedAt,
                CanDelete = canDelete
            };
        }).ToList();
    }
}
