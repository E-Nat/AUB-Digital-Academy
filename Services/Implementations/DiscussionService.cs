using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

public class DiscussionService : IDiscussionService
{
    private readonly ApplicationDbContext _context;
    private readonly IHtmlSanitizationService _htmlSanitizer;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<DiscussionService> _logger;

    public DiscussionService(
        ApplicationDbContext context,
        IHtmlSanitizationService htmlSanitizer,
        UserManager<User> userManager,
        ILogger<DiscussionService> logger)
    {
        _context = context;
        _htmlSanitizer = htmlSanitizer;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<bool> CanUserModerateAsync(int courseItemId, string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive) return false;

        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return true;

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Chapter)
            .AsNoTracking()
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId);

        if (courseItem == null || courseItem.Chapter == null) return false;

        var courseId = courseItem.Chapter.CourseId;

        // Check Teacher ownership
        var isTeacherOwner = await _context.Courses
            .AnyAsync(c => c.Id == courseId && c.TeacherId == userId);

        if (isTeacherOwner) return true;

        // Check Assigned TA
        var isAssignedAssistant = await _context.CourseAssistants
            .AnyAsync(ca => ca.CourseId == courseId && ca.AssistantId == userId);

        return isAssignedAssistant;
    }

    public async Task<bool> CanUserParticipateAsync(int courseItemId, string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        if (await CanUserModerateAsync(courseItemId, userId))
            return true;

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive) return false;

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Chapter)
            .AsNoTracking()
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId);

        if (courseItem == null || courseItem.Chapter == null) return false;

        var courseId = courseItem.Chapter.CourseId;

        // Check active enrollment
        var isEnrolled = await _context.Enrollments
            .AnyAsync(e => e.CourseId == courseId && e.StudentId == userId);

        return isEnrolled;
    }

    public async Task<LessonDiscussionsViewModel> GetLessonDiscussionsAsync(int courseItemId, string? currentUserId)
    {
        var courseItem = await _context.CourseItems
            .Include(ci => ci.Chapter)
                .ThenInclude(ch => ch.Course)
            .AsNoTracking()
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId);

        if (courseItem == null)
        {
            return new LessonDiscussionsViewModel();
        }

        var courseId = courseItem.Chapter.CourseId;
        var canModerate = await CanUserModerateAsync(courseItemId, currentUserId);
        var canParticipate = await CanUserParticipateAsync(courseItemId, currentUserId);

        var query = _context.LessonDiscussionThreads
            .Include(t => t.User)
            .Include(t => t.DeletedBy)
            .Include(t => t.Replies)
                .ThenInclude(r => r.User)
            .Include(t => t.Replies)
                .ThenInclude(r => r.DeletedBy)
            .Where(t => t.CourseItemId == courseItemId);

        if (!canModerate)
        {
            query = query.Where(t => !t.IsDeleted);
        }

        var rawThreads = await query
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        // Resolve assistant and teacher IDs for the course to determine role labels
        var teacherId = courseItem.Chapter.Course.TeacherId;
        var assistantIds = await _context.CourseAssistants
            .Where(ca => ca.CourseId == courseId)
            .Select(ca => ca.AssistantId)
            .ToListAsync();

        var adminUserIds = (await _userManager.GetUsersInRoleAsync("Admin")).Select(u => u.Id).ToHashSet();

        string ResolveRole(string authorId)
        {
            if (adminUserIds.Contains(authorId)) return "Admin";
            if (authorId == teacherId) return "Teacher";
            if (assistantIds.Contains(authorId)) return "Teaching Assistant";
            return "Student";
        }

        var threadsList = new List<DiscussionThreadItemViewModel>();

        foreach (var t in rawThreads)
        {
            var isThreadAuthor = !string.IsNullOrEmpty(currentUserId) && t.UserId == currentUserId;

            var threadVm = new DiscussionThreadItemViewModel
            {
                Id = t.Id,
                CourseItemId = t.CourseItemId,
                Title = t.Title,
                Content = t.IsDeleted && !canModerate ? "[This discussion was removed by a moderator]" : t.Content,
                AuthorId = t.UserId,
                AuthorName = t.User?.FullName ?? "Unknown",
                AuthorAvatar = t.User?.Avatar,
                AuthorRole = ResolveRole(t.UserId),
                CreatedAt = t.CreatedAt,
                IsPinned = t.IsPinned,
                IsAnswered = t.IsAnswered,
                IsDeleted = t.IsDeleted,
                DeletedByName = t.DeletedBy?.FullName,
                ModerationReason = t.ModerationReason,
                DeletedAt = t.DeletedAt,
                CanModerate = canModerate,
                IsAuthor = isThreadAuthor
            };

            var rawReplies = t.Replies.AsEnumerable();
            if (!canModerate)
            {
                rawReplies = rawReplies.Where(r => !r.IsDeleted);
            }

            threadVm.Replies = rawReplies
                .OrderByDescending(r => r.IsPinned)
                .ThenBy(r => r.CreatedAt)
                .Select(r => new DiscussionReplyItemViewModel
                {
                    Id = r.Id,
                    ThreadId = r.ThreadId,
                    Content = r.IsDeleted && !canModerate ? "[This reply was removed by a moderator]" : r.Content,
                    AuthorId = r.UserId,
                    AuthorName = r.User?.FullName ?? "Unknown",
                    AuthorAvatar = r.User?.Avatar,
                    AuthorRole = ResolveRole(r.UserId),
                    CreatedAt = r.CreatedAt,
                    IsPinned = r.IsPinned,
                    IsInstructorAnswer = r.IsInstructorAnswer,
                    IsDeleted = r.IsDeleted,
                    DeletedByName = r.DeletedBy?.FullName,
                    ModerationReason = r.ModerationReason,
                    DeletedAt = r.DeletedAt,
                    CanModerate = canModerate,
                    IsAuthor = !string.IsNullOrEmpty(currentUserId) && r.UserId == currentUserId
                }).ToList();

            threadsList.Add(threadVm);
        }

        return new LessonDiscussionsViewModel
        {
            CourseItemId = courseItemId,
            CourseId = courseId,
            CourseTitle = courseItem.Chapter.Course.Title,
            LessonTitle = courseItem.Title,
            CanPostThread = canParticipate,
            CanReply = canParticipate,
            CanModerate = canModerate,
            Threads = threadsList
        };
    }

    public async Task<DiscussionThreadItemViewModel?> GetThreadDetailsAsync(int threadId, string? currentUserId)
    {
        var thread = await _context.LessonDiscussionThreads
            .Include(t => t.User)
            .Include(t => t.DeletedBy)
            .Include(t => t.CourseItem)
                .ThenInclude(ci => ci.Chapter)
                    .ThenInclude(ch => ch.Course)
            .Include(t => t.Replies)
                .ThenInclude(r => r.User)
            .Include(t => t.Replies)
                .ThenInclude(r => r.DeletedBy)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == threadId);

        if (thread == null) return null;

        var canModerate = await CanUserModerateAsync(thread.CourseItemId, currentUserId);
        if (thread.IsDeleted && !canModerate) return null;

        var courseId = thread.CourseItem.Chapter.CourseId;
        var teacherId = thread.CourseItem.Chapter.Course.TeacherId;
        var assistantIds = await _context.CourseAssistants
            .Where(ca => ca.CourseId == courseId)
            .Select(ca => ca.AssistantId)
            .ToListAsync();
        var adminUserIds = (await _userManager.GetUsersInRoleAsync("Admin")).Select(u => u.Id).ToHashSet();

        string ResolveRole(string authorId)
        {
            if (adminUserIds.Contains(authorId)) return "Admin";
            if (authorId == teacherId) return "Teacher";
            if (assistantIds.Contains(authorId)) return "Teaching Assistant";
            return "Student";
        }

        var isAuthor = !string.IsNullOrEmpty(currentUserId) && thread.UserId == currentUserId;

        var vm = new DiscussionThreadItemViewModel
        {
            Id = thread.Id,
            CourseItemId = thread.CourseItemId,
            Title = thread.Title,
            Content = thread.IsDeleted && !canModerate ? "[This discussion was removed by a moderator]" : thread.Content,
            AuthorId = thread.UserId,
            AuthorName = thread.User?.FullName ?? "Unknown",
            AuthorAvatar = thread.User?.Avatar,
            AuthorRole = ResolveRole(thread.UserId),
            CreatedAt = thread.CreatedAt,
            IsPinned = thread.IsPinned,
            IsAnswered = thread.IsAnswered,
            IsDeleted = thread.IsDeleted,
            DeletedByName = thread.DeletedBy?.FullName,
            ModerationReason = thread.ModerationReason,
            DeletedAt = thread.DeletedAt,
            CanModerate = canModerate,
            IsAuthor = isAuthor
        };

        var rawReplies = thread.Replies.AsEnumerable();
        if (!canModerate)
        {
            rawReplies = rawReplies.Where(r => !r.IsDeleted);
        }

        vm.Replies = rawReplies
            .OrderByDescending(r => r.IsPinned)
            .ThenBy(r => r.CreatedAt)
            .Select(r => new DiscussionReplyItemViewModel
            {
                Id = r.Id,
                ThreadId = r.ThreadId,
                Content = r.IsDeleted && !canModerate ? "[This reply was removed by a moderator]" : r.Content,
                AuthorId = r.UserId,
                AuthorName = r.User?.FullName ?? "Unknown",
                AuthorAvatar = r.User?.Avatar,
                AuthorRole = ResolveRole(r.UserId),
                CreatedAt = r.CreatedAt,
                IsPinned = r.IsPinned,
                IsInstructorAnswer = r.IsInstructorAnswer,
                IsDeleted = r.IsDeleted,
                DeletedByName = r.DeletedBy?.FullName,
                ModerationReason = r.ModerationReason,
                DeletedAt = r.DeletedAt,
                CanModerate = canModerate,
                IsAuthor = !string.IsNullOrEmpty(currentUserId) && r.UserId == currentUserId
            }).ToList();

        return vm;
    }

    public async Task<DiscussionOperationResult> CreateThreadAsync(int courseItemId, string userId, string title, string content)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length < 3)
        {
            return DiscussionOperationResult.Fail("Title must be at least 3 characters.");
        }

        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length < 3)
        {
            return DiscussionOperationResult.Fail("Question content must be at least 3 characters.");
        }

        var canParticipate = await CanUserParticipateAsync(courseItemId, userId);
        if (!canParticipate)
        {
            return DiscussionOperationResult.Fail("You do not have permission to post questions in this course.");
        }

        // Sanitize title and content to prevent Stored XSS
        var cleanTitle = _htmlSanitizer.SanitizeLessonHtml(title) ?? string.Empty;
        cleanTitle = System.Text.RegularExpressions.Regex.Replace(cleanTitle, "<.*?>", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            cleanTitle = title.Trim();
        }

        if (cleanTitle.Length > 200)
        {
            cleanTitle = cleanTitle.Substring(0, 200);
        }

        var cleanContent = _htmlSanitizer.SanitizeLessonHtml(content) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanContent))
        {
            cleanContent = content.Trim();
        }

        var thread = new LessonDiscussionThread
        {
            CourseItemId = courseItemId,
            UserId = userId,
            Title = cleanTitle,
            Content = cleanContent,
            IsPinned = false,
            IsAnswered = false,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.LessonDiscussionThreads.Add(thread);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Discussion thread {ThreadId} created by user {UserId} for course item {CourseItemId}.",
            thread.Id, userId, courseItemId);

        return DiscussionOperationResult.Ok("Question posted successfully.", threadId: thread.Id);
    }

    public async Task<DiscussionOperationResult> CreateReplyAsync(int threadId, string userId, string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length < 1)
        {
            return DiscussionOperationResult.Fail("Reply content cannot be empty.");
        }

        var thread = await _context.LessonDiscussionThreads
            .Include(t => t.CourseItem)
            .FirstOrDefaultAsync(t => t.Id == threadId);

        if (thread == null)
        {
            return DiscussionOperationResult.Fail("Discussion thread not found.");
        }

        if (thread.IsDeleted)
        {
            return DiscussionOperationResult.Fail("Cannot reply to a deleted discussion thread.");
        }

        var canParticipate = await CanUserParticipateAsync(thread.CourseItemId, userId);
        if (!canParticipate)
        {
            return DiscussionOperationResult.Fail("You do not have permission to reply in this course.");
        }

        var isFacultyOrAdmin = await CanUserModerateAsync(thread.CourseItemId, userId);

        var cleanContent = _htmlSanitizer.SanitizeLessonHtml(content) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanContent))
        {
            cleanContent = content.Trim();
        }

        var reply = new LessonDiscussionReply
        {
            ThreadId = threadId,
            UserId = userId,
            Content = cleanContent,
            IsPinned = false,
            IsInstructorAnswer = isFacultyOrAdmin,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.LessonDiscussionReplies.Add(reply);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Discussion reply {ReplyId} added to thread {ThreadId} by user {UserId}.",
            reply.Id, threadId, userId);

        return DiscussionOperationResult.Ok("Reply posted successfully.", threadId: threadId, replyId: reply.Id);
    }

    public async Task<DiscussionOperationResult> TogglePinThreadAsync(int threadId, string userId)
    {
        var thread = await _context.LessonDiscussionThreads.FirstOrDefaultAsync(t => t.Id == threadId);
        if (thread == null) return DiscussionOperationResult.Fail("Discussion thread not found.");

        var canModerate = await CanUserModerateAsync(thread.CourseItemId, userId);
        if (!canModerate)
        {
            return DiscussionOperationResult.Fail("Only course faculty or administrators can pin discussion threads.");
        }

        thread.IsPinned = !thread.IsPinned;
        thread.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return DiscussionOperationResult.Ok(thread.IsPinned ? "Question pinned to top." : "Question unpinned.",
            threadId: threadId, data: new { isPinned = thread.IsPinned });
    }

    public async Task<DiscussionOperationResult> ToggleAnsweredThreadAsync(int threadId, string userId)
    {
        var thread = await _context.LessonDiscussionThreads.FirstOrDefaultAsync(t => t.Id == threadId);
        if (thread == null) return DiscussionOperationResult.Fail("Discussion thread not found.");

        var canModerate = await CanUserModerateAsync(thread.CourseItemId, userId);
        var isAuthor = thread.UserId == userId;

        if (!canModerate && !isAuthor)
        {
            return DiscussionOperationResult.Fail("Only the author or course faculty can mark a question as answered.");
        }

        thread.IsAnswered = !thread.IsAnswered;
        thread.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return DiscussionOperationResult.Ok(thread.IsAnswered ? "Question marked as answered." : "Question marked as unanswered.",
            threadId: threadId, data: new { isAnswered = thread.IsAnswered });
    }

    public async Task<DiscussionOperationResult> TogglePinReplyAsync(int replyId, string userId)
    {
        var reply = await _context.LessonDiscussionReplies
            .Include(r => r.Thread)
            .FirstOrDefaultAsync(r => r.Id == replyId);

        if (reply == null) return DiscussionOperationResult.Fail("Reply not found.");

        var canModerate = await CanUserModerateAsync(reply.Thread.CourseItemId, userId);
        if (!canModerate)
        {
            return DiscussionOperationResult.Fail("Only course faculty or administrators can pin helpful answers.");
        }

        reply.IsPinned = !reply.IsPinned;
        reply.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return DiscussionOperationResult.Ok(reply.IsPinned ? "Answer pinned as helpful." : "Answer unpinned.",
            threadId: reply.ThreadId, replyId: replyId, data: new { isPinned = reply.IsPinned });
    }

    public async Task<DiscussionOperationResult> DeleteThreadAsync(int threadId, string userId, string? reason = null)
    {
        var thread = await _context.LessonDiscussionThreads.FirstOrDefaultAsync(t => t.Id == threadId);
        if (thread == null) return DiscussionOperationResult.Fail("Discussion thread not found.");

        var canModerate = await CanUserModerateAsync(thread.CourseItemId, userId);
        var isAuthor = thread.UserId == userId;

        if (!canModerate && !isAuthor)
        {
            return DiscussionOperationResult.Fail("You do not have permission to delete this thread.");
        }

        thread.IsDeleted = true;
        thread.DeletedById = userId;
        thread.DeletedAt = DateTime.UtcNow;
        thread.ModerationReason = string.IsNullOrWhiteSpace(reason)
            ? (isAuthor ? "Deleted by author" : "Removed by moderator")
            : reason.Trim();
        thread.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Discussion thread {ThreadId} deleted by user {UserId}. Reason: {Reason}",
            threadId, userId, thread.ModerationReason);

        return DiscussionOperationResult.Ok("Discussion thread deleted.", threadId: threadId);
    }

    public async Task<DiscussionOperationResult> DeleteReplyAsync(int replyId, string userId, string? reason = null)
    {
        var reply = await _context.LessonDiscussionReplies
            .Include(r => r.Thread)
            .FirstOrDefaultAsync(r => r.Id == replyId);

        if (reply == null) return DiscussionOperationResult.Fail("Reply not found.");

        var canModerate = await CanUserModerateAsync(reply.Thread.CourseItemId, userId);
        var isAuthor = reply.UserId == userId;

        if (!canModerate && !isAuthor)
        {
            return DiscussionOperationResult.Fail("You do not have permission to delete this reply.");
        }

        reply.IsDeleted = true;
        reply.DeletedById = userId;
        reply.DeletedAt = DateTime.UtcNow;
        reply.ModerationReason = string.IsNullOrWhiteSpace(reason)
            ? (isAuthor ? "Deleted by author" : "Removed by moderator")
            : reason.Trim();
        reply.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Discussion reply {ReplyId} deleted by user {UserId}. Reason: {Reason}",
            replyId, userId, reply.ModerationReason);

        return DiscussionOperationResult.Ok("Reply deleted.", threadId: reply.ThreadId, replyId: replyId);
    }
}
