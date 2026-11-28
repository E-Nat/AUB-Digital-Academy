using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.ViewModels;

public class DiscussionThreadItemViewModel
{
    public int Id { get; set; }
    public int CourseItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorAvatar { get; set; }
    public string AuthorRole { get; set; } = "Student";
    public DateTime CreatedAt { get; set; }
    public bool IsPinned { get; set; }
    public bool IsAnswered { get; set; }
    public bool IsDeleted { get; set; }
    public string? DeletedByName { get; set; }
    public string? ModerationReason { get; set; }
    public DateTime? DeletedAt { get; set; }
    public bool CanModerate { get; set; }
    public bool IsAuthor { get; set; }
    public int RepliesCount => Replies.Count;
    public List<DiscussionReplyItemViewModel> Replies { get; set; } = new();
}

public class DiscussionReplyItemViewModel
{
    public int Id { get; set; }
    public int ThreadId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorAvatar { get; set; }
    public string AuthorRole { get; set; } = "Student";
    public DateTime CreatedAt { get; set; }
    public bool IsPinned { get; set; }
    public bool IsInstructorAnswer { get; set; }
    public bool IsDeleted { get; set; }
    public string? DeletedByName { get; set; }
    public string? ModerationReason { get; set; }
    public DateTime? DeletedAt { get; set; }
    public bool CanModerate { get; set; }
    public bool IsAuthor { get; set; }
}

public class LessonDiscussionsViewModel
{
    public int CourseItemId { get; set; }
    public int CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string LessonTitle { get; set; } = string.Empty;
    public bool CanPostThread { get; set; }
    public bool CanReply { get; set; }
    public bool CanModerate { get; set; }
    public List<DiscussionThreadItemViewModel> Threads { get; set; } = new();
}

public class CreateThreadInputModel
{
    [Required]
    public int CourseItemId { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Title must be between 3 and 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Question content is required.")]
    [StringLength(4000, MinimumLength = 3, ErrorMessage = "Content must be between 3 and 4000 characters.")]
    public string Content { get; set; } = string.Empty;
}

public class CreateReplyInputModel
{
    [Required]
    public int ThreadId { get; set; }

    [Required(ErrorMessage = "Reply content is required.")]
    [StringLength(4000, MinimumLength = 1, ErrorMessage = "Reply must be between 1 and 4000 characters.")]
    public string Content { get; set; } = string.Empty;
}

public class ModerateDiscussionInputModel
{
    [Required]
    public int Id { get; set; }

    [StringLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
    public string? Reason { get; set; }
}

public class DiscussionOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? ThreadId { get; set; }
    public int? ReplyId { get; set; }
    public object? Data { get; set; }

    public static DiscussionOperationResult Ok(string message = "Operation succeeded.", int? threadId = null, int? replyId = null, object? data = null)
        => new() { Success = true, Message = message, ThreadId = threadId, ReplyId = replyId, Data = data };

    public static DiscussionOperationResult Fail(string message, int? threadId = null, int? replyId = null)
        => new() { Success = false, Message = message, ThreadId = threadId, ReplyId = replyId };
}
