using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface INotificationService
{
    // Notifications
    Task<NotificationFeedViewModel> GetUserNotificationsAsync(string userId, int take = 15, bool unreadOnly = false, NotificationType? filterType = null);
    Task<int> GetUnreadCountAsync(string userId);
    Task<bool> MarkAsReadAsync(int notificationId, string userId);
    Task<int> MarkAllAsReadAsync(string userId);
    Task<Notification> CreateNotificationAsync(string userId, NotificationType type, string title, string message, string? targetUrl = null);
    Task<int> CreateBulkNotificationsAsync(IEnumerable<string> userIds, NotificationType type, string title, string message, string? targetUrl = null);

    // Announcements
    Task<List<AnnouncementItemViewModel>> GetAnnouncementsForUserAsync(string userId, int take = 20, int? courseId = null);
    Task<AnnouncementOperationResult> CreateAnnouncementAsync(string authorUserId, string title, string content, AnnouncementScope scope, int? courseId = null, bool isPinned = false);
    Task<AnnouncementOperationResult> DeleteAnnouncementAsync(int announcementId, string userId);
    Task<bool> CanUserPublishAnnouncementAsync(string userId, AnnouncementScope scope, int? courseId);
}
