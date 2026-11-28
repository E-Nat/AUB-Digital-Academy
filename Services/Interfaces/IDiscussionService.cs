using AUBDigitalAcademy.Models.ViewModels;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface IDiscussionService
{
    Task<LessonDiscussionsViewModel> GetLessonDiscussionsAsync(int courseItemId, string? currentUserId);
    Task<DiscussionThreadItemViewModel?> GetThreadDetailsAsync(int threadId, string? currentUserId);
    Task<DiscussionOperationResult> CreateThreadAsync(int courseItemId, string userId, string title, string content);
    Task<DiscussionOperationResult> CreateReplyAsync(int threadId, string userId, string content);
    Task<DiscussionOperationResult> TogglePinThreadAsync(int threadId, string userId);
    Task<DiscussionOperationResult> ToggleAnsweredThreadAsync(int threadId, string userId);
    Task<DiscussionOperationResult> TogglePinReplyAsync(int replyId, string userId);
    Task<DiscussionOperationResult> DeleteThreadAsync(int threadId, string userId, string? reason = null);
    Task<DiscussionOperationResult> DeleteReplyAsync(int replyId, string userId, string? reason = null);
    Task<bool> CanUserParticipateAsync(int courseItemId, string? userId);
    Task<bool> CanUserModerateAsync(int courseItemId, string? userId);
}
