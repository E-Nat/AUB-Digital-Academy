using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

public class CurriculumService : ICurriculumService
{
    /// <summary>
    /// Short documented server-side grace window (30s) to absorb network/auto-save latency
    /// right at timer expiration without allowing meaningful post-deadline solving.
    /// </summary>
    public static readonly TimeSpan QuizGracePeriod = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ensures UTC <see cref="DateTimeKind"/> metadata is preserved without shifting ticks or applying timezone offsets.
    /// </summary>
    internal static DateTime EnsureUtcKind(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    internal static DateTime? EnsureUtcKind(DateTime? value)
        => value.HasValue ? EnsureUtcKind(value.Value) : null;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<CurriculumService> _logger;
    private readonly IAssignmentStorageService? _storageService;
    private readonly IHtmlSanitizationService _htmlSanitizer;

    public CurriculumService(
        ApplicationDbContext context,
        ILogger<CurriculumService> logger,
        IAssignmentStorageService? storageService = null,
        IHtmlSanitizationService? htmlSanitizer = null)
    {
        _context = context;
        _logger = logger;
        _storageService = storageService;
        _htmlSanitizer = htmlSanitizer ?? new HtmlSanitizationService();
    }

    public async Task<CourseCurriculumViewModel?> GetCurriculumAsync(int courseId)
    {
        if (courseId <= 0) return null;

        var course = await _context.Courses
            .AsNoTracking()
            .Include(c => c.Major)
            .Include(c => c.Semester)
            .Include(c => c.Teacher)
            .Include(c => c.Chapters.OrderBy(ch => ch.Position))
                .ThenInclude(ch => ch.CourseItems.OrderBy(ci => ci.Position))
                    .ThenInclude(ci => ci.Lesson)
            .Include(c => c.Chapters.OrderBy(ch => ch.Position))
                .ThenInclude(ch => ch.CourseItems.OrderBy(ci => ci.Position))
                    .ThenInclude(ci => ci.Book)
            .Include(c => c.Chapters.OrderBy(ch => ch.Position))
                .ThenInclude(ch => ch.CourseItems.OrderBy(ci => ci.Position))
                    .ThenInclude(ci => ci.Assignment)
            .Include(c => c.Chapters.OrderBy(ch => ch.Position))
                .ThenInclude(ch => ch.CourseItems.OrderBy(ci => ci.Position))
                    .ThenInclude(ci => ci.Quiz)
                        .ThenInclude(q => q!.Questions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (course == null)
        {
            _logger.LogWarning("GetCurriculumAsync: Course ID {CourseId} not found.", courseId);
            return null;
        }

        var allCoursePrereqs = await _context.CourseItemPrerequisites
            .Where(p => p.CourseItem.Chapter.CourseId == courseId)
            .Include(p => p.RequiredCourseItem)
            .AsNoTracking()
            .ToListAsync();

        var prereqMap = allCoursePrereqs
            .GroupBy(p => p.CourseItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var viewModel = new CourseCurriculumViewModel
        {
            CourseId = course.Id,
            CourseCode = course.Code,
            CourseTitle = course.Title,
            CourseDescription = course.Description,
            Thumbnail = course.Thumbnail,
            Status = course.Status,
            AccessMode = course.AccessMode,
            MajorName = course.Major?.Name,
            MajorCode = course.Major?.Code,
            SemesterName = course.Semester?.Name,
            TeacherName = course.Teacher?.FullName,
            Modules = course.Chapters
                .OrderBy(ch => ch.Position)
                .Select(ch => new CurriculumModuleViewModel
                {
                    Id = ch.Id,
                    CourseId = ch.CourseId,
                    Title = ch.Title,
                    Position = ch.Position,
                    Items = ch.CourseItems
                        .Where(ci => ci.ChapterId == ch.Id)
                        .OrderBy(ci => ci.Position)
                        .Select(ci => new CurriculumItemViewModel
                        {
                            Id = ci.Id,
                            ChapterId = ci.ChapterId,
                            Title = ci.Title,
                            Position = ci.Position,
                            Type = ci.Type,
                            LessonId = ci.Lesson?.Id,
                            VideoUrl = ci.Lesson?.VideoUrl,
                            ContentHtml = ci.Lesson?.ContentHtml,
                            BookId = ci.Book?.Id,
                            FileUrl = ci.Book?.FileUrl,
                            FileType = ci.Book?.FileType,
                            FileSize = ci.Book?.FileSize,
                            AssignmentId = ci.Assignment?.Id,
                            Instruction = ci.Assignment?.Instruction,
                            MaxScore = ci.Assignment?.MaxScore,
                            DueDate = ci.Assignment?.DueDate,
                            QuizId = ci.Quiz?.Id,
                            TimeLimitMinutes = ci.Quiz?.TimeLimitMinutes,
                            PassingScore = ci.Quiz?.PassingScore,
                            MaxAttempts = ci.Quiz?.MaxAttempts,
                            IsPublished = ci.Quiz?.IsPublished ?? false,
                            QuestionCount = ci.Quiz?.Questions?.Count ?? 0,
                            PrerequisiteItemIds = prereqMap.TryGetValue(ci.Id, out var pList) ? pList.Select(p => p.RequiredCourseItemId).ToList() : new(),
                            PrerequisiteItemTitles = prereqMap.TryGetValue(ci.Id, out var pList2) ? pList2.Select(p => p.RequiredCourseItem?.Title ?? $"Item #{p.RequiredCourseItemId}").ToList() : new()
                        })
                        .ToList()
                })
                .ToList(),
            ModuleCreateModel = new ModuleCreateViewModel { CourseId = course.Id },
            LessonCreateModel = new LessonCreateViewModel { CourseId = course.Id },
            BookCreateModel = new BookCreateViewModel { CourseId = course.Id },
            AssignmentCreateModel = new AssignmentCreateViewModel { CourseId = course.Id },
            QuizCreateModel = new QuizCreateViewModel { CourseId = course.Id }
        };

        return viewModel;
    }

    public async Task<IEnumerable<Chapter>> GetChaptersByCourseIdAsync(int courseId)
    {
        if (courseId <= 0) return Enumerable.Empty<Chapter>();

        return await _context.Chapters
            .AsNoTracking()
            .Where(ch => ch.CourseId == courseId)
            .OrderBy(ch => ch.Position)
            .ToListAsync();
    }

    public async Task<List<CurriculumModuleViewModel>> GetModulesByCourseIdAsync(int courseId)
    {
        if (courseId <= 0) return new List<CurriculumModuleViewModel>();

        return await _context.Chapters
            .AsNoTracking()
            .Where(ch => ch.CourseId == courseId)
            .OrderBy(ch => ch.Position)
            .Select(ch => new CurriculumModuleViewModel
            {
                Id = ch.Id,
                CourseId = ch.CourseId,
                Title = ch.Title,
                Position = ch.Position,
                Items = ch.CourseItems
                    .Where(ci => ci.ChapterId == ch.Id)
                    .OrderBy(ci => ci.Position)
                    .Select(ci => new CurriculumItemViewModel
                    {
                        Id = ci.Id,
                        ChapterId = ci.ChapterId,
                        Title = ci.Title,
                        Position = ci.Position,
                        Type = ci.Type,
                        LessonId = ci.Lesson != null ? ci.Lesson.Id : null,
                        VideoUrl = ci.Lesson != null ? ci.Lesson.VideoUrl : null,
                        ContentHtml = ci.Lesson != null ? ci.Lesson.ContentHtml : null
                    })
                    .ToList()
            })
            .ToListAsync();
    }

    public async Task<Chapter?> GetChapterByIdAsync(int chapterId)
    {
        if (chapterId <= 0) return null;
        return await _context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == chapterId);
    }

    public async Task<CurriculumOperationResult> CreateChapterAsync(int courseId, string title, int? position = null)
    {
        if (courseId <= 0)
        {
            return CurriculumOperationResult.ValidationError("A valid course ID is required.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return CurriculumOperationResult.ValidationError("Module title is required.");
        }

        var courseExists = await _context.Courses.AnyAsync(c => c.Id == courseId);
        if (!courseExists)
        {
            return CurriculumOperationResult.NotFound($"Course with ID {courseId} does not exist.");
        }

        int assignedPosition = position.HasValue && position.Value > 0
            ? position.Value
            : await GetNextChapterPositionAsync(courseId);

        var chapter = new Chapter
        {
            CourseId = courseId,
            Title = title.Trim(),
            Position = assignedPosition,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            _context.Chapters.Add(chapter);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Module '{Title}' created with ID {ChapterId} under Course {CourseId}.", chapter.Title, chapter.Id, courseId);
            return CurriculumOperationResult.Success(chapter.Id, chapter.Title, "Module created successfully.", courseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create module '{Title}' for Course {CourseId}.", title, courseId);
            return CurriculumOperationResult.Failed("An unexpected database error occurred while creating the module.");
        }
    }

    public Task<CurriculumOperationResult> CreateChapterAsync(ModuleCreateViewModel model)
    {
        if (model == null) return Task.FromResult(CurriculumOperationResult.ValidationError("Module creation payload is required."));
        return CreateChapterAsync(model.CourseId, model.Title, model.Position);
    }

    public async Task<CurriculumOperationResult> UpdateChapterAsync(int chapterId, string title, int position, int? expectedCourseId = null)
    {
        if (chapterId <= 0)
        {
            return CurriculumOperationResult.ValidationError("Invalid module ID.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return CurriculumOperationResult.ValidationError("Module title is required.");
        }

        var chapter = await _context.Chapters.FindAsync(chapterId);
        if (chapter == null)
        {
            return CurriculumOperationResult.NotFound("Module not found.");
        }

        // Ownership validation: verify chapter belongs to expectedCourseId if specified
        if (expectedCourseId.HasValue && chapter.CourseId != expectedCourseId.Value)
        {
            _logger.LogWarning("Module ownership mismatch: Module {ChapterId} (Course {Actual}) does not belong to expected Course {Expected}.",
                chapterId, chapter.CourseId, expectedCourseId.Value);
            return CurriculumOperationResult.ValidationError($"Module ID {chapterId} does not belong to Course ID {expectedCourseId.Value}.");
        }

        chapter.Title = title.Trim();
        if (position > 0)
        {
            chapter.Position = position;
        }
        chapter.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Module ID {ChapterId} updated successfully.", chapter.Id);
            return CurriculumOperationResult.Success(chapter.Id, chapter.Title, "Module updated successfully.", chapter.CourseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update module ID {ChapterId}.", chapterId);
            return CurriculumOperationResult.Failed("An unexpected error occurred while updating the module.");
        }
    }

    public Task<CurriculumOperationResult> UpdateChapterAsync(ModuleEditViewModel model)
    {
        if (model == null) return Task.FromResult(CurriculumOperationResult.ValidationError("Module update payload is required."));
        return UpdateChapterAsync(model.Id, model.Title, model.Position, model.CourseId > 0 ? model.CourseId : null);
    }

    public async Task<CurriculumOperationResult> DeleteChapterAsync(int chapterId, int? expectedCourseId = null)
    {
        if (chapterId <= 0)
        {
            return CurriculumOperationResult.ValidationError("Invalid module ID.");
        }

        var chapter = await _context.Chapters
            .Include(ch => ch.CourseItems)
            .FirstOrDefaultAsync(ch => ch.Id == chapterId);

        if (chapter == null)
        {
            return CurriculumOperationResult.NotFound("Module not found.");
        }

        // Ownership validation: verify chapter belongs to expectedCourseId if specified
        if (expectedCourseId.HasValue && chapter.CourseId != expectedCourseId.Value)
        {
            _logger.LogWarning("Module delete ownership mismatch: Module {ChapterId} (Course {Actual}) does not belong to expected Course {Expected}.",
                chapterId, chapter.CourseId, expectedCourseId.Value);
            return CurriculumOperationResult.ValidationError($"Module ID {chapterId} does not belong to Course ID {expectedCourseId.Value}.");
        }

        // Check for student learning activity before safe cascade deletion
        var progressCount = await _context.CourseProgresses
            .CountAsync(cp => cp.CourseItem.ChapterId == chapterId);

        var quizSubCount = await _context.QuizSubmissions
            .CountAsync(qs => qs.Quiz.CourseItem.ChapterId == chapterId);

        var quizAttemptCount = await _context.QuizAttempts
            .CountAsync(qa => qa.Quiz.CourseItem.ChapterId == chapterId);

        var assignmentSubCount = await _context.AssignmentSubmissions
            .CountAsync(asub => asub.Assignment.CourseItem.ChapterId == chapterId);

        int totalQuizRecords = Math.Max(quizSubCount, quizAttemptCount);
        int totalStudentData = progressCount + totalQuizRecords + assignmentSubCount;
        if (totalStudentData > 0)
        {
            var warning = $"Cannot delete module '{chapter.Title}' because it contains active student learning records ({progressCount} progress records, {totalQuizRecords + assignmentSubCount} submissions).";
            _logger.LogWarning("Safe delete blocked for module ID {ChapterId}: {Warning}", chapterId, warning);
            return CurriculumOperationResult.BlockedByStudentData(
                chapter.Id,
                chapter.Title,
                warning,
                progressCount,
                totalQuizRecords + assignmentSubCount);
        }

        try
        {
            _context.Chapters.Remove(chapter);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Module ID {ChapterId} ('{Title}') and its syllabus items were safely deleted.", chapterId, chapter.Title);
            return CurriculumOperationResult.Success(chapterId, chapter.Title, "Module deleted successfully.", chapter.CourseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete module ID {ChapterId}.", chapterId);
            return CurriculumOperationResult.Failed("An unexpected database error occurred while deleting the module.");
        }
    }

    public async Task<IEnumerable<CurriculumItemViewModel>?> GetLessonsByChapterIdAsync(int chapterId)
    {
        if (chapterId <= 0) return null;

        var chapterExists = await _context.Chapters.AnyAsync(ch => ch.Id == chapterId);
        if (!chapterExists)
        {
            _logger.LogWarning("GetLessonsByChapterIdAsync: Module ID {ChapterId} does not exist.", chapterId);
            return null;
        }

        return await _context.CourseItems
            .AsNoTracking()
            .Where(ci => ci.ChapterId == chapterId && ci.Type == ItemType.Lesson)
            .OrderBy(ci => ci.Position)
            .Select(ci => new CurriculumItemViewModel
            {
                Id = ci.Id,
                ChapterId = ci.ChapterId,
                Title = ci.Title,
                Position = ci.Position,
                Type = ci.Type,
                LessonId = ci.Lesson != null ? ci.Lesson.Id : (int?)null,
                VideoUrl = ci.Lesson != null ? ci.Lesson.VideoUrl : null,
                ContentHtml = ci.Lesson != null ? ci.Lesson.ContentHtml : null
            })
            .ToListAsync();
    }

    public Task<IEnumerable<CurriculumItemViewModel>?> GetLessonsByModuleAsync(int moduleId) =>
        GetLessonsByChapterIdAsync(moduleId);

    // Get Lesson
    public async Task<LessonEditViewModel?> GetLessonAsync(int courseItemId, int? expectedChapterId = null, int? expectedCourseId = null)
    {
        if (courseItemId <= 0) return null;

        var item = await _context.CourseItems
            .Include(ci => ci.Lesson)
            .Include(ci => ci.Chapter)
            .AsNoTracking()
            .FirstOrDefaultAsync(ci => (ci.Id == courseItemId || (ci.Lesson != null && ci.Lesson.Id == courseItemId)) && ci.Type == ItemType.Lesson);

        if (item == null) return null;

        if (expectedChapterId.HasValue && item.ChapterId != expectedChapterId.Value)
        {
            _logger.LogWarning("GetLessonAsync ownership mismatch: CourseItemId {CourseItemId} belongs to Chapter {ActualChapterId}, expected {ExpectedChapterId}.",
                item.Id, item.ChapterId, expectedChapterId.Value);
            return null;
        }

        if (expectedCourseId.HasValue && item.Chapter?.CourseId != expectedCourseId.Value)
        {
            _logger.LogWarning("GetLessonAsync ownership mismatch: CourseItemId {CourseItemId} belongs to Course {ActualCourseId}, expected {ExpectedCourseId}.",
                item.Id, item.Chapter?.CourseId, expectedCourseId.Value);
            return null;
        }

        var prereqs = await _context.CourseItemPrerequisites
            .Where(p => p.CourseItemId == item.Id)
            .Select(p => p.RequiredCourseItemId)
            .ToListAsync();

        return new LessonEditViewModel
        {
            CourseItemId = item.Id,
            LessonId = item.Lesson?.Id ?? 0,
            ChapterId = item.ChapterId,
            CourseId = item.Chapter?.CourseId ?? 0,
            Title = item.Title,
            Position = item.Position,
            VideoUrl = item.Lesson?.VideoUrl,
            ContentHtml = item.Lesson?.ContentHtml,
            PrerequisiteItemIds = prereqs
        };
    }

    public Task<LessonEditViewModel?> GetLessonByIdAsync(int lessonId, int? expectedChapterId = null, int? expectedCourseId = null) =>
        GetLessonAsync(lessonId, expectedChapterId, expectedCourseId);

    public async Task<CurriculumOperationResult> CreateLessonAsync(
        int chapterId,
        string title,
        string? contentHtml,
        string? videoUrl,
        int? position = null,
        int? expectedCourseId = null)
    {
        if (chapterId <= 0)
        {
            return CurriculumOperationResult.ValidationError("Parent module selection is required.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return CurriculumOperationResult.ValidationError("Lesson title is required.");
        }

        var chapter = await _context.Chapters.FindAsync(chapterId);
        if (chapter == null)
        {
            return CurriculumOperationResult.NotFound("Parent module does not exist.");
        }

        if (expectedCourseId.HasValue && chapter.CourseId != expectedCourseId.Value)
        {
            return CurriculumOperationResult.ValidationError("Parent module does not belong to the specified course.");
        }

        string? sanitizedVideoUrl = null;
        if (!string.IsNullOrWhiteSpace(videoUrl))
        {
            sanitizedVideoUrl = _htmlSanitizer.SanitizeResourceUrl(videoUrl);
            if (sanitizedVideoUrl == null)
            {
                return CurriculumOperationResult.ValidationError("Invalid video URL. Only http://, https://, or safe relative paths are permitted.");
            }
        }

        var sanitizedContentHtml = _htmlSanitizer.SanitizeLessonHtml(contentHtml);

        int assignedPosition = position.HasValue && position.Value > 0
            ? position.Value
            : await GetNextItemPositionAsync(chapterId);

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Create generic container CourseItem
            var courseItem = new CourseItem
            {
                ChapterId = chapterId,
                Title = title.Trim(),
                Position = assignedPosition,
                Type = ItemType.Lesson
            };
            _context.CourseItems.Add(courseItem);
            await _context.SaveChangesAsync();

            // 2. Create 1:1 child Lesson entity
            var lesson = new Lesson
            {
                CourseItemId = courseItem.Id,
                ContentHtml = sanitizedContentHtml,
                VideoUrl = sanitizedVideoUrl
            };
            _context.Lessons.Add(lesson);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation("Lesson '{Title}' created with CourseItemID {CourseItemId} and LessonID {LessonId}.",
                courseItem.Title, courseItem.Id, lesson.Id);

            // Notify enrolled students of the new lesson
            try
            {
                var enrolledStudentIds = await _context.Enrollments
                    .Where(e => e.CourseId == chapter.CourseId)
                    .Select(e => e.StudentId)
                    .ToListAsync();

                if (enrolledStudentIds.Any())
                {
                    var now = DateTime.UtcNow;
                    var notifs = enrolledStudentIds.Select(sid => new Notification
                    {
                        UserId = sid,
                        Type = NotificationType.NewLesson,
                        Title = $"New Lesson: {courseItem.Title}",
                        Message = $"A new lesson has been added to {chapter.Course?.Title ?? "your course"}.",
                        TargetUrl = $"/Student/Course/{chapter.CourseId}/Lesson/{courseItem.Id}",
                        IsRead = false,
                        CreatedAt = now
                    });
                    _context.Notifications.AddRange(notifs);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception notifEx)
            {
                _logger.LogWarning(notifEx, "Failed to dispatch new lesson notifications for CourseItem {CourseItemId}.", courseItem.Id);
            }

            return CurriculumOperationResult.Success(courseItem.Id, courseItem.Title, "Lesson created successfully.", chapterId, lesson.Id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to create lesson '{Title}' under Chapter {ChapterId}.", title, chapterId);
            return CurriculumOperationResult.Failed("An unexpected database error occurred while creating the lesson.");
        }
    }

    public async Task<CurriculumOperationResult> CreateLessonAsync(LessonCreateViewModel model)
    {
        if (model == null) return CurriculumOperationResult.ValidationError("Lesson creation payload is required.");
        var result = await CreateLessonAsync(
            model.ChapterId,
            model.Title,
            model.ContentHtml,
            model.VideoUrl,
            model.Position,
            model.CourseId > 0 ? model.CourseId : null);

        if (result.IsSuccess && result.EntityId.HasValue && model.PrerequisiteItemIds != null && model.PrerequisiteItemIds.Any())
        {
            var chapter = await _context.Chapters.AsNoTracking().FirstOrDefaultAsync(ch => ch.Id == model.ChapterId);
            if (chapter != null)
            {
                await SetItemPrerequisitesInternalAsync(result.EntityId.Value, model.PrerequisiteItemIds, chapter.CourseId);
            }
        }

        return result;
    }

    public async Task<CurriculumOperationResult> UpdateLessonAsync(
        int courseItemId,
        string title,
        string? contentHtml,
        string? videoUrl,
        int position,
        int? expectedChapterId = null,
        int? expectedCourseId = null)
    {
        if (courseItemId <= 0)
        {
            return CurriculumOperationResult.ValidationError("Invalid course item ID.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return CurriculumOperationResult.ValidationError("Lesson title is required.");
        }

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Lesson)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => (ci.Id == courseItemId || (ci.Lesson != null && ci.Lesson.Id == courseItemId)) && ci.Type == ItemType.Lesson);

        if (courseItem == null)
        {
            return CurriculumOperationResult.NotFound("Lesson not found.");
        }

        // Validate Module ownership
        if (expectedChapterId.HasValue && courseItem.ChapterId != expectedChapterId.Value)
        {
            var msg = "Ownership mismatch: This lesson does not belong to the specified module.";
            _logger.LogWarning("UpdateLessonAsync: {Message} CourseItemId={CourseItemId}, Actual={Actual}, Expected={Expected}",
                msg, courseItem.Id, courseItem.ChapterId, expectedChapterId.Value);
            return CurriculumOperationResult.ValidationError(msg);
        }

        // Validate Course ownership
        if (expectedCourseId.HasValue && courseItem.Chapter?.CourseId != expectedCourseId.Value)
        {
            var msg = "Ownership mismatch: This lesson does not belong to the specified course.";
            _logger.LogWarning("UpdateLessonAsync: {Message} CourseItemId={CourseItemId}, Actual={Actual}, Expected={Expected}",
                msg, courseItem.Id, courseItem.Chapter?.CourseId, expectedCourseId.Value);
            return CurriculumOperationResult.ValidationError(msg);
        }

        string? sanitizedVideoUrl = null;
        if (!string.IsNullOrWhiteSpace(videoUrl))
        {
            sanitizedVideoUrl = _htmlSanitizer.SanitizeResourceUrl(videoUrl);
            if (sanitizedVideoUrl == null)
            {
                return CurriculumOperationResult.ValidationError("Invalid video URL. Only http://, https://, or safe relative paths are permitted.");
            }
        }

        var sanitizedContentHtml = _htmlSanitizer.SanitizeLessonHtml(contentHtml);

        courseItem.Title = title.Trim();
        if (position > 0)
        {
            courseItem.Position = position;
        }

        if (courseItem.Lesson == null)
        {
            // Fallback recovery if 1:1 child lesson row was missing
            courseItem.Lesson = new Lesson
            {
                CourseItemId = courseItem.Id,
                ContentHtml = sanitizedContentHtml,
                VideoUrl = sanitizedVideoUrl
            };
            _context.Lessons.Add(courseItem.Lesson);
        }
        else
        {
            courseItem.Lesson.ContentHtml = sanitizedContentHtml;
            courseItem.Lesson.VideoUrl = sanitizedVideoUrl;
        }

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Lesson ID {CourseItemId} ('{Title}') updated successfully.", courseItem.Id, courseItem.Title);
            return CurriculumOperationResult.Success(courseItem.Id, courseItem.Title, "Lesson updated successfully.", courseItem.ChapterId, courseItem.Lesson?.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update lesson ID {CourseItemId}.", courseItem.Id);
            return CurriculumOperationResult.Failed("An unexpected database error occurred while updating the lesson.");
        }
    }

    public async Task<CurriculumOperationResult> UpdateLessonAsync(LessonEditViewModel model)
    {
        if (model == null) return CurriculumOperationResult.ValidationError("Lesson update payload is required.");
        int targetId = model.CourseItemId > 0 ? model.CourseItemId : model.LessonId;
        var result = await UpdateLessonAsync(
            targetId,
            model.Title,
            model.ContentHtml,
            model.VideoUrl,
            model.Position,
            model.ChapterId > 0 ? model.ChapterId : null,
            model.CourseId > 0 ? model.CourseId : null);

        if (result.IsSuccess && model.PrerequisiteItemIds != null)
        {
            var item = await _context.CourseItems.Include(ci => ci.Chapter).AsNoTracking().FirstOrDefaultAsync(ci => ci.Id == targetId);
            if (item != null && item.Chapter != null)
            {
                await SetItemPrerequisitesInternalAsync(targetId, model.PrerequisiteItemIds, item.Chapter.CourseId);
            }
        }

        return result;
    }

    public async Task<CurriculumOperationResult> DeleteLessonAsync(
        int courseItemId,
        int? expectedChapterId = null,
        int? expectedCourseId = null)
    {
        if (courseItemId <= 0)
        {
            return CurriculumOperationResult.ValidationError("Invalid course item ID.");
        }

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Lesson)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => (ci.Id == courseItemId || (ci.Lesson != null && ci.Lesson.Id == courseItemId)) && ci.Type == ItemType.Lesson);

        if (courseItem == null)
        {
            return CurriculumOperationResult.NotFound("Lesson not found.");
        }

        // Validate Module ownership
        if (expectedChapterId.HasValue && courseItem.ChapterId != expectedChapterId.Value)
        {
            var msg = "Ownership mismatch: This lesson does not belong to the specified module.";
            _logger.LogWarning("DeleteLessonAsync: {Message} CourseItemId={CourseItemId}, Actual={Actual}, Expected={Expected}",
                msg, courseItem.Id, courseItem.ChapterId, expectedChapterId.Value);
            return CurriculumOperationResult.ValidationError(msg);
        }

        // Validate Course ownership
        if (expectedCourseId.HasValue && courseItem.Chapter?.CourseId != expectedCourseId.Value)
        {
            var msg = "Ownership mismatch: This lesson does not belong to the specified course.";
            _logger.LogWarning("DeleteLessonAsync: {Message} CourseItemId={CourseItemId}, Actual={Actual}, Expected={Expected}",
                msg, courseItem.Id, courseItem.Chapter?.CourseId, expectedCourseId.Value);
            return CurriculumOperationResult.ValidationError(msg);
        }

        var progressCount = await _context.CourseProgresses
            .CountAsync(cp => cp.CourseItemId == courseItem.Id);

        if (progressCount > 0)
        {
            var warning = $"Cannot delete lesson '{courseItem.Title}' because {progressCount} student(s) have recorded progress on this item.";
            _logger.LogWarning("Safe delete blocked for lesson ID {CourseItemId}: {Warning}", courseItem.Id, warning);
            return CurriculumOperationResult.BlockedByStudentData(
                courseItem.Id,
                courseItem.Title,
                warning,
                progressCount: progressCount);
        }

        try
        {
            _context.CourseItems.Remove(courseItem);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Lesson ID {CourseItemId} ('{Title}') deleted cleanly.", courseItem.Id, courseItem.Title);
            return CurriculumOperationResult.Success(courseItem.Id, courseItem.Title, "Lesson deleted successfully.", courseItem.ChapterId, courseItem.Lesson?.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete lesson ID {CourseItemId}.", courseItem.Id);
            return CurriculumOperationResult.Failed("An unexpected database error occurred while deleting the lesson.");
        }
    }

    public async Task<int> GetNextChapterPositionAsync(int courseId)
    {
        var maxPos = await _context.Chapters
            .Where(ch => ch.CourseId == courseId)
            .MaxAsync(ch => (int?)ch.Position) ?? 0;

        return maxPos + 1;
    }

    public async Task<int> GetNextItemPositionAsync(int chapterId)
    {
        var maxPos = await _context.CourseItems
            .Where(ci => ci.ChapterId == chapterId)
            .MaxAsync(ci => (int?)ci.Position) ?? 0;

        return maxPos + 1;
    }

    public async Task<StudentLessonPlayerViewModel?> GetStudentLessonPlayerAsync(int courseId, int lessonId, string? studentId = null, bool allowAdminPreview = false)
    {
        if (courseId <= 0 || lessonId <= 0) return null;

        var course = await _context.Courses
            .AsNoTracking()
            .Include(c => c.Teacher)
            .Include(c => c.Chapters.OrderBy(ch => ch.Position).ThenBy(ch => ch.Id))
                .ThenInclude(ch => ch.CourseItems.OrderBy(ci => ci.Position).ThenBy(ci => ci.Id))
                    .ThenInclude(ci => ci.Lesson)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Quiz)
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (course == null)
        {
            _logger.LogWarning("GetStudentLessonPlayerAsync: Course ID {CourseId} not found.", courseId);
            return null;
        }

        // Flatten all lessons in deterministic curriculum order:
        // 1. Chapter Position ASC, Chapter Id ASC
        // 2. CourseItem Position ASC, CourseItem Id ASC (where Type == Lesson and Lesson != null)
        var orderedLessons = course.Chapters
            .OrderBy(ch => ch.Position)
            .ThenBy(ch => ch.Id)
            .SelectMany(ch => ch.CourseItems
                .Where(ci => ci.Type == ItemType.Lesson && ci.Lesson != null)
                .OrderBy(ci => ci.Position)
                .ThenBy(ci => ci.Id)
                .Select(ci => new
                {
                    Chapter = ch,
                    CourseItem = ci,
                    Lesson = ci.Lesson!
                }))
            .ToList();

        // Locate current lesson by CourseItemId OR Lesson.Id
        var currentIndex = orderedLessons.FindIndex(x => x.CourseItem.Id == lessonId || x.Lesson.Id == lessonId);
        if (currentIndex == -1)
        {
            // Lesson does not exist in this course (ownership mismatch or nonexistent)
            _logger.LogWarning("GetStudentLessonPlayerAsync: Lesson/CourseItem ID {LessonId} does not belong to Course ID {CourseId}.", lessonId, courseId);
            return null;
        }

        var current = orderedLessons[currentIndex];
        var previous = currentIndex > 0 ? orderedLessons[currentIndex - 1] : null;
        var next = currentIndex < orderedLessons.Count - 1 ? orderedLessons[currentIndex + 1] : null;

        // Query real student completion data from SQL Server if studentId is supplied
        HashSet<int> completedItemIds = new();
        HashSet<int> completedLessonIds = new();
        double lastVideoPosition = 0;
        int videoQualifyingPct = 0;
        if (!string.IsNullOrWhiteSpace(studentId))
        {
            var completedRows = await _context.CourseProgresses
                .AsNoTracking()
                .Where(cp => cp.StudentId == studentId && cp.IsCompleted && cp.CourseItem.Chapter.CourseId == courseId)
                .Select(cp => new { cp.CourseItemId, cp.CourseItem.Type })
                .ToListAsync();
            completedItemIds = completedRows.Select(x => x.CourseItemId).ToHashSet();
            completedLessonIds = completedRows.Where(x => x.Type == ItemType.Lesson).Select(x => x.CourseItemId).ToHashSet();

            var videoProg = await _context.VideoLessonProgresses
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.StudentId == studentId && v.CourseItemId == current.CourseItem.Id);
            if (videoProg != null)
            {
                lastVideoPosition = videoProg.LastPositionSeconds;
                videoQualifyingPct = videoProg.VideoDurationSeconds > 0
                    ? Math.Min(100, (int)Math.Round((videoProg.QualifyingWatchedSeconds / videoProg.VideoDurationSeconds) * 100.0))
                    : (videoProg.IsCompleted ? 100 : 0);
            }
        }

        Dictionary<int, List<CourseItemPrerequisite>> prereqMap = new();
        if (course.AccessMode == CourseAccessMode.Sequential)
        {
            var allPrereqs = await _context.CourseItemPrerequisites
                .Where(p => p.CourseItem.Chapter.CourseId == courseId)
                .Include(p => p.RequiredCourseItem)
                .AsNoTracking()
                .ToListAsync();
            prereqMap = allPrereqs.GroupBy(p => p.CourseItemId).ToDictionary(g => g.Key, g => g.ToList());
        }

        var (currentAccessible, currentMissing) = string.IsNullOrWhiteSpace(studentId)
            ? (true, new List<string>())
            : await CheckItemAccessAsync(current.CourseItem.Id, studentId, allowAdminPreview);

        var modules = course.Chapters
            .OrderBy(ch => ch.Position)
            .ThenBy(ch => ch.Id)
            .Select(ch => new StudentPlayerModuleViewModel
            {
                ModuleId = ch.Id,
                Title = ch.Title,
                Position = ch.Position,
                IsCurrentModule = ch.Id == current.Chapter.Id,
                Lessons = ch.CourseItems
                    .Where(ci => (ci.Type == ItemType.Lesson && ci.Lesson != null) || (ci.Type == ItemType.Quiz && ci.Quiz != null && (ci.Quiz.IsPublished || allowAdminPreview)))
                    .OrderBy(ci => ci.Position)
                    .ThenBy(ci => ci.Id)
                    .Select(ci =>
                    {
                        bool isLocked = false;
                        List<string> itemMissing = new();
                        if (!allowAdminPreview && course.AccessMode == CourseAccessMode.Sequential && prereqMap.TryGetValue(ci.Id, out var pList))
                        {
                            foreach (var p in pList)
                            {
                                if (!completedItemIds.Contains(p.RequiredCourseItemId))
                                {
                                    isLocked = true;
                                    itemMissing.Add(p.RequiredCourseItem?.Title ?? $"Item #{p.RequiredCourseItemId}");
                                }
                            }
                        }

                        return new StudentPlayerLessonItemViewModel
                        {
                            CourseItemId = ci.Id,
                            LessonId = ci.Lesson?.Id ?? 0,
                            QuizId = ci.Quiz?.Id,
                            Type = ci.Type,
                            Title = ci.Title,
                            Position = ci.Position,
                            HasVideo = ci.Lesson != null && !string.IsNullOrWhiteSpace(ci.Lesson.VideoUrl),
                            HasContent = ci.Lesson != null && !string.IsNullOrWhiteSpace(ci.Lesson.ContentHtml),
                            IsCurrent = ci.Id == current.CourseItem.Id,
                            IsCompleted = completedItemIds.Contains(ci.Id),
                            IsLocked = isLocked,
                            MissingPrerequisites = itemMissing
                        };
                    }).ToList()
            }).ToList();

        return new StudentLessonPlayerViewModel
        {
            CourseId = course.Id,
            CourseCode = course.Code,
            CourseTitle = course.Title,
            TeacherName = course.Teacher?.FullName ?? "Instructor",
            ModuleId = current.Chapter.Id,
            ModuleTitle = current.Chapter.Title,
            ModulePosition = current.Chapter.Position,
            CourseItemId = current.CourseItem.Id,
            LessonId = current.Lesson.Id,
            LessonTitle = current.CourseItem.Title,
            LessonPosition = current.CourseItem.Position,
            ContentHtml = _htmlSanitizer.SanitizeLessonHtml(current.Lesson.ContentHtml),
            VideoUrl = _htmlSanitizer.SanitizeResourceUrl(current.Lesson.VideoUrl),
            IsCompleted = completedItemIds.Contains(current.CourseItem.Id),
            IsLocked = !currentAccessible,
            MissingPrerequisites = currentMissing,
            AccessMode = course.AccessMode,
            LastVideoPositionSeconds = lastVideoPosition,
            VideoQualifyingPercentage = videoQualifyingPct,
            CurrentLessonIndex = currentIndex + 1,
            TotalLessonsCount = orderedLessons.Count,
            CompletedLessonsCount = completedLessonIds.Count,
            PreviousLesson = previous == null ? null : new StudentLessonNavigationItemViewModel
            {
                CourseItemId = previous.CourseItem.Id,
                LessonId = previous.Lesson.Id,
                Title = previous.CourseItem.Title,
                ModulePosition = previous.Chapter.Position,
                LessonPosition = previous.CourseItem.Position
            },
            NextLesson = next == null ? null : new StudentLessonNavigationItemViewModel
            {
                CourseItemId = next.CourseItem.Id,
                LessonId = next.Lesson.Id,
                Title = next.CourseItem.Title,
                ModulePosition = next.Chapter.Position,
                LessonPosition = next.CourseItem.Position
            },
            Modules = modules
        };
    }

    public async Task<int?> GetFirstLessonIdAsync(int courseId)
    {
        if (courseId <= 0) return null;

        var course = await _context.Courses
            .AsNoTracking()
            .Include(c => c.Chapters.OrderBy(ch => ch.Position).ThenBy(ch => ch.Id))
                .ThenInclude(ch => ch.CourseItems.OrderBy(ci => ci.Position).ThenBy(ci => ci.Id))
                    .ThenInclude(ci => ci.Lesson)
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (course == null) return null;

        var firstLesson = course.Chapters
            .OrderBy(ch => ch.Position)
            .ThenBy(ch => ch.Id)
            .SelectMany(ch => ch.CourseItems
                .Where(ci => ci.Type == ItemType.Lesson && ci.Lesson != null)
                .OrderBy(ci => ci.Position)
                .ThenBy(ci => ci.Id))
            .FirstOrDefault();

        return firstLesson?.Id;
    }

    public async Task<CurriculumOperationResult> MarkLessonCompleteAsync(string studentId, int courseId, int courseItemId)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return CurriculumOperationResult.ValidationError("Student ID is required.");
        }

        if (courseId <= 0 || courseItemId <= 0)
        {
            return CurriculumOperationResult.ValidationError("Invalid course ID or lesson ID.");
        }

        // Verify lesson exists and belongs to a chapter in the specified course
        var courseItem = await _context.CourseItems
            .Include(ci => ci.Chapter)
            .Include(ci => ci.Lesson)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId);

        if (courseItem == null || courseItem.Chapter == null)
        {
            _logger.LogWarning("MarkLessonCompleteAsync: CourseItem ID {CourseItemId} not found.", courseItemId);
            return CurriculumOperationResult.NotFound("Lesson not found.");
        }

        if (courseItem.Chapter.CourseId != courseId)
        {
            _logger.LogWarning("MarkLessonCompleteAsync: CourseItem ID {CourseItemId} belongs to Course {ActualCourseId}, not requested Course {RequestedCourseId}.",
                courseItemId, courseItem.Chapter.CourseId, courseId);
            return CurriculumOperationResult.ValidationError("Lesson does not belong to the specified course.");
        }

        // Check prerequisites in Sequential access mode
        var (isAccessible, missingPrereqs) = await CheckItemAccessAsync(courseItemId, studentId);
        if (!isAccessible)
        {
            _logger.LogWarning("MarkLessonCompleteAsync: Student {StudentId} blocked by prerequisites for item {CourseItemId}: {Missing}",
                studentId, courseItemId, string.Join(", ", missingPrereqs));
            return CurriculumOperationResult.ValidationError($"Prerequisites not met. Please complete: {string.Join(", ", missingPrereqs)}.");
        }

        // Idempotent duplicate check: Check if CourseProgress already exists for this student and CourseItem
        var existingProgress = await _context.CourseProgresses
            .FirstOrDefaultAsync(cp => cp.StudentId == studentId && cp.CourseItemId == courseItemId);

        if (existingProgress != null)
        {
            _logger.LogInformation("MarkLessonCompleteAsync: Lesson ID {CourseItemId} is already completed by student {StudentId}.", courseItemId, studentId);
            return CurriculumOperationResult.Success(courseItemId, courseItem.Title, "Lesson is already marked as complete.");
        }

        // Gate video lessons: require >= 90% qualifying watched playback before completion
        if (courseItem.Type == ItemType.Lesson && courseItem.Lesson != null && !string.IsNullOrWhiteSpace(courseItem.Lesson.VideoUrl))
        {
            var videoProgress = await _context.VideoLessonProgresses
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.StudentId == studentId && v.CourseItemId == courseItemId);
            bool videoCompleted = videoProgress != null &&
                                  (videoProgress.IsCompleted ||
                                   (videoProgress.VideoDurationSeconds > 0 &&
                                    (videoProgress.QualifyingWatchedSeconds / videoProgress.VideoDurationSeconds) >= 0.90));
            if (!videoCompleted)
            {
                return CurriculumOperationResult.ValidationError("Please watch at least 90% of the lesson video before marking this lesson as complete.");
            }
        }

        // Create new completion record in SQL Server
        var progress = new CourseProgress
        {
            StudentId = studentId,
            CourseItemId = courseItemId,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        };

        _context.CourseProgresses.Add(progress);

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("MarkLessonCompleteAsync: Successfully recorded completion for student {StudentId}, Lesson ID {CourseItemId}.", studentId, courseItemId);

            // Check course milestone (100% completion)
            try
            {
                var totalLessonsInCourse = await _context.CourseItems
                    .CountAsync(ci => ci.Chapter.CourseId == courseId && ci.Type == ItemType.Lesson);

                var completedLessonsInCourse = await _context.CourseProgresses
                    .CountAsync(cp => cp.StudentId == studentId && cp.CourseItem.Chapter.CourseId == courseId && cp.CourseItem.Type == ItemType.Lesson);

                if (totalLessonsInCourse > 0 && completedLessonsInCourse >= totalLessonsInCourse)
                {
                    var alreadyNotified = await _context.Notifications.AnyAsync(n =>
                        n.UserId == studentId &&
                        n.Type == NotificationType.CourseMilestone &&
                        n.TargetUrl == $"/Student/Course/{courseId}");

                    if (!alreadyNotified)
                    {
                        var courseObj = await _context.Courses.FindAsync(courseId);
                        var milestoneNotif = new Notification
                        {
                            UserId = studentId,
                            Type = NotificationType.CourseMilestone,
                            Title = "Course Milestone: 100% Completed!",
                            Message = $"Congratulations! You have completed all lessons in {courseObj?.Title ?? "your course"}.",
                            TargetUrl = $"/Student/Course/{courseId}",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.Notifications.Add(milestoneNotif);
                        await _context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception milestoneEx)
            {
                _logger.LogWarning(milestoneEx, "Failed to check or dispatch milestone notification for student {StudentId}.", studentId);
            }

            return CurriculumOperationResult.Success(courseItemId, courseItem.Title, "Lesson marked as complete.");
        }
        catch (DbUpdateException ex)
        {
            // Concurrent request safety: Unique composite index on {StudentId, CourseItemId} prevents duplicates
            _logger.LogWarning(ex, "MarkLessonCompleteAsync: Concurrency duplicate caught by unique index for student {StudentId}, Lesson {CourseItemId}.", studentId, courseItemId);
            return CurriculumOperationResult.Success(courseItemId, courseItem.Title, "Lesson is already marked as complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkLessonCompleteAsync: Unexpected error saving progress for student {StudentId}, Lesson {CourseItemId}.", studentId, courseItemId);
            return CurriculumOperationResult.Failed($"Failed to record completion: {ex.Message}");
        }
    }

    public async Task<VideoHeartbeatResult> RecordVideoHeartbeatAsync(
        string studentId,
        int courseId,
        int courseItemId,
        double currentPositionSeconds,
        double videoDurationSeconds,
        double segmentStartSeconds,
        double segmentEndSeconds,
        bool isPlaybackAdvance)
    {
        if (string.IsNullOrWhiteSpace(studentId) || courseId <= 0 || courseItemId <= 0)
        {
            return new VideoHeartbeatResult(false, 0, 0, false, 0, "Invalid video heartbeat parameters.");
        }

        var isEnrolled = await _context.Enrollments.AsNoTracking().AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (!isEnrolled)
        {
            return new VideoHeartbeatResult(false, 0, 0, false, 0, "Student is not enrolled in this course.");
        }

        var courseItem = await _context.CourseItems
            .AsNoTracking()
            .Include(ci => ci.Chapter)
            .Include(ci => ci.Lesson)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Lesson);

        if (courseItem == null || courseItem.Chapter == null || courseItem.Chapter.CourseId != courseId ||
            courseItem.Lesson == null || string.IsNullOrWhiteSpace(courseItem.Lesson.VideoUrl))
        {
            return new VideoHeartbeatResult(false, 0, 0, false, 0, "Video lesson not found in this course.");
        }

        var progress = await _context.VideoLessonProgresses
            .FirstOrDefaultAsync(v => v.StudentId == studentId && v.CourseItemId == courseItemId);

        if (progress == null)
        {
            progress = new VideoLessonProgress
            {
                StudentId = studentId,
                CourseItemId = courseItemId,
                WatchedIntervalsJson = "[]",
                LastActivityAt = DateTime.UtcNow
            };
            _context.VideoLessonProgresses.Add(progress);
        }

        if (videoDurationSeconds > 0)
        {
            progress.VideoDurationSeconds = videoDurationSeconds;
        }

        double effectiveDuration = progress.VideoDurationSeconds;
        double clampedPos = effectiveDuration > 0
            ? Math.Clamp(currentPositionSeconds, 0, effectiveDuration)
            : Math.Max(0, currentPositionSeconds);
        progress.LastPositionSeconds = clampedPos;
        progress.LastActivityAt = DateTime.UtcNow;

        var intervals = DeserializeIntervals(progress.WatchedIntervalsJson);

        // Anti-seek rule: only accept continuous forward playback segments <= 15s per heartbeat
        if (isPlaybackAdvance && segmentStartSeconds >= 0 && segmentEndSeconds > segmentStartSeconds)
        {
            double clampedStart = effectiveDuration > 0 ? Math.Clamp(segmentStartSeconds, 0, effectiveDuration) : segmentStartSeconds;
            double clampedEnd = effectiveDuration > 0 ? Math.Clamp(segmentEndSeconds, 0, effectiveDuration) : segmentEndSeconds;
            double span = clampedEnd - clampedStart;

            if (span > 0 && span <= 15.0)
            {
                progress.WatchedDurationSeconds += span;
                intervals.Add(new[] { Math.Round(clampedStart, 2), Math.Round(clampedEnd, 2) });
            }
        }

        var merged = MergeIntervals(intervals);
        double qualifying = merged.Sum(iv => iv[1] - iv[0]);
        progress.WatchedIntervalsJson = JsonSerializer.Serialize(merged);
        progress.QualifyingWatchedSeconds = Math.Round(qualifying, 2);

        int pct = effectiveDuration > 0
            ? Math.Min(100, (int)Math.Round((progress.QualifyingWatchedSeconds / effectiveDuration) * 100.0))
            : 0;

        bool reachedThreshold = effectiveDuration > 0 && (progress.QualifyingWatchedSeconds / effectiveDuration) >= 0.90;
        if (reachedThreshold && !progress.IsCompleted)
        {
            progress.IsCompleted = true;
            progress.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        if (progress.IsCompleted)
        {
            await MarkLessonCompleteAsync(studentId, courseId, courseItemId);
        }

        return new VideoHeartbeatResult(
            true,
            progress.QualifyingWatchedSeconds,
            progress.IsCompleted ? Math.Max(90, pct) : pct,
            progress.IsCompleted,
            progress.LastPositionSeconds);
    }

    private static List<double[]> DeserializeIntervals(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<double[]>();
        try
        {
            var parsed = JsonSerializer.Deserialize<List<double[]>>(json);
            return parsed?.Where(iv => iv != null && iv.Length == 2 && iv[1] > iv[0]).ToList() ?? new List<double[]>();
        }
        catch
        {
            return new List<double[]>();
        }
    }

    private static List<double[]> MergeIntervals(List<double[]> intervals)
    {
        if (intervals.Count == 0) return new List<double[]>();

        var ordered = intervals.OrderBy(iv => iv[0]).ThenBy(iv => iv[1]).ToList();
        var merged = new List<double[]> { new[] { ordered[0][0], ordered[0][1] } };

        for (int i = 1; i < ordered.Count; i++)
        {
            var last = merged[^1];
            var curr = ordered[i];
            if (curr[0] <= last[1] + 1.0)
            {
                last[1] = Math.Max(last[1], curr[1]);
            }
            else
            {
                merged.Add(new[] { curr[0], curr[1] });
            }
        }

        return merged;
    }

    public async Task<int> GetStudentCompletedLessonsCountAsync(string studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId)) return 0;

        return await _context.CourseProgresses
            .AsNoTracking()
            .CountAsync(cp => cp.StudentId == studentId && cp.IsCompleted && cp.CourseItem.Type == ItemType.Lesson);
    }

    public async Task<Dictionary<int, (int TotalLessons, int CompletedLessons)>> GetStudentCoursesProgressAsync(string studentId, IEnumerable<int> courseIds)
    {
        var result = new Dictionary<int, (int TotalLessons, int CompletedLessons)>();
        var idList = courseIds.Where(id => id > 0).Distinct().ToList();
        if (idList.Count == 0) return result;

        // Total learnable lessons per course
        var totalLessonsMap = await _context.CourseItems
            .AsNoTracking()
            .Where(ci => ci.Type == ItemType.Lesson && idList.Contains(ci.Chapter.CourseId))
            .GroupBy(ci => ci.Chapter.CourseId)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CourseId, x => x.Count);

        // Completed lessons per course for student
        Dictionary<int, int> completedLessonsMap = new();
        if (!string.IsNullOrWhiteSpace(studentId))
        {
            completedLessonsMap = await _context.CourseProgresses
                .AsNoTracking()
                .Where(cp => cp.StudentId == studentId && cp.IsCompleted && cp.CourseItem.Type == ItemType.Lesson && idList.Contains(cp.CourseItem.Chapter.CourseId))
                .GroupBy(cp => cp.CourseItem.Chapter.CourseId)
                .Select(g => new { CourseId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CourseId, x => x.Count);
        }

        foreach (var cId in idList)
        {
            var total = totalLessonsMap.GetValueOrDefault(cId, 0);
            var completed = completedLessonsMap.GetValueOrDefault(cId, 0);
            result[cId] = (total, completed);
        }

        return result;
    }

    public async Task<Dictionary<int, (int TotalVideoLessons, int CompletedVideoLessons, DateTime? LastActivityAt)>> GetStudentCoursesVideoProgressAsync(
        string studentId, IEnumerable<int> courseIds)
    {
        var result = new Dictionary<int, (int TotalVideoLessons, int CompletedVideoLessons, DateTime? LastActivityAt)>();
        var idList = courseIds.Where(id => id > 0).Distinct().ToList();
        if (idList.Count == 0) return result;

        var totalVideoMap = await _context.CourseItems
            .AsNoTracking()
            .Where(ci => ci.Type == ItemType.Lesson &&
                         ci.Lesson != null &&
                         !string.IsNullOrWhiteSpace(ci.Lesson.VideoUrl) &&
                         idList.Contains(ci.Chapter.CourseId))
            .GroupBy(ci => ci.Chapter.CourseId)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CourseId, x => x.Count);

        Dictionary<int, (int CompletedCount, DateTime? LastActivityAt)> studentVideoMap = new();
        if (!string.IsNullOrWhiteSpace(studentId))
        {
            studentVideoMap = await _context.VideoLessonProgresses
                .AsNoTracking()
                .Where(v => v.StudentId == studentId &&
                            v.CourseItem.Type == ItemType.Lesson &&
                            v.CourseItem.Lesson != null &&
                            !string.IsNullOrWhiteSpace(v.CourseItem.Lesson.VideoUrl) &&
                            idList.Contains(v.CourseItem.Chapter.CourseId))
                .GroupBy(v => v.CourseItem.Chapter.CourseId)
                .Select(g => new
                {
                    CourseId = g.Key,
                    CompletedCount = g.Count(v => v.IsCompleted),
                    LastActivityAt = g.Max(v => (DateTime?)v.LastActivityAt)
                })
                .ToDictionaryAsync(x => x.CourseId, x => (x.CompletedCount, x.LastActivityAt));
        }

        foreach (var cId in idList)
        {
            var total = totalVideoMap.GetValueOrDefault(cId, 0);
            var (completed, lastAt) = studentVideoMap.GetValueOrDefault(cId, (0, null));
            result[cId] = (total, completed, lastAt);
        }

        return result;
    }

    // =========================================================================
    // STEP 4.3 QUIZ MANAGEMENT & STUDENT QUIZ PLAYER
    // =========================================================================

    public async Task<CurriculumOperationResult> CreateQuizAsync(QuizCreateViewModel model)
    {
        if (model == null) return CurriculumOperationResult.ValidationError("Invalid quiz submission data.");
        if (string.IsNullOrWhiteSpace(model.Title)) return CurriculumOperationResult.ValidationError("Quiz title is required.");
        if (model.ChapterId <= 0) return CurriculumOperationResult.ValidationError("Valid Module ID is required.");

        var chapter = await _context.Chapters
            .Include(ch => ch.Course)
            .FirstOrDefaultAsync(ch => ch.Id == model.ChapterId);

        if (chapter == null) return CurriculumOperationResult.NotFound("Module not found.");
        if (model.CourseId > 0 && chapter.CourseId != model.CourseId)
        {
            return CurriculumOperationResult.ValidationError("Module does not belong to the specified course.");
        }

        int position = model.Position ?? await GetNextItemPositionAsync(model.ChapterId);

        var courseItem = new CourseItem
        {
            Title = model.Title.Trim(),
            Position = position,
            Type = ItemType.Quiz,
            ChapterId = model.ChapterId
        };
        _context.CourseItems.Add(courseItem);
        await _context.SaveChangesAsync();

        var quiz = new Quiz
        {
            CourseItemId = courseItem.Id,
            TimeLimitMinutes = model.TimeLimitMinutes > 0 ? model.TimeLimitMinutes : 30,
            PassingScore = model.PassingScore >= 0 ? model.PassingScore : 50.0,
            MaxAttempts = model.MaxAttempts > 0 ? model.MaxAttempts : 3,
            IsPublished = model.IsPublished
        };
        _context.Quizzes.Add(quiz);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Quiz ID {QuizId} created under CourseItem ID {CourseItemId} in Chapter {ChapterId}.",
            quiz.Id, courseItem.Id, model.ChapterId);

        return CurriculumOperationResult.Ok("Quiz created successfully.", courseItem.Id);
    }

    public async Task<CurriculumOperationResult> UpdateQuizAsync(QuizEditViewModel model)
    {
        if (model == null) return CurriculumOperationResult.ValidationError("Invalid quiz edit data.");
        if (string.IsNullOrWhiteSpace(model.Title)) return CurriculumOperationResult.ValidationError("Quiz title is required.");
        if (model.CourseItemId <= 0) return CurriculumOperationResult.ValidationError("Valid CourseItem ID is required.");

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Quiz)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => ci.Id == model.CourseItemId && ci.Type == ItemType.Quiz);

        if (courseItem == null || courseItem.Quiz == null) return CurriculumOperationResult.NotFound("Quiz not found.");

        if (model.CourseId > 0 && courseItem.Chapter.CourseId != model.CourseId)
        {
            return CurriculumOperationResult.ValidationError("Quiz does not belong to the specified course.");
        }
        if (model.ChapterId > 0 && courseItem.ChapterId != model.ChapterId)
        {
            return CurriculumOperationResult.ValidationError("Quiz does not belong to the specified module.");
        }

        courseItem.Title = model.Title.Trim();
        courseItem.Position = model.Position;
        courseItem.Quiz.TimeLimitMinutes = model.TimeLimitMinutes > 0 ? model.TimeLimitMinutes : 30;
        courseItem.Quiz.PassingScore = model.PassingScore >= 0 ? model.PassingScore : 50.0;
        courseItem.Quiz.MaxAttempts = model.MaxAttempts > 0 ? model.MaxAttempts : 3;
        courseItem.Quiz.IsPublished = model.IsPublished;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Quiz ID {QuizId} updated successfully.", courseItem.Quiz.Id);
        return CurriculumOperationResult.Ok("Quiz updated successfully.");
    }

    public async Task<CurriculumOperationResult> DeleteQuizAsync(int courseItemId, int? expectedChapterId = null, int? expectedCourseId = null)
    {
        if (courseItemId <= 0) return CurriculumOperationResult.ValidationError("Invalid quiz item ID.");

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Quiz)
                .ThenInclude(q => q!.Submissions)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Quiz);

        if (courseItem == null) return CurriculumOperationResult.NotFound("Quiz item not found.");

        if (expectedCourseId.HasValue && courseItem.Chapter.CourseId != expectedCourseId.Value)
        {
            return CurriculumOperationResult.ValidationError("Quiz item does not belong to the specified course.");
        }
        if (expectedChapterId.HasValue && courseItem.ChapterId != expectedChapterId.Value)
        {
            return CurriculumOperationResult.ValidationError("Quiz item does not belong to the specified module.");
        }

        if (courseItem.Quiz != null &&
            (courseItem.Quiz.Submissions.Any() || await _context.QuizAttempts.AnyAsync(qa => qa.QuizId == courseItem.Quiz.Id)))
        {
            _logger.LogWarning("Safe delete blocked for Quiz ID {QuizId}: Student submissions or attempts exist.", courseItem.Quiz.Id);
            return CurriculumOperationResult.Fail("Cannot delete this quiz because student submissions already exist. Preserving academic history.");
        }

        _context.CourseItems.Remove(courseItem);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Quiz CourseItem ID {CourseItemId} safely deleted.", courseItemId);
        return CurriculumOperationResult.Ok("Quiz deleted successfully.");
    }

    public async Task<QuizEditViewModel?> GetQuizAsync(int courseItemId, int? expectedChapterId = null, int? expectedCourseId = null)
    {
        if (courseItemId <= 0) return null;

        var courseItem = await _context.CourseItems
            .AsNoTracking()
            .Include(ci => ci.Quiz)
                .ThenInclude(q => q!.Questions)
                    .ThenInclude(qu => qu.Options)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Quiz);

        if (courseItem == null || courseItem.Quiz == null) return null;

        if (expectedCourseId.HasValue && courseItem.Chapter.CourseId != expectedCourseId.Value) return null;
        if (expectedChapterId.HasValue && courseItem.ChapterId != expectedChapterId.Value) return null;

        return new QuizEditViewModel
        {
            CourseItemId = courseItem.Id,
            QuizId = courseItem.Quiz.Id,
            ChapterId = courseItem.ChapterId,
            CourseId = courseItem.Chapter.CourseId,
            Title = courseItem.Title,
            Position = courseItem.Position,
            TimeLimitMinutes = courseItem.Quiz.TimeLimitMinutes,
            PassingScore = courseItem.Quiz.PassingScore,
            MaxAttempts = courseItem.Quiz.MaxAttempts,
            IsPublished = courseItem.Quiz.IsPublished,
            Questions = courseItem.Quiz.Questions
                .OrderBy(q => q.Order)
                .Select(q => new QuestionViewModel
                {
                    Id = q.Id,
                    QuizId = q.QuizId,
                    Text = q.Text,
                    Type = q.Type,
                    Points = q.Points,
                    Order = q.Order,
                    Options = q.Options.Select(o => new QuestionOptionViewModel
                    {
                        Id = o.Id,
                        QuestionId = o.QuestionId,
                        Text = o.Text,
                        IsCorrect = o.IsCorrect
                    }).ToList()
                }).ToList()
        };
    }

    // =========================================================================
    // STEP 4.3B BOOK & ASSIGNMENT MANAGEMENT (FACULTY AUTHORING)
    // =========================================================================

    public async Task<CurriculumOperationResult> CreateBookAsync(BookCreateViewModel model)
    {
        if (model == null) return CurriculumOperationResult.ValidationError("Invalid resource/book data.");
        if (string.IsNullOrWhiteSpace(model.Title)) return CurriculumOperationResult.ValidationError("Resource title is required.");
        if (string.IsNullOrWhiteSpace(model.FileUrl)) return CurriculumOperationResult.ValidationError("Document file URL or path is required.");
        if (model.ChapterId <= 0) return CurriculumOperationResult.ValidationError("Valid Module ID is required.");

        var safeFileUrl = _htmlSanitizer.SanitizeResourceUrl(model.FileUrl);
        if (safeFileUrl == null)
        {
            return CurriculumOperationResult.ValidationError("Invalid document URL or path. Only http://, https://, or safe relative paths are permitted.");
        }

        var chapter = await _context.Chapters.Include(ch => ch.Course).FirstOrDefaultAsync(ch => ch.Id == model.ChapterId);
        if (chapter == null) return CurriculumOperationResult.NotFound("Module not found.");
        if (model.CourseId > 0 && chapter.CourseId != model.CourseId)
        {
            return CurriculumOperationResult.ValidationError("Module does not belong to the specified course.");
        }

        int position = model.Position ?? await GetNextItemPositionAsync(model.ChapterId);

        var courseItem = new CourseItem
        {
            Title = model.Title.Trim(),
            Position = position,
            Type = ItemType.Book,
            ChapterId = model.ChapterId
        };
        _context.CourseItems.Add(courseItem);
        await _context.SaveChangesAsync();

        var book = new Book
        {
            CourseItemId = courseItem.Id,
            FileUrl = safeFileUrl,
            FileType = !string.IsNullOrWhiteSpace(model.FileType) ? model.FileType.Trim() : "PDF",
            FileSize = model.FileSize
        };
        _context.Books.Add(book);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Book ID {BookId} created under CourseItem ID {CourseItemId} in Chapter {ChapterId}.",
            book.Id, courseItem.Id, model.ChapterId);

        return CurriculumOperationResult.Ok("Resource / Book created successfully.", courseItem.Id);
    }

    public async Task<CurriculumOperationResult> UpdateBookAsync(BookEditViewModel model)
    {
        if (model == null) return CurriculumOperationResult.ValidationError("Invalid book edit data.");
        if (string.IsNullOrWhiteSpace(model.Title)) return CurriculumOperationResult.ValidationError("Resource title is required.");
        if (string.IsNullOrWhiteSpace(model.FileUrl)) return CurriculumOperationResult.ValidationError("Document file URL or path is required.");
        if (model.CourseItemId <= 0) return CurriculumOperationResult.ValidationError("Valid CourseItem ID is required.");

        var safeFileUrl = _htmlSanitizer.SanitizeResourceUrl(model.FileUrl);
        if (safeFileUrl == null)
        {
            return CurriculumOperationResult.ValidationError("Invalid document URL or path. Only http://, https://, or safe relative paths are permitted.");
        }

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Book)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => ci.Id == model.CourseItemId && ci.Type == ItemType.Book);

        if (courseItem == null || courseItem.Book == null) return CurriculumOperationResult.NotFound("Resource not found.");

        if (model.CourseId > 0 && courseItem.Chapter.CourseId != model.CourseId)
        {
            return CurriculumOperationResult.ValidationError("Resource does not belong to the specified course.");
        }
        if (model.ChapterId > 0 && courseItem.ChapterId != model.ChapterId)
        {
            return CurriculumOperationResult.ValidationError("Resource does not belong to the specified module.");
        }

        courseItem.Title = model.Title.Trim();
        courseItem.Position = model.Position;
        courseItem.Book.FileUrl = safeFileUrl;
        courseItem.Book.FileType = !string.IsNullOrWhiteSpace(model.FileType) ? model.FileType.Trim() : "PDF";
        courseItem.Book.FileSize = model.FileSize;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Book ID {BookId} updated successfully.", courseItem.Book.Id);
        return CurriculumOperationResult.Ok("Resource / Book updated successfully.");
    }

    public async Task<CurriculumOperationResult> DeleteBookAsync(int courseItemId, int? expectedChapterId = null, int? expectedCourseId = null)
    {
        if (courseItemId <= 0) return CurriculumOperationResult.ValidationError("Invalid course item ID.");

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Book)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Book);

        if (courseItem == null) return CurriculumOperationResult.NotFound("Resource not found.");

        if (expectedCourseId.HasValue && courseItem.Chapter.CourseId != expectedCourseId.Value)
        {
            return CurriculumOperationResult.ValidationError("Resource does not belong to the specified course.");
        }
        if (expectedChapterId.HasValue && courseItem.ChapterId != expectedChapterId.Value)
        {
            return CurriculumOperationResult.ValidationError("Resource does not belong to the specified module.");
        }

        _context.CourseItems.Remove(courseItem);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Book CourseItem ID {CourseItemId} deleted successfully.", courseItemId);
        return CurriculumOperationResult.Success(courseItemId, courseItem.Title, "Resource / Book deleted successfully.", courseItem.Chapter?.CourseId);
    }

    public async Task<BookEditViewModel?> GetBookByIdAsync(int courseItemId)
    {
        if (courseItemId <= 0) return null;

        var courseItem = await _context.CourseItems
            .AsNoTracking()
            .Include(ci => ci.Book)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Book);

        if (courseItem == null || courseItem.Book == null) return null;

        return new BookEditViewModel
        {
            CourseItemId = courseItem.Id,
            BookId = courseItem.Book.Id,
            ChapterId = courseItem.ChapterId,
            CourseId = courseItem.Chapter.CourseId,
            Title = courseItem.Title,
            FileUrl = courseItem.Book.FileUrl,
            FileType = courseItem.Book.FileType,
            FileSize = courseItem.Book.FileSize,
            Position = courseItem.Position
        };
    }

    public async Task<CurriculumOperationResult> CreateAssignmentAsync(AssignmentCreateViewModel model)
    {
        if (model == null) return CurriculumOperationResult.ValidationError("Invalid assignment data.");
        if (string.IsNullOrWhiteSpace(model.Title)) return CurriculumOperationResult.ValidationError("Assignment title is required.");
        if (string.IsNullOrWhiteSpace(model.Instruction)) return CurriculumOperationResult.ValidationError("Instructions are required.");
        if (model.ChapterId <= 0) return CurriculumOperationResult.ValidationError("Valid Module ID is required.");

        var chapter = await _context.Chapters.Include(ch => ch.Course).FirstOrDefaultAsync(ch => ch.Id == model.ChapterId);
        if (chapter == null) return CurriculumOperationResult.NotFound("Module not found.");
        if (model.CourseId > 0 && chapter.CourseId != model.CourseId)
        {
            return CurriculumOperationResult.ValidationError("Module does not belong to the specified course.");
        }

        int position = model.Position ?? await GetNextItemPositionAsync(model.ChapterId);

        var courseItem = new CourseItem
        {
            Title = model.Title.Trim(),
            Position = position,
            Type = ItemType.Assignment,
            ChapterId = model.ChapterId
        };
        _context.CourseItems.Add(courseItem);
        await _context.SaveChangesAsync();

        var assignment = new Assignment
        {
            CourseItemId = courseItem.Id,
            Instruction = model.Instruction.Trim(),
            MaxScore = model.MaxScore > 0 ? model.MaxScore : 100.0,
            DueDate = model.DueDate != default ? model.DueDate : DateTime.UtcNow.AddDays(7)
        };
        _context.Assignments.Add(assignment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Assignment ID {AssignmentId} created under CourseItem ID {CourseItemId} in Chapter {ChapterId}.",
            assignment.Id, courseItem.Id, model.ChapterId);

        return CurriculumOperationResult.Ok("Assignment created successfully.", courseItem.Id);
    }

    public async Task<CurriculumOperationResult> UpdateAssignmentAsync(AssignmentEditViewModel model)
    {
        if (model == null) return CurriculumOperationResult.ValidationError("Invalid assignment edit data.");
        if (string.IsNullOrWhiteSpace(model.Title)) return CurriculumOperationResult.ValidationError("Assignment title is required.");
        if (string.IsNullOrWhiteSpace(model.Instruction)) return CurriculumOperationResult.ValidationError("Instructions are required.");
        if (model.CourseItemId <= 0) return CurriculumOperationResult.ValidationError("Valid CourseItem ID is required.");

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Assignment)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => ci.Id == model.CourseItemId && ci.Type == ItemType.Assignment);

        if (courseItem == null || courseItem.Assignment == null) return CurriculumOperationResult.NotFound("Assignment not found.");

        if (model.CourseId > 0 && courseItem.Chapter.CourseId != model.CourseId)
        {
            return CurriculumOperationResult.ValidationError("Assignment does not belong to the specified course.");
        }
        if (model.ChapterId > 0 && courseItem.ChapterId != model.ChapterId)
        {
            return CurriculumOperationResult.ValidationError("Assignment does not belong to the specified module.");
        }

        courseItem.Title = model.Title.Trim();
        courseItem.Position = model.Position;
        courseItem.Assignment.Instruction = model.Instruction.Trim();
        courseItem.Assignment.MaxScore = model.MaxScore > 0 ? model.MaxScore : 100.0;
        courseItem.Assignment.DueDate = model.DueDate != default ? model.DueDate : courseItem.Assignment.DueDate;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Assignment ID {AssignmentId} updated successfully.", courseItem.Assignment.Id);
        return CurriculumOperationResult.Ok("Assignment updated successfully.");
    }

    public async Task<CurriculumOperationResult> DeleteAssignmentAsync(int courseItemId, int? expectedChapterId = null, int? expectedCourseId = null)
    {
        if (courseItemId <= 0) return CurriculumOperationResult.ValidationError("Invalid assignment item ID.");

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Assignment)
                .ThenInclude(a => a!.Submissions)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Assignment);

        if (courseItem == null) return CurriculumOperationResult.NotFound("Assignment not found.");

        if (expectedCourseId.HasValue && courseItem.Chapter.CourseId != expectedCourseId.Value)
        {
            return CurriculumOperationResult.ValidationError("Assignment does not belong to the specified course.");
        }
        if (expectedChapterId.HasValue && courseItem.ChapterId != expectedChapterId.Value)
        {
            return CurriculumOperationResult.ValidationError("Assignment does not belong to the specified module.");
        }

        if (courseItem.Assignment != null && courseItem.Assignment.Submissions.Any())
        {
            _logger.LogWarning("Safe delete blocked for Assignment ID {AssignmentId}: Student submissions exist.", courseItem.Assignment.Id);
            return CurriculumOperationResult.BlockedByStudentData(courseItemId, courseItem.Title, "Cannot delete assignment because student submissions already exist. Deletion is blocked to preserve student academic records.", 0, courseItem.Assignment.Submissions.Count);
        }

        _context.CourseItems.Remove(courseItem);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Assignment CourseItem ID {CourseItemId} safely deleted.", courseItemId);
        return CurriculumOperationResult.Success(courseItemId, courseItem.Title, "Assignment deleted successfully.", courseItem.Chapter?.CourseId);
    }

    public async Task<AssignmentEditViewModel?> GetAssignmentByIdAsync(int courseItemId)
    {
        if (courseItemId <= 0) return null;

        var courseItem = await _context.CourseItems
            .AsNoTracking()
            .Include(ci => ci.Assignment)
            .Include(ci => ci.Chapter)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Assignment);

        if (courseItem == null || courseItem.Assignment == null) return null;

        return new AssignmentEditViewModel
        {
            CourseItemId = courseItem.Id,
            AssignmentId = courseItem.Assignment.Id,
            ChapterId = courseItem.ChapterId,
            CourseId = courseItem.Chapter.CourseId,
            Title = courseItem.Title,
            Instruction = courseItem.Assignment.Instruction,
            MaxScore = courseItem.Assignment.MaxScore,
            DueDate = courseItem.Assignment.DueDate,
            Position = courseItem.Position
        };
    }

    public async Task<Quiz?> GetQuizByIdAsync(int quizId)
    {
        if (quizId <= 0) return null;

        return await _context.Quizzes
            .AsNoTracking()
            .Include(q => q.CourseItem)
                .ThenInclude(ci => ci.Chapter)
                    .ThenInclude(ch => ch.Course)
            .Include(q => q.Questions)
                .ThenInclude(qu => qu.Options)
            .FirstOrDefaultAsync(q => q.Id == quizId);
    }

    public async Task<QuizQuestionsViewModel?> GetQuizQuestionsAsync(int quizId)
    {
        if (quizId <= 0) return null;

        var quiz = await _context.Quizzes
            .AsNoTracking()
            .Include(q => q.CourseItem)
                .ThenInclude(ci => ci.Chapter)
                    .ThenInclude(ch => ch.Course)
            .Include(q => q.Questions.OrderBy(qu => qu.Order))
                .ThenInclude(qu => qu.Options)
            .FirstOrDefaultAsync(q => q.Id == quizId);

        if (quiz == null || quiz.CourseItem == null || quiz.CourseItem.Chapter == null) return null;

        return new QuizQuestionsViewModel
        {
            QuizId = quiz.Id,
            CourseItemId = quiz.CourseItemId,
            CourseId = quiz.CourseItem.Chapter.CourseId,
            CourseCode = quiz.CourseItem.Chapter.Course?.Code ?? "",
            CourseTitle = quiz.CourseItem.Chapter.Course?.Title ?? "",
            QuizTitle = quiz.CourseItem.Title,
            ChapterId = quiz.CourseItem.ChapterId,
            ChapterTitle = quiz.CourseItem.Chapter.Title,
            TimeLimitMinutes = quiz.TimeLimitMinutes,
            PassingScore = quiz.PassingScore,
            IsPublished = quiz.IsPublished,
            Questions = quiz.Questions
                .OrderBy(q => q.Order)
                .Select(q => new QuestionViewModel
                {
                    Id = q.Id,
                    QuizId = q.QuizId,
                    Text = q.Text,
                    Type = q.Type,
                    Points = q.Points,
                    Order = q.Order,
                    Options = q.Options.Select(o => new QuestionOptionViewModel
                    {
                        Id = o.Id,
                        QuestionId = o.QuestionId,
                        Text = o.Text,
                        IsCorrect = o.IsCorrect
                    }).ToList()
                }).ToList(),
            NewQuestion = new QuestionViewModel { QuizId = quiz.Id }
        };
    }

    public async Task<CurriculumOperationResult> AddQuestionAsync(int quizId, QuestionViewModel model)
    {
        if (quizId <= 0) return CurriculumOperationResult.ValidationError("Invalid Quiz ID.");
        if (model == null) return CurriculumOperationResult.ValidationError("Question model is required.");
        if (string.IsNullOrWhiteSpace(model.Text)) return CurriculumOperationResult.ValidationError("Question text is required.");

        var quiz = await _context.Quizzes
            .Include(q => q.Questions)
            .FirstOrDefaultAsync(q => q.Id == quizId);

        if (quiz == null) return CurriculumOperationResult.NotFound("Quiz not found.");

        // Validation: filter non-empty options
        var validOptions = (model.Options ?? new List<QuestionOptionViewModel>())
            .Where(o => !string.IsNullOrWhiteSpace(o.Text))
            .ToList();

        if (validOptions.Count < 2)
        {
            return CurriculumOperationResult.ValidationError("A multiple-choice question must have at least two answer options.");
        }

        var correctCount = validOptions.Count(o => o.IsCorrect);
        if (correctCount != 1)
        {
            return CurriculumOperationResult.ValidationError("Exactly one correct answer option must be selected.");
        }

        int order = model.Order > 0 ? model.Order : quiz.Questions.Count + 1;

        var question = new Question
        {
            QuizId = quizId,
            Text = model.Text.Trim(),
            Type = model.Type,
            Points = model.Points > 0 ? model.Points : 1.0,
            Order = order
        };
        _context.Questions.Add(question);
        await _context.SaveChangesAsync();

        foreach (var opt in validOptions)
        {
            _context.QuestionOptions.Add(new QuestionOption
            {
                QuestionId = question.Id,
                Text = opt.Text.Trim(),
                IsCorrect = opt.IsCorrect
            });
        }
        await _context.SaveChangesAsync();

        _logger.LogInformation("Question ID {QuestionId} with {OptionCount} options added to Quiz ID {QuizId}.",
            question.Id, validOptions.Count, quizId);

        return CurriculumOperationResult.Ok("Question added successfully.", question.Id);
    }

    public async Task<CurriculumOperationResult> UpdateQuestionAsync(QuestionViewModel model)
    {
        if (model == null || model.Id <= 0) return CurriculumOperationResult.ValidationError("Invalid question data.");
        if (string.IsNullOrWhiteSpace(model.Text)) return CurriculumOperationResult.ValidationError("Question text is required.");

        var question = await _context.Questions
            .Include(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == model.Id);

        if (question == null) return CurriculumOperationResult.NotFound("Question not found.");

        if (model.QuizId > 0 && question.QuizId != model.QuizId)
        {
            return CurriculumOperationResult.ValidationError("Question does not belong to the specified quiz.");
        }

        var validOptions = (model.Options ?? new List<QuestionOptionViewModel>())
            .Where(o => !string.IsNullOrWhiteSpace(o.Text))
            .ToList();

        if (validOptions.Count < 2)
        {
            return CurriculumOperationResult.ValidationError("A question must have at least two answer options.");
        }

        var correctCount = validOptions.Count(o => o.IsCorrect);
        if (correctCount != 1)
        {
            return CurriculumOperationResult.ValidationError("Exactly one correct answer option must be selected.");
        }

        question.Text = model.Text.Trim();
        question.Points = model.Points > 0 ? model.Points : 1.0;
        if (model.Order > 0) question.Order = model.Order;
        question.Type = model.Type;

        // Remove existing options and recreate to avoid identity conflicts
        _context.QuestionOptions.RemoveRange(question.Options);
        foreach (var opt in validOptions)
        {
            _context.QuestionOptions.Add(new QuestionOption
            {
                QuestionId = question.Id,
                Text = opt.Text.Trim(),
                IsCorrect = opt.IsCorrect
            });
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Question ID {QuestionId} updated successfully.", question.Id);
        return CurriculumOperationResult.Ok("Question updated successfully.");
    }

    public async Task<CurriculumOperationResult> DeleteQuestionAsync(int questionId, int? expectedQuizId = null)
    {
        if (questionId <= 0) return CurriculumOperationResult.ValidationError("Invalid Question ID.");

        var question = await _context.Questions
            .Include(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == questionId);

        if (question == null) return CurriculumOperationResult.NotFound("Question not found.");
        if (expectedQuizId.HasValue && question.QuizId != expectedQuizId.Value)
        {
            return CurriculumOperationResult.ValidationError("Question does not belong to the specified quiz.");
        }

        _context.Questions.Remove(question);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Question ID {QuestionId} safely deleted.", questionId);
        return CurriculumOperationResult.Ok("Question deleted successfully.");
    }

    public async Task<QuestionViewModel?> GetQuestionByIdAsync(int questionId)
    {
        if (questionId <= 0) return null;

        var question = await _context.Questions
            .AsNoTracking()
            .Include(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == questionId);

        if (question == null) return null;

        return new QuestionViewModel
        {
            Id = question.Id,
            QuizId = question.QuizId,
            Text = question.Text,
            Type = question.Type,
            Points = question.Points,
            Order = question.Order,
            Options = question.Options.Select(o => new QuestionOptionViewModel
            {
                Id = o.Id,
                QuestionId = o.QuestionId,
                Text = o.Text,
                IsCorrect = o.IsCorrect
            }).ToList()
        };
    }

    public async Task<StudentQuizPlayerViewModel?> GetStudentQuizPlayerAsync(int courseId, int courseItemId, string studentId, bool allowAdminPreview = false)
    {
        if (courseId <= 0 || courseItemId <= 0 || string.IsNullOrWhiteSpace(studentId)) return null;

        // Enrollment check (bypassed only for explicit server-verified Admin preview)
        if (!allowAdminPreview)
        {
            var isEnrolled = await _context.Enrollments.AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
            if (!isEnrolled)
            {
                _logger.LogWarning("GetStudentQuizPlayerAsync: Student {StudentId} is not enrolled in Course {CourseId}.", studentId, courseId);
                return null;
            }
        }

        var course = await _context.Courses
            .AsNoTracking()
            .Include(c => c.Teacher)
            .Include(c => c.Chapters.OrderBy(ch => ch.Position).ThenBy(ch => ch.Id))
                .ThenInclude(ch => ch.CourseItems.OrderBy(ci => ci.Position).ThenBy(ci => ci.Id))
                    .ThenInclude(ci => ci.Lesson)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Quiz)
                        .ThenInclude(q => q!.Questions.OrderBy(qu => qu.Order))
                            .ThenInclude(qu => qu.Options)
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (course == null) return null;

        // Locate Quiz CourseItem
        CourseItem? currentItem = null;
        Chapter? currentChapter = null;
        foreach (var ch in course.Chapters)
        {
            var item = ch.CourseItems.FirstOrDefault(ci => ci.Id == courseItemId && ci.Type == ItemType.Quiz && ci.Quiz != null);
            if (item != null)
            {
                currentItem = item;
                currentChapter = ch;
                break;
            }
        }

        if (currentItem == null || currentChapter == null || currentItem.Quiz == null)
        {
            _logger.LogWarning("GetStudentQuizPlayerAsync: Quiz CourseItem ID {CourseItemId} not found in Course {CourseId}.", courseItemId, courseId);
            return null;
        }

        var quiz = currentItem.Quiz;
        if (!quiz.IsPublished && !allowAdminPreview)
        {
            _logger.LogWarning("GetStudentQuizPlayerAsync: Student {StudentId} attempted to access unpublished Quiz {QuizId} (CourseItem {CourseItemId}).",
                studentId, quiz.Id, courseItemId);
            return null;
        }

        // Student submissions for this quiz
        var allSubmissions = await _context.QuizSubmissions
            .AsNoTracking()
            .Where(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId)
            .OrderByDescending(qs => qs.SubmittedAt)
            .ThenByDescending(qs => qs.Id)
            .ToListAsync();

        var existingAttemptsMeta = await _context.QuizAttempts
            .AsNoTracking()
            .Where(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId)
            .OrderBy(qa => qa.AttemptNumber)
            .Select(qa => new { qa.AttemptNumber, qa.MaxAttemptsAllowed })
            .FirstOrDefaultAsync();

        var firstSubmission = allSubmissions.LastOrDefault();
        int effectiveMaxAttempts = firstSubmission != null && firstSubmission.MaxAttemptsAllowed > 0
            ? firstSubmission.MaxAttemptsAllowed
            : (existingAttemptsMeta != null && existingAttemptsMeta.MaxAttemptsAllowed > 0
                ? existingAttemptsMeta.MaxAttemptsAllowed
                : (quiz.MaxAttempts > 0 ? quiz.MaxAttempts : 3));

        var (isQuizAccessible, missingQuizPrereqs) = await CheckItemAccessAsync(currentItem.Id, studentId, allowAdminPreview);

        DateTime nowUtc = DateTime.UtcNow;
        QuizAttempt? activeAttempt = null;
        string? expiredAttemptNotice = null;

        if (!allowAdminPreview && isQuizAccessible)
        {
            activeAttempt = await _context.QuizAttempts
                .AsNoTracking()
                .Include(qa => qa.DraftAnswers)
                .FirstOrDefaultAsync(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId && qa.Status == QuizAttemptStatus.InProgress);

            DateTime? activeDeadlineUtc = EnsureUtcKind(activeAttempt?.DeadlineUtc);
            if (activeAttempt != null &&
                quiz.TimeLimitMinutes > 0 &&
                activeDeadlineUtc.HasValue &&
                nowUtc > activeDeadlineUtc.Value.Add(QuizGracePeriod))
            {
                var (finalizedResult, finalizedOutcome) = await FinalizeExpiredAttemptSafelyAsync(
                    courseId,
                    courseItemId,
                    currentItem.Title,
                    course.Code,
                    course.Title,
                    quiz,
                    studentId,
                    activeAttempt.Id,
                    nowUtc);

                if (finalizedResult.IsSuccess && finalizedOutcome != null)
                {
                    expiredAttemptNotice = $"Your previous attempt (#{activeAttempt.AttemptNumber}) expired and was automatically finalized using your saved pre-deadline answers (Score: {finalizedOutcome.Score}%).";
                }
                else if (finalizedResult.Status == CurriculumOperationStatus.Conflict)
                {
                    expiredAttemptNotice = $"Your previous attempt (#{activeAttempt.AttemptNumber}) expired and has been finalized.";
                }

                activeAttempt = null;
                allSubmissions = await _context.QuizSubmissions
                    .AsNoTracking()
                    .Where(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId)
                    .OrderByDescending(qs => qs.SubmittedAt)
                    .ThenByDescending(qs => qs.Id)
                    .ToListAsync();
            }

            if (allSubmissions.Count == 0 && effectiveMaxAttempts > 0 && activeAttempt == null)
            {
                int maxAttemptNum = await _context.QuizAttempts
                    .AsNoTracking()
                    .Where(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId)
                    .Select(qa => (int?)qa.AttemptNumber)
                    .MaxAsync() ?? 0;

                activeAttempt = await CreateOrResumeInProgressAttemptAsync(
                    quiz,
                    studentId,
                    Math.Max(allSubmissions.Count, maxAttemptNum) + 1,
                    effectiveMaxAttempts,
                    nowUtc);
            }
        }

        var latestSubmission = allSubmissions.FirstOrDefault();
        var bestScore = allSubmissions.Any() ? allSubmissions.Max(s => s.Score) : (double?)null;

        int? latestCorrectQuestionsCount = null;
        int? latestTotalQuestionsCount = null;
        if (latestSubmission != null)
        {
            var latestAnswerStats = await _context.QuizSubmissionAnswers
                .AsNoTracking()
                .Where(a => a.QuizSubmissionId == latestSubmission.Id)
                .Select(a => a.IsCorrect)
                .ToListAsync();

            if (latestAnswerStats.Count > 0)
            {
                latestTotalQuestionsCount = latestAnswerStats.Count;
                latestCorrectQuestionsCount = latestAnswerStats.Count(isCorrect => isCorrect == true);
            }
            else
            {
                latestTotalQuestionsCount = quiz.Questions.Count;
                latestCorrectQuestionsCount = 0;
            }
        }

        // Query completed item IDs (all types for sidebar indicators) and lesson-only IDs for progress
        var completedRows = await _context.CourseProgresses
            .AsNoTracking()
            .Where(cp => cp.StudentId == studentId && cp.IsCompleted && cp.CourseItem.Chapter.CourseId == courseId)
            .Select(cp => new { cp.CourseItemId, cp.CourseItem.Type })
            .ToListAsync();
        var completedItemIds = completedRows.Select(x => x.CourseItemId).ToHashSet();
        var completedLessonCount = completedRows.Count(x => x.Type == ItemType.Lesson);

        var totalLessons = course.Chapters.SelectMany(ch => ch.CourseItems.Where(ci => ci.Type == ItemType.Lesson)).Count();
        var progressPct = totalLessons > 0 ? Math.Min(100, (int)Math.Round((double)completedLessonCount / totalLessons * 100)) : 0;

        var modules = course.Chapters
            .OrderBy(ch => ch.Position)
            .ThenBy(ch => ch.Id)
            .Select(ch => new StudentPlayerModuleViewModel
            {
                ModuleId = ch.Id,
                Title = ch.Title,
                Position = ch.Position,
                IsCurrentModule = ch.Id == currentChapter.Id,
                Lessons = ch.CourseItems
                    .Where(ci => (ci.Type == ItemType.Lesson && ci.Lesson != null) || (ci.Type == ItemType.Quiz && ci.Quiz != null && (ci.Quiz.IsPublished || allowAdminPreview)))
                    .OrderBy(ci => ci.Position)
                    .ThenBy(ci => ci.Id)
                    .Select(ci => new StudentPlayerLessonItemViewModel
                    {
                        CourseItemId = ci.Id,
                        LessonId = ci.Lesson?.Id ?? 0,
                        QuizId = ci.Quiz?.Id,
                        Type = ci.Type,
                        Title = ci.Title,
                        Position = ci.Position,
                        HasVideo = ci.Lesson != null && !string.IsNullOrWhiteSpace(ci.Lesson.VideoUrl),
                        HasContent = ci.Lesson != null && !string.IsNullOrWhiteSpace(ci.Lesson.ContentHtml),
                        IsCurrent = ci.Id == currentItem.Id,
                        IsCompleted = completedItemIds.Contains(ci.Id)
                    }).ToList()
            }).ToList();

        DateTime? resolvedAttemptDeadlineUtc = EnsureUtcKind(activeAttempt?.DeadlineUtc);
        int? remainingSeconds = null;
        if (!allowAdminPreview && isQuizAccessible && resolvedAttemptDeadlineUtc.HasValue)
        {
            remainingSeconds = Math.Max(0, (int)Math.Floor((resolvedAttemptDeadlineUtc.Value - nowUtc).TotalSeconds));
        }

        var savedAnswers = activeAttempt?.DraftAnswers
            .Where(a => a.SelectedOptionId.HasValue)
            .ToDictionary(a => a.QuestionId, a => a.SelectedOptionId!.Value) ?? new Dictionary<int, int>();

        return new StudentQuizPlayerViewModel
        {
            CourseId = course.Id,
            CourseCode = course.Code,
            CourseTitle = course.Title,
            TeacherName = course.Teacher?.FullName ?? "Instructor",
            ModuleId = currentChapter.Id,
            ModuleTitle = currentChapter.Title,
            ModulePosition = currentChapter.Position,
            CourseItemId = currentItem.Id,
            QuizId = quiz.Id,
            QuizTitle = currentItem.Title,
            QuizPosition = currentItem.Position,
            TimeLimitMinutes = quiz.TimeLimitMinutes,
            RemainingSeconds = remainingSeconds,
            ActiveAttemptId = activeAttempt?.Id,
            AttemptStartedAtUtc = EnsureUtcKind(activeAttempt?.StartedAtUtc),
            AttemptDeadlineUtc = resolvedAttemptDeadlineUtc,
            ServerNowUtc = EnsureUtcKind(nowUtc),
            ExpiredAttemptNotice = expiredAttemptNotice,
            SavedAnswers = savedAnswers,
            MaxAttempts = effectiveMaxAttempts,
            PassingScore = quiz.PassingScore,
            IsLocked = !isQuizAccessible,
            IsAdminPreview = allowAdminPreview,
            MissingPrerequisites = missingQuizPrereqs,
            IsPublished = quiz.IsPublished,
            HasSubmission = latestSubmission != null,
            AttemptCount = allSubmissions.Count,
            LatestSubmissionId = latestSubmission?.Id,
            LatestScore = latestSubmission?.Score,
            BestScore = bestScore,
            LatestIsPassed = latestSubmission?.IsPassed,
            LatestIsAutoFinalized = latestSubmission?.IsAutoFinalized ?? false,
            LatestCorrectQuestionsCount = latestCorrectQuestionsCount,
            LatestTotalQuestionsCount = latestTotalQuestionsCount,
            LatestSubmittedAt = latestSubmission?.SubmittedAt,
            CourseProgressPercentage = progressPct,
            CompletedLessonsCount = completedLessonCount,
            TotalLessonsCount = totalLessons,
            Questions = quiz.Questions
                .OrderBy(q => q.Order)
                .Select(q => new StudentQuizQuestionViewModel
                {
                    QuestionId = q.Id,
                    Text = q.Text,
                    Points = q.Points,
                    Order = q.Order,
                    Type = q.Type,
                    Options = q.Options
                        .Select(o => new StudentQuizOptionViewModel
                        {
                            OptionId = o.Id,
                            Text = o.Text
                        }).ToList()
                }).ToList(),
            Modules = modules
        };
    }

    public async Task<CurriculumOperationResult> SaveQuizAnswerAsync(
        string studentId,
        int courseId,
        int courseItemId,
        int attemptId,
        int questionId,
        int optionId)
    {
        if (string.IsNullOrWhiteSpace(studentId) || courseId <= 0 || courseItemId <= 0 || attemptId <= 0 || questionId <= 0 || optionId <= 0)
        {
            return CurriculumOperationResult.ValidationError("Invalid quiz answer request parameters.");
        }

        var isEnrolled = await _context.Enrollments.AsNoTracking()
            .AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (!isEnrolled)
        {
            return CurriculumOperationResult.Forbidden("Student is not enrolled in this course.");
        }

        var (isQuizAccessible, missingPrereqs) = await CheckItemAccessAsync(courseItemId, studentId, allowAdminPreview: false);
        if (!isQuizAccessible)
        {
            return CurriculumOperationResult.Forbidden($"This quiz is locked by prerequisites: {string.Join(", ", missingPrereqs)}.");
        }

        var courseItem = await _context.CourseItems
            .AsNoTracking()
            .Include(ci => ci.Chapter)
                .ThenInclude(ch => ch.Course)
            .Include(ci => ci.Quiz)
                .ThenInclude(q => q!.Questions)
                    .ThenInclude(qu => qu.Options)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Quiz);

        if (courseItem == null || courseItem.Quiz == null || courseItem.Chapter == null || courseItem.Chapter.CourseId != courseId)
        {
            return CurriculumOperationResult.NotFound("Quiz not found.");
        }

        var quiz = courseItem.Quiz;
        if (!quiz.IsPublished)
        {
            return CurriculumOperationResult.NotFound("Quiz not found.");
        }

        var question = quiz.Questions.FirstOrDefault(q => q.Id == questionId);
        if (question == null || !question.Options.Any(o => o.Id == optionId))
        {
            return CurriculumOperationResult.ValidationError("Question or option does not belong to this quiz.");
        }

        var attempt = await _context.QuizAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(qa => qa.Id == attemptId && qa.QuizId == quiz.Id && qa.StudentId == studentId);

        if (attempt == null)
        {
            return CurriculumOperationResult.NotFound("Quiz attempt not found.");
        }

        if (attempt.Status != QuizAttemptStatus.InProgress)
        {
            return CurriculumOperationResult.Conflict("This quiz attempt has already been finalized.", attempt.QuizSubmissionId);
        }

        var nowUtc = DateTime.UtcNow;
        DateTime? attemptDeadlineUtc = EnsureUtcKind(attempt.DeadlineUtc);
        if (quiz.TimeLimitMinutes > 0 && attemptDeadlineUtc.HasValue && nowUtc > attemptDeadlineUtc.Value.Add(QuizGracePeriod))
        {
            var (finalizedResult, _) = await FinalizeExpiredAttemptSafelyAsync(
                courseId,
                courseItemId,
                courseItem.Title,
                courseItem.Chapter.Course?.Code ?? "",
                courseItem.Chapter.Course?.Title ?? "",
                quiz,
                studentId,
                attempt.Id,
                nowUtc);

            return CurriculumOperationResult.Expired(
                "Quiz time limit has expired. Your saved pre-deadline answers have been finalized.",
                finalizedResult.EntityId);
        }

        var existingAnswer = await _context.QuizAttemptAnswers
            .FirstOrDefaultAsync(a => a.QuizAttemptId == attempt.Id && a.QuestionId == questionId);
        if (existingAnswer == null)
        {
            _context.QuizAttemptAnswers.Add(new QuizAttemptAnswer
            {
                QuizAttemptId = attempt.Id,
                QuestionId = questionId,
                SelectedOptionId = optionId,
                UpdatedAtUtc = nowUtc
            });
        }
        else
        {
            existingAnswer.SelectedOptionId = optionId;
            existingAnswer.UpdatedAtUtc = nowUtc;
        }

        try
        {
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
        }
        catch (DbUpdateException ex) when (IsSqlUniqueConstraintViolation(ex))
        {
            _context.ChangeTracker.Clear();
            var concurrentAnswer = await _context.QuizAttemptAnswers
                .FirstOrDefaultAsync(a => a.QuizAttemptId == attempt.Id && a.QuestionId == questionId);
            if (concurrentAnswer != null)
            {
                concurrentAnswer.SelectedOptionId = optionId;
                concurrentAnswer.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                _context.ChangeTracker.Clear();
            }
        }

        int remaining = attemptDeadlineUtc.HasValue
            ? Math.Max(0, (int)Math.Floor((attemptDeadlineUtc.Value - nowUtc).TotalSeconds))
            : 0;

        return CurriculumOperationResult.Success(
            attempt.Id,
            courseItem.Title,
            "Answer saved.",
            courseItemId,
            remaining);
    }

    public async Task<(CurriculumOperationResult Result, QuizAttempt? Attempt)> StartNextQuizAttemptAsync(
        string studentId,
        int courseId,
        int courseItemId)
    {
        if (string.IsNullOrWhiteSpace(studentId) || courseId <= 0 || courseItemId <= 0)
        {
            return (CurriculumOperationResult.ValidationError("Invalid quiz attempt request."), null);
        }

        var isEnrolled = await _context.Enrollments
            .AsNoTracking()
            .AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (!isEnrolled)
        {
            _logger.LogWarning("StartNextQuizAttemptAsync: Student {StudentId} is not enrolled in Course {CourseId}.", studentId, courseId);
            return (CurriculumOperationResult.Forbidden("Student is not enrolled in this course."), null);
        }

        var courseItem = await _context.CourseItems
            .AsNoTracking()
            .Include(ci => ci.Chapter)
                .ThenInclude(ch => ch.Course)
            .Include(ci => ci.Quiz)
                .ThenInclude(q => q!.Questions)
                    .ThenInclude(qu => qu.Options)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Quiz);

        if (courseItem == null || courseItem.Quiz == null || courseItem.Chapter == null || courseItem.Chapter.CourseId != courseId)
        {
            return (CurriculumOperationResult.NotFound("Quiz not found."), null);
        }

        var quiz = courseItem.Quiz;
        if (!quiz.IsPublished)
        {
            return (CurriculumOperationResult.NotFound("Quiz not found."), null);
        }

        var (isQuizAccessible, missingQuizPrereqs) = await CheckItemAccessAsync(courseItem.Id, studentId, allowAdminPreview: false);
        if (!isQuizAccessible)
        {
            return (CurriculumOperationResult.Forbidden($"This quiz is locked by prerequisites: {string.Join(", ", missingQuizPrereqs)}."), null);
        }

        var firstSubmission = await _context.QuizSubmissions
            .AsNoTracking()
            .Where(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId)
            .OrderBy(qs => qs.SubmittedAt)
            .ThenBy(qs => qs.Id)
            .FirstOrDefaultAsync();

        var firstAttemptRecord = await _context.QuizAttempts
            .AsNoTracking()
            .Where(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId)
            .OrderBy(qa => qa.AttemptNumber)
            .FirstOrDefaultAsync();

        int effectiveMaxAttempts = firstSubmission != null && firstSubmission.MaxAttemptsAllowed > 0
            ? firstSubmission.MaxAttemptsAllowed
            : (firstAttemptRecord != null && firstAttemptRecord.MaxAttemptsAllowed > 0
                ? firstAttemptRecord.MaxAttemptsAllowed
                : (quiz.MaxAttempts > 0 ? quiz.MaxAttempts : 3));

        DateTime nowUtc = DateTime.UtcNow;

        var activeAttempt = await _context.QuizAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId && qa.Status == QuizAttemptStatus.InProgress);

        if (activeAttempt != null)
        {
            DateTime? activeDeadlineUtc = EnsureUtcKind(activeAttempt.DeadlineUtc);
            if (quiz.TimeLimitMinutes > 0 && activeDeadlineUtc.HasValue)
            {
                if (nowUtc > activeDeadlineUtc.Value.Add(QuizGracePeriod))
                {
                    await FinalizeExpiredAttemptSafelyAsync(
                        courseId,
                        courseItemId,
                        courseItem.Title,
                        courseItem.Chapter.Course?.Code ?? "",
                        courseItem.Chapter.Course?.Title ?? "",
                        quiz,
                        studentId,
                        activeAttempt.Id,
                        nowUtc);
                }
                else if (nowUtc > activeDeadlineUtc.Value)
                {
                    return (
                        CurriculumOperationResult.Conflict(
                            "Your current quiz attempt is still finalizing within the grace window. Please wait a few seconds and refresh.",
                            activeAttempt.Id),
                        null);
                }
                else
                {
                    return (CurriculumOperationResult.Ok("Active quiz attempt resumed.", activeAttempt.Id), activeAttempt);
                }
            }
            else
            {
                return (CurriculumOperationResult.Ok("Active quiz attempt resumed.", activeAttempt.Id), activeAttempt);
            }
        }

        // Re-check active attempts after any concurrent finalization
        activeAttempt = await _context.QuizAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId && qa.Status == QuizAttemptStatus.InProgress);
        if (activeAttempt != null)
        {
            DateTime? activeDeadlineUtc = EnsureUtcKind(activeAttempt.DeadlineUtc);
            if (quiz.TimeLimitMinutes > 0 && activeDeadlineUtc.HasValue && nowUtc > activeDeadlineUtc.Value)
            {
                return (
                    CurriculumOperationResult.Conflict(
                        "Your previous quiz attempt is finalizing. Please refresh in a moment.",
                        activeAttempt.Id),
                    null);
            }
            return (CurriculumOperationResult.Ok("Active quiz attempt resumed.", activeAttempt.Id), activeAttempt);
        }

        int submissionsCount = await _context.QuizSubmissions
            .AsNoTracking()
            .CountAsync(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId);

        if (submissionsCount >= effectiveMaxAttempts)
        {
            _logger.LogWarning("StartNextQuizAttemptAsync: Student {StudentId} has reached MaxAttempts ({Max}) for Quiz {QuizId}.",
                studentId, effectiveMaxAttempts, quiz.Id);
            return (CurriculumOperationResult.Forbidden($"You have reached the maximum allowed attempts ({effectiveMaxAttempts}) for this quiz."), null);
        }

        int maxAttemptNum = await _context.QuizAttempts
            .AsNoTracking()
            .Where(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId)
            .Select(qa => (int?)qa.AttemptNumber)
            .MaxAsync() ?? 0;

        var createdOrResumed = await CreateOrResumeInProgressAttemptAsync(
            quiz,
            studentId,
            Math.Max(submissionsCount, maxAttemptNum) + 1,
            effectiveMaxAttempts,
            nowUtc);

        if (createdOrResumed == null)
        {
            return (CurriculumOperationResult.Forbidden($"You have reached the maximum allowed attempts ({effectiveMaxAttempts}) for this quiz."), null);
        }

        return (CurriculumOperationResult.Ok("Next quiz attempt started.", createdOrResumed.Id), createdOrResumed);
    }

    private async Task<QuizAttempt?> CreateOrResumeInProgressAttemptAsync(
        Quiz quiz,
        string studentId,
        int attemptNumber,
        int effectiveMaxAttempts,
        DateTime nowUtc)
    {
        var newAttempt = new QuizAttempt
        {
            QuizId = quiz.Id,
            StudentId = studentId,
            AttemptNumber = attemptNumber,
            Status = QuizAttemptStatus.InProgress,
            StartedAtUtc = nowUtc,
            DeadlineUtc = quiz.TimeLimitMinutes > 0 ? nowUtc.AddMinutes(quiz.TimeLimitMinutes) : null,
            MaxAttemptsAllowed = effectiveMaxAttempts
        };

        _context.QuizAttempts.Add(newAttempt);
        try
        {
            await _context.SaveChangesAsync();
            _context.Entry(newAttempt).State = EntityState.Detached;
            return newAttempt;
        }
        catch (DbUpdateException ex) when (IsSqlUniqueConstraintViolation(ex))
        {
            _context.Entry(newAttempt).State = EntityState.Detached;
            var existingActive = await _context.QuizAttempts
                .AsNoTracking()
                .Include(qa => qa.DraftAnswers)
                .FirstOrDefaultAsync(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId && qa.Status == QuizAttemptStatus.InProgress);

            if (existingActive != null)
            {
                return existingActive;
            }

            int refreshedSubmissions = await _context.QuizSubmissions
                .AsNoTracking()
                .CountAsync(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId);

            if (refreshedSubmissions < effectiveMaxAttempts)
            {
                int refreshedMaxAttemptNum = await _context.QuizAttempts
                    .AsNoTracking()
                    .Where(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId)
                    .Select(qa => (int?)qa.AttemptNumber)
                    .MaxAsync() ?? 0;

                var retryAttempt = new QuizAttempt
                {
                    QuizId = quiz.Id,
                    StudentId = studentId,
                    AttemptNumber = Math.Max(refreshedSubmissions, refreshedMaxAttemptNum) + 1,
                    Status = QuizAttemptStatus.InProgress,
                    StartedAtUtc = nowUtc,
                    DeadlineUtc = quiz.TimeLimitMinutes > 0 ? nowUtc.AddMinutes(quiz.TimeLimitMinutes) : null,
                    MaxAttemptsAllowed = effectiveMaxAttempts
                };
                _context.QuizAttempts.Add(retryAttempt);
                try
                {
                    await _context.SaveChangesAsync();
                    _context.Entry(retryAttempt).State = EntityState.Detached;
                    return retryAttempt;
                }
                catch (DbUpdateException retryEx) when (IsSqlUniqueConstraintViolation(retryEx))
                {
                    _context.Entry(retryAttempt).State = EntityState.Detached;
                    return await _context.QuizAttempts
                        .AsNoTracking()
                        .Include(qa => qa.DraftAnswers)
                        .FirstOrDefaultAsync(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId && qa.Status == QuizAttemptStatus.InProgress);
                }
            }

            return null;
        }
    }

    public async Task<(CurriculumOperationResult Result, StudentQuizResultViewModel? Outcome)> SubmitQuizAsync(
        string studentId,
        int courseId,
        int courseItemId,
        Dictionary<int, int> selectedOptions,
        int? attemptId = null,
        bool requireActiveAttempt = false)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return (CurriculumOperationResult.ValidationError("Student ID is required."), null);
        }

        if (courseId <= 0 || courseItemId <= 0)
        {
            return (CurriculumOperationResult.ValidationError("Invalid course or quiz item ID."), null);
        }

        // Enrollment check
        var isEnrolled = await _context.Enrollments.AsNoTracking().AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (!isEnrolled)
        {
            _logger.LogWarning("SubmitQuizAsync: Student {StudentId} is not enrolled in Course {CourseId}.", studentId, courseId);
            return (CurriculumOperationResult.Forbidden("Student is not enrolled in this course."), null);
        }

        var courseItem = await _context.CourseItems
            .AsNoTracking()
            .Include(ci => ci.Chapter)
                .ThenInclude(ch => ch.Course)
            .Include(ci => ci.Quiz)
                .ThenInclude(q => q!.Questions)
                    .ThenInclude(qu => qu.Options)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Quiz);

        if (courseItem == null || courseItem.Quiz == null || courseItem.Chapter == null)
        {
            return (CurriculumOperationResult.NotFound("Quiz not found."), null);
        }

        if (courseItem.Chapter.CourseId != courseId)
        {
            return (CurriculumOperationResult.ValidationError("Quiz does not belong to the specified course."), null);
        }

        var quiz = courseItem.Quiz;
        if (!quiz.IsPublished)
        {
            _logger.LogWarning("SubmitQuizAsync: Student {StudentId} attempted to submit unpublished Quiz {QuizId} (CourseItem {CourseItemId}).",
                studentId, quiz.Id, courseItemId);
            return (CurriculumOperationResult.NotFound("Quiz not found."), null);
        }

        string courseCode = courseItem.Chapter.Course?.Code ?? "";
        string courseTitle = courseItem.Chapter.Course?.Title ?? "";
        DateTime nowUtc = DateTime.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Query student's first attempt to determine effective MaxAttempts (future-only limit rule)
            var firstSubmission = await _context.QuizSubmissions
                .AsNoTracking()
                .Where(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId)
                .OrderBy(qs => qs.SubmittedAt)
                .ThenBy(qs => qs.Id)
                .FirstOrDefaultAsync();

            var firstAttemptRecord = await _context.QuizAttempts
                .AsNoTracking()
                .Where(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId)
                .OrderBy(qa => qa.AttemptNumber)
                .FirstOrDefaultAsync();

            int effectiveMaxAttempts = firstSubmission != null && firstSubmission.MaxAttemptsAllowed > 0
                ? firstSubmission.MaxAttemptsAllowed
                : (firstAttemptRecord != null && firstAttemptRecord.MaxAttemptsAllowed > 0
                    ? firstAttemptRecord.MaxAttemptsAllowed
                    : (quiz.MaxAttempts > 0 ? quiz.MaxAttempts : 3));

            var existingAttemptsCount = await _context.QuizSubmissions
                .AsNoTracking()
                .CountAsync(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId);

            QuizAttempt? attempt;
            if (attemptId.HasValue)
            {
                attempt = await _context.QuizAttempts
                    .AsNoTracking()
                    .Include(qa => qa.DraftAnswers)
                    .FirstOrDefaultAsync(qa => qa.Id == attemptId.Value && qa.QuizId == quiz.Id && qa.StudentId == studentId);

                if (attempt == null)
                {
                    await transaction.RollbackAsync();
                    return (CurriculumOperationResult.ValidationError("Quiz attempt not found."), null);
                }

                if (attempt.Status != QuizAttemptStatus.InProgress)
                {
                    await transaction.RollbackAsync();
                    return (CurriculumOperationResult.Conflict("This quiz attempt has already been finalized.", attempt.QuizSubmissionId), null);
                }
            }
            else
            {
                attempt = await _context.QuizAttempts
                    .AsNoTracking()
                    .Include(qa => qa.DraftAnswers)
                    .FirstOrDefaultAsync(qa => qa.QuizId == quiz.Id && qa.StudentId == studentId && qa.Status == QuizAttemptStatus.InProgress);

                if (attempt == null)
                {
                    if (requireActiveAttempt)
                    {
                        await transaction.RollbackAsync();
                        if (existingAttemptsCount >= effectiveMaxAttempts)
                        {
                            return (CurriculumOperationResult.Forbidden($"You have reached the maximum allowed attempts ({effectiveMaxAttempts}) for this quiz."), null);
                        }
                        return (CurriculumOperationResult.ValidationError("No active quiz attempt was found. Please open the quiz before submitting."), null);
                    }

                    if (existingAttemptsCount >= effectiveMaxAttempts)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogWarning("SubmitQuizAsync: Student {StudentId} has reached MaxAttempts ({Max}) for Quiz {QuizId}.",
                            studentId, effectiveMaxAttempts, quiz.Id);
                        return (CurriculumOperationResult.Forbidden($"You have reached the maximum allowed attempts ({effectiveMaxAttempts}) for this quiz."), null);
                    }

                    attempt = new QuizAttempt
                    {
                        QuizId = quiz.Id,
                        StudentId = studentId,
                        AttemptNumber = existingAttemptsCount + 1,
                        Status = QuizAttemptStatus.InProgress,
                        StartedAtUtc = nowUtc,
                        DeadlineUtc = quiz.TimeLimitMinutes > 0 ? nowUtc.AddMinutes(quiz.TimeLimitMinutes) : null,
                        MaxAttemptsAllowed = effectiveMaxAttempts
                    };
                }
            }

            if (existingAttemptsCount >= effectiveMaxAttempts)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning("SubmitQuizAsync: Student {StudentId} has reached MaxAttempts ({Max}) for Quiz {QuizId}.",
                    studentId, effectiveMaxAttempts, quiz.Id);
                return (CurriculumOperationResult.Forbidden($"You have reached the maximum allowed attempts ({effectiveMaxAttempts}) for this quiz."), null);
            }

            DateTime? attemptDeadlineUtc = EnsureUtcKind(attempt.DeadlineUtc);
            bool isExpired = quiz.TimeLimitMinutes > 0 &&
                             attemptDeadlineUtc.HasValue &&
                             nowUtc > attemptDeadlineUtc.Value.Add(QuizGracePeriod);

            Dictionary<int, int> answersToGrade;
            if (isExpired)
            {
                // Reject late POST payload; grade ONLY pre-deadline saved draft answers
                answersToGrade = attempt.DraftAnswers
                    .Where(a => a.SelectedOptionId.HasValue)
                    .ToDictionary(a => a.QuestionId, a => a.SelectedOptionId!.Value);

                _logger.LogWarning("SubmitQuizAsync expired: Student {StudentId}, Quiz {QuizId}, Attempt {AttemptId}, Deadline {DeadlineUtc}, Now {NowUtc}. Finalizing from {DraftCount} saved pre-deadline answer(s).",
                    studentId, quiz.Id, attempt.Id, attempt.DeadlineUtc, nowUtc, answersToGrade.Count);
            }
            else
            {
                answersToGrade = selectedOptions ?? new Dictionary<int, int>();
            }

            var outcome = await FinalizeAttemptInCurrentTransactionAsync(
                courseId,
                courseItemId,
                courseItem.Title,
                courseCode,
                courseTitle,
                quiz,
                studentId,
                attempt,
                answersToGrade,
                isAutoFinalized: isExpired,
                nowUtc: nowUtc,
                existingAttemptsCount: existingAttemptsCount,
                effectiveMaxAttempts: effectiveMaxAttempts);

            if (outcome == null)
            {
                await transaction.RollbackAsync();
                int? existingSubId = attempt.Id > 0
                    ? await _context.QuizAttempts.AsNoTracking().Where(qa => qa.Id == attempt.Id).Select(qa => qa.QuizSubmissionId).FirstOrDefaultAsync()
                    : null;
                return (CurriculumOperationResult.Conflict("This quiz attempt has already been finalized.", existingSubId), null);
            }

            await transaction.CommitAsync();

            await TryDispatchQuizNotificationAsync(studentId, courseId, courseItemId, courseItem.Title, outcome.Score, outcome.IsPassed);

            if (isExpired)
            {
                return (
                    CurriculumOperationResult.Expired(
                        "Quiz time limit has expired. Late answers were rejected and your attempt was finalized using your pre-deadline saved answers.",
                        outcome.SubmissionId),
                    outcome);
            }

            _logger.LogInformation("Student {StudentId} submitted Quiz ID {QuizId} (Submission ID {SubmissionId}, Attempt {Attempt}/{Max}): Score={Score}%, IsPassed={IsPassed}.",
                studentId, quiz.Id, outcome.SubmissionId, outcome.AttemptNumber, effectiveMaxAttempts, outcome.Score, outcome.IsPassed);

            return (CurriculumOperationResult.Ok("Quiz submitted successfully.", outcome.SubmissionId), outcome);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            int? existingSubId = attemptId.HasValue
                ? await _context.QuizAttempts.AsNoTracking().Where(qa => qa.Id == attemptId.Value).Select(qa => qa.QuizSubmissionId).FirstOrDefaultAsync()
                : null;
            return (CurriculumOperationResult.Conflict("This quiz attempt has already been finalized.", existingSubId), null);
        }
        catch (DbUpdateException ex) when (IsSqlUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            int? existingSubId = attemptId.HasValue
                ? await _context.QuizAttempts.AsNoTracking().Where(qa => qa.Id == attemptId.Value).Select(qa => qa.QuizSubmissionId).FirstOrDefaultAsync()
                : null;
            return (CurriculumOperationResult.Conflict("A concurrent submission for this quiz attempt is already finalized.", existingSubId), null);
        }
    }

    private async Task<(CurriculumOperationResult Result, StudentQuizResultViewModel? Outcome)> FinalizeExpiredAttemptSafelyAsync(
        int courseId,
        int courseItemId,
        string quizTitle,
        string courseCode,
        string courseTitle,
        Quiz quiz,
        string studentId,
        int attemptId,
        DateTime nowUtc)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var attempt = await _context.QuizAttempts
                .AsNoTracking()
                .Include(qa => qa.DraftAnswers)
                .FirstOrDefaultAsync(qa => qa.Id == attemptId && qa.QuizId == quiz.Id && qa.StudentId == studentId);

            if (attempt == null)
            {
                await transaction.RollbackAsync();
                return (CurriculumOperationResult.NotFound("Quiz attempt not found."), null);
            }

            if (attempt.Status != QuizAttemptStatus.InProgress)
            {
                await transaction.RollbackAsync();
                return (CurriculumOperationResult.Conflict("This quiz attempt has already been finalized.", attempt.QuizSubmissionId), null);
            }

            var existingAttemptsCount = await _context.QuizSubmissions
                .AsNoTracking()
                .CountAsync(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId);

            int effectiveMaxAttempts = attempt.MaxAttemptsAllowed > 0
                ? attempt.MaxAttemptsAllowed
                : (quiz.MaxAttempts > 0 ? quiz.MaxAttempts : 3);

            var draftMap = attempt.DraftAnswers
                .Where(a => a.SelectedOptionId.HasValue)
                .ToDictionary(a => a.QuestionId, a => a.SelectedOptionId!.Value);

            var outcome = await FinalizeAttemptInCurrentTransactionAsync(
                courseId,
                courseItemId,
                quizTitle,
                courseCode,
                courseTitle,
                quiz,
                studentId,
                attempt,
                draftMap,
                isAutoFinalized: true,
                nowUtc: nowUtc,
                existingAttemptsCount: existingAttemptsCount,
                effectiveMaxAttempts: effectiveMaxAttempts);

            if (outcome == null)
            {
                await transaction.RollbackAsync();
                var existingSubId = await _context.QuizAttempts
                    .AsNoTracking()
                    .Where(qa => qa.Id == attemptId)
                    .Select(qa => qa.QuizSubmissionId)
                    .FirstOrDefaultAsync();
                return (CurriculumOperationResult.Conflict("Quiz attempt already finalized.", existingSubId), null);
            }

            await transaction.CommitAsync();

            await TryDispatchQuizNotificationAsync(studentId, courseId, courseItemId, quizTitle, outcome.Score, outcome.IsPassed);

            return (CurriculumOperationResult.Ok("Expired quiz attempt auto-finalized.", outcome.SubmissionId), outcome);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            var existingSubId = await _context.QuizAttempts
                .AsNoTracking()
                .Where(qa => qa.Id == attemptId)
                .Select(qa => qa.QuizSubmissionId)
                .FirstOrDefaultAsync();
            return (CurriculumOperationResult.Conflict("Quiz attempt already finalized.", existingSubId), null);
        }
        catch (DbUpdateException ex) when (IsSqlUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            var existingSubId = await _context.QuizAttempts
                .AsNoTracking()
                .Where(qa => qa.Id == attemptId)
                .Select(qa => qa.QuizSubmissionId)
                .FirstOrDefaultAsync();
            return (CurriculumOperationResult.Conflict("Quiz attempt already finalized.", existingSubId), null);
        }
    }

    private async Task<StudentQuizResultViewModel?> FinalizeAttemptInCurrentTransactionAsync(
        int courseId,
        int courseItemId,
        string quizTitle,
        string courseCode,
        string courseTitle,
        Quiz quiz,
        string studentId,
        QuizAttempt attempt,
        Dictionary<int, int> selectedOptions,
        bool isAutoFinalized,
        DateTime nowUtc,
        int existingAttemptsCount,
        int effectiveMaxAttempts)
    {
        var targetStatus = isAutoFinalized ? QuizAttemptStatus.Expired : QuizAttemptStatus.Submitted;

        // Claim the active QuizAttempt row FIRST via atomic UPDATE to serialize concurrent submissions
        // and prevent SQL Server lock-order deadlocks.
        if (attempt.Id > 0)
        {
            int claimedRows = await _context.QuizAttempts
                .Where(qa => qa.Id == attempt.Id && qa.Status == QuizAttemptStatus.InProgress)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(qa => qa.Status, targetStatus)
                    .SetProperty(qa => qa.FinalizedAtUtc, nowUtc));

            if (claimedRows == 0)
            {
                return null;
            }
        }

        bool areAttemptsExhausted = (existingAttemptsCount + 1) >= effectiveMaxAttempts;
        var questions = quiz.Questions.OrderBy(q => q.Order).ToList();

        double totalPoints = questions.Sum(q => q.Points > 0 ? q.Points : 1.0);
        double earnedPoints = 0.0;
        int correctCount = 0;

        var questionBreakdown = new List<StudentQuestionResultItemViewModel>();
        var answersToInsert = new List<QuizSubmissionAnswer>();

        foreach (var q in questions)
        {
            double qPoints = q.Points > 0 ? q.Points : 1.0;
            int? selectedOptId = null;
            if (selectedOptions.TryGetValue(q.Id, out int optId))
            {
                selectedOptId = optId;
            }

            var selectedOpt = selectedOptId.HasValue ? q.Options.FirstOrDefault(o => o.Id == selectedOptId.Value) : null;
            if (selectedOpt == null)
            {
                selectedOptId = null;
            }
            bool isQuestionCorrect = selectedOpt != null && selectedOpt.IsCorrect;

            if (isQuestionCorrect)
            {
                earnedPoints += qPoints;
                correctCount++;
            }

            questionBreakdown.Add(new StudentQuestionResultItemViewModel
            {
                QuestionId = q.Id,
                Text = q.Text,
                Points = qPoints,
                EarnedPoints = isQuestionCorrect ? qPoints : 0.0,
                IsCorrect = isQuestionCorrect,
                SelectedOptionId = selectedOptId,
                SelectedOptionText = selectedOpt?.Text ?? "No answer provided",
                Options = q.Options.Select(o => new StudentOptionResultItemViewModel
                {
                    OptionId = o.Id,
                    Text = o.Text,
                    IsCorrect = areAttemptsExhausted ? o.IsCorrect : false,
                    IsSelected = selectedOptId.HasValue && selectedOptId.Value == o.Id
                }).ToList()
            });

            answersToInsert.Add(new QuizSubmissionAnswer
            {
                QuestionId = q.Id,
                SelectedOptionId = selectedOptId,
                AnswerText = selectedOpt?.Text,
                PointsEarned = isQuestionCorrect ? qPoints : 0.0,
                IsCorrect = isQuestionCorrect,
                CreatedAt = nowUtc
            });
        }

        double score = totalPoints > 0 ? Math.Round((earnedPoints / totalPoints) * 100.0, 2) : 0.0;
        bool isPassed = totalPoints > 0 && score >= 50.0;

        DateTime? attemptDeadlineUtc = EnsureUtcKind(attempt.DeadlineUtc);
        DateTime recordedSubmittedAt = isAutoFinalized && attemptDeadlineUtc.HasValue && nowUtc > attemptDeadlineUtc.Value
            ? attemptDeadlineUtc.Value
            : nowUtc;

        var submission = new QuizSubmission
        {
            QuizId = quiz.Id,
            StudentId = studentId,
            Score = score,
            IsPassed = isPassed,
            MaxAttemptsAllowed = effectiveMaxAttempts,
            StartedAtUtc = EnsureUtcKind(attempt.StartedAtUtc),
            IsAutoFinalized = isAutoFinalized,
            SubmittedAt = recordedSubmittedAt
        };

        _context.QuizSubmissions.Add(submission);
        if (attempt.Id == 0)
        {
            attempt.Status = targetStatus;
            attempt.FinalizedAtUtc = nowUtc;
            attempt.QuizSubmission = submission;
            _context.QuizAttempts.Add(attempt);
        }
        await _context.SaveChangesAsync();

        if (attempt.Id > 0)
        {
            await _context.QuizAttempts
                .Where(qa => qa.Id == attempt.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(qa => qa.QuizSubmissionId, submission.Id));
        }

        foreach (var ans in answersToInsert)
        {
            ans.QuizSubmissionId = submission.Id;
            ans.CreatedAt = submission.SubmittedAt;
        }
        _context.QuizSubmissionAnswers.AddRange(answersToInsert);
        await _context.SaveChangesAsync();

        var allAttemptScores = await _context.QuizSubmissions
            .AsNoTracking()
            .Where(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId)
            .Select(qs => qs.Score)
            .ToListAsync();
        double bestScore = allAttemptScores.Any() ? allAttemptScores.Max() : score;

        return new StudentQuizResultViewModel
        {
            CourseId = courseId,
            CourseCode = courseCode,
            CourseTitle = courseTitle,
            CourseItemId = courseItemId,
            QuizId = quiz.Id,
            QuizTitle = quizTitle,
            PassingScore = 50.0,
            SubmissionId = submission.Id,
            AttemptNumber = existingAttemptsCount + 1,
            MaxAttempts = effectiveMaxAttempts,
            AreAttemptsExhausted = areAttemptsExhausted,
            Score = score,
            BestScore = bestScore,
            IsPassed = isPassed,
            StartedAtUtc = submission.StartedAtUtc,
            IsAutoFinalized = submission.IsAutoFinalized,
            SubmittedAt = submission.SubmittedAt,
            TotalPoints = totalPoints,
            EarnedPoints = earnedPoints,
            TotalQuestions = questions.Count,
            CorrectQuestionsCount = correctCount,
            Questions = questionBreakdown
        };
    }

    private async Task TryDispatchQuizNotificationAsync(
        string studentId,
        int courseId,
        int courseItemId,
        string quizTitle,
        double score,
        bool isPassed)
    {
        try
        {
            var notif = new Notification
            {
                UserId = studentId,
                Type = NotificationType.GradeReleased,
                Title = $"Quiz Graded: {quizTitle}",
                Message = $"Your score for {quizTitle} is {score}% ({(isPassed ? "Passed" : "Not Passed")}).",
                TargetUrl = $"/Student/Course/{courseId}/Quiz/{courseItemId}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            _context.Notifications.Add(notif);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch quiz grade notification for student {StudentId}.", studentId);
        }
    }

    private static bool IsSqlUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }

    public async Task<StudentQuizResultViewModel?> GetStudentQuizResultAsync(int courseId, int courseItemId, int submissionId, string studentId, bool allowAdminPreview = false)
    {
        if (courseId <= 0 || courseItemId <= 0 || submissionId <= 0 || string.IsNullOrWhiteSpace(studentId)) return null;

        var submission = await _context.QuizSubmissions
            .AsNoTracking()
            .Include(qs => qs.Quiz)
                .ThenInclude(q => q.CourseItem)
                    .ThenInclude(ci => ci.Chapter)
                        .ThenInclude(ch => ch.Course)
            .Include(qs => qs.Quiz)
                .ThenInclude(q => q.Questions)
                    .ThenInclude(qu => qu.Options)
            .Include(qs => qs.Answers)
                .ThenInclude(a => a.SelectedOption)
            .FirstOrDefaultAsync(qs => qs.Id == submissionId);

        if (submission == null) return null;

        // Student isolation check: student can only view their own submission (Admin preview may inspect any)
        if (!allowAdminPreview && submission.StudentId != studentId)
        if (submission.StudentId != studentId)
        {
            _logger.LogWarning("GetStudentQuizResultAsync: Student {StudentId} attempted to access submission {SubmissionId} belonging to {OwnerId}.",
                studentId, submissionId, submission.StudentId);
            return null;
        }

        // Cross-course check
        if (submission.Quiz.CourseItemId != courseItemId || submission.Quiz.CourseItem.Chapter.CourseId != courseId)
        {
            _logger.LogWarning("GetStudentQuizResultAsync: Submission {SubmissionId} does not belong to Course {CourseId} / CourseItem {CourseItemId}.",
                submissionId, courseId, courseItemId);
            return null;
        }

        var quiz = submission.Quiz;
        var courseItem = quiz.CourseItem;
        var questions = quiz.Questions.OrderBy(q => q.Order).ToList();
        double totalPoints = questions.Sum(q => q.Points > 0 ? q.Points : 1.0);

        // Load all attempts for attempt number and highest score
        var allStudentAttempts = await _context.QuizSubmissions
            .AsNoTracking()
            .Where(qs => qs.QuizId == quiz.Id && qs.StudentId == submission.StudentId)
            .OrderBy(qs => qs.SubmittedAt)
            .ToListAsync();

        int attemptIndex = allStudentAttempts.FindIndex(s => s.Id == submissionId);
        int attemptNumber = attemptIndex >= 0 ? attemptIndex + 1 : 1;
        var firstAttempt = allStudentAttempts.FirstOrDefault();
        int effectiveMaxAttempts = firstAttempt != null && firstAttempt.MaxAttemptsAllowed > 0
            ? firstAttempt.MaxAttemptsAllowed
            : (quiz.MaxAttempts > 0 ? quiz.MaxAttempts : 3);
        double bestScore = allStudentAttempts.Any() ? allStudentAttempts.Max(s => s.Score) : submission.Score;
        bool canRetake = allStudentAttempts.Count < effectiveMaxAttempts;
        bool areAttemptsExhausted = allStudentAttempts.Count >= effectiveMaxAttempts;

        var answersMap = submission.Answers.ToDictionary(a => a.QuestionId);
        int correctCount = submission.Answers.Count(a => a.IsCorrect == true);
        double earnedPoints = submission.Answers.Any() ? submission.Answers.Sum(a => a.PointsEarned) : Math.Round((submission.Score / 100.0) * totalPoints, 2);

        var questionBreakdown = questions.Select(q =>
        {
            answersMap.TryGetValue(q.Id, out var savedAnswer);
            int? selectedOptId = savedAnswer?.SelectedOptionId;
            string selectedText = savedAnswer?.SelectedOption?.Text ?? savedAnswer?.AnswerText ?? "No answer provided";
            bool isCorrect = savedAnswer?.IsCorrect ?? false;
            double qEarnedPoints = savedAnswer?.PointsEarned ?? 0.0;

            return new StudentQuestionResultItemViewModel
            {
                QuestionId = q.Id,
                Text = q.Text,
                Points = q.Points,
                EarnedPoints = qEarnedPoints,
                IsCorrect = isCorrect,
                SelectedOptionId = selectedOptId,
                SelectedOptionText = selectedText,
                Options = q.Options.Select(o => new StudentOptionResultItemViewModel
                {
                    OptionId = o.Id,
                    Text = o.Text,
                    IsCorrect = areAttemptsExhausted ? o.IsCorrect : false,
                    IsSelected = selectedOptId.HasValue && selectedOptId.Value == o.Id
                }).ToList()
            };
        }).ToList();

        return new StudentQuizResultViewModel
        {
            CourseId = courseId,
            CourseCode = courseItem.Chapter.Course?.Code ?? "",
            CourseTitle = courseItem.Chapter.Course?.Title ?? "",
            CourseItemId = courseItemId,
            QuizId = quiz.Id,
            QuizTitle = courseItem.Title,
            PassingScore = 50.0,
            SubmissionId = submission.Id,
            AttemptNumber = attemptNumber,
            MaxAttempts = effectiveMaxAttempts,
            AreAttemptsExhausted = areAttemptsExhausted,
            Score = submission.Score,
            BestScore = bestScore,
            IsPassed = submission.IsPassed,
            StartedAtUtc = submission.StartedAtUtc,
            IsAutoFinalized = submission.IsAutoFinalized,
            SubmittedAt = submission.SubmittedAt,
            TotalPoints = totalPoints,
            EarnedPoints = earnedPoints,
            TotalQuestions = questions.Count,
            CorrectQuestionsCount = correctCount,
            Questions = questionBreakdown
        };
    }

    // =========================================================================
    // PHASE 4 — SHARED CURRICULUM NAVIGATION HELPER
    // =========================================================================

    private static bool IsQuizVisibleToStudent(CourseItem ci, bool allowAdminPreview = false) =>
        ci.Type != ItemType.Quiz || (ci.Quiz != null && (ci.Quiz.IsPublished || allowAdminPreview));

    private async Task<(List<StudentPlayerModuleViewModel> Modules, int CompletedLessonsCount, int TotalLessonsCount, int ProgressPercentage)> BuildStudentCurriculumNavigationAsync(
        int courseId,
        string studentId,
        int currentCourseItemId,
        int currentChapterId,
        bool allowAdminPreview = false)
    {
        var course = await _context.Courses
            .AsNoTracking()
            .Include(c => c.Chapters.OrderBy(ch => ch.Position))
                .ThenInclude(ch => ch.CourseItems.OrderBy(ci => ci.Position))
                    .ThenInclude(ci => ci.Lesson)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Quiz)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Assignment)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Book)
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (course == null)
        {
            return (new List<StudentPlayerModuleViewModel>(), 0, 0, 0);
        }

        var completedRows = await _context.CourseProgresses
            .AsNoTracking()
            .Where(cp => cp.StudentId == studentId && cp.IsCompleted && cp.CourseItem.Chapter.CourseId == courseId)
            .Select(cp => new { cp.CourseItemId, cp.CourseItem.Type })
            .ToListAsync();
        var completedItemIds = completedRows.Select(x => x.CourseItemId).ToHashSet();
        var completedLessonIds = completedRows.Where(x => x.Type == ItemType.Lesson).Select(x => x.CourseItemId).ToHashSet();

        // Quiz submissions for passed status
        var quizSubmissions = await _context.QuizSubmissions
            .AsNoTracking()
            .Where(qs => qs.StudentId == studentId && qs.Quiz.CourseItem.Chapter.CourseId == courseId)
            .ToListAsync();
        var quizPassedMap = quizSubmissions
            .GroupBy(qs => qs.QuizId)
            .ToDictionary(g => g.Key, g => new { HasPassed = g.Any(s => s.IsPassed), BestScore = g.Max(s => s.Score) });

        // Assignment submissions for completed status
        var assignmentSubmissions = await _context.AssignmentSubmissions
            .AsNoTracking()
            .Where(s => s.StudentId == studentId && s.Assignment.CourseItem.Chapter.CourseId == courseId)
            .ToListAsync();
        var assignmentCompletedMap = assignmentSubmissions
            .GroupBy(s => s.AssignmentId)
            .ToDictionary(g => g.Key, g => g.Any(s => s.Status == SubmissionStatus.Graded || s.Status == SubmissionStatus.Submitted || s.Status == SubmissionStatus.Late));

        var allItems = course.Chapters.SelectMany(ch => ch.CourseItems).ToList();
        var totalLessons = allItems.Count(ci => ci.Type == ItemType.Lesson);

        Dictionary<int, List<CourseItemPrerequisite>> prereqMap = new();
        if (course.AccessMode == CourseAccessMode.Sequential)
        {
            var allPrereqs = await _context.CourseItemPrerequisites
                .Where(p => p.CourseItem.Chapter.CourseId == courseId)
                .Include(p => p.RequiredCourseItem)
                .AsNoTracking()
                .ToListAsync();
            prereqMap = allPrereqs.GroupBy(p => p.CourseItemId).ToDictionary(g => g.Key, g => g.ToList());
        }

        var modules = course.Chapters
            .OrderBy(ch => ch.Position)
            .ThenBy(ch => ch.Id)
            .Select(ch => new StudentPlayerModuleViewModel
            {
                ModuleId = ch.Id,
                Title = ch.Title,
                Position = ch.Position,
                IsCurrentModule = ch.Id == currentChapterId,
                Lessons = ch.CourseItems
                    .Where(ci => IsQuizVisibleToStudent(ci, allowAdminPreview))
                    .OrderBy(ci => ci.Position)
                    .ThenBy(ci => ci.Id)
                    .Select(ci =>
                    {
                        bool isPassedQuiz = false;
                        double? bestQuizScore = null;
                        if (ci.Quiz != null && quizPassedMap.TryGetValue(ci.Quiz.Id, out var qStat))
                        {
                            isPassedQuiz = qStat.HasPassed;
                            bestQuizScore = qStat.BestScore;
                        }

                        bool isCompleted = completedItemIds.Contains(ci.Id);
                        if (ci.Type == ItemType.Quiz && isPassedQuiz) isCompleted = true;
                        if (ci.Type == ItemType.Assignment && ci.Assignment != null && assignmentCompletedMap.TryGetValue(ci.Assignment.Id, out var aComp) && aComp)
                        {
                            isCompleted = true;
                        }

                        bool isLocked = false;
                        List<string> missingTitles = new();
                        if (!allowAdminPreview && course.AccessMode == CourseAccessMode.Sequential && prereqMap.TryGetValue(ci.Id, out var itemPrereqs) && itemPrereqs.Count > 0)
                        {
                            foreach (var req in itemPrereqs)
                            {
                                bool reqDone = completedItemIds.Contains(req.RequiredCourseItemId);
                                if (!reqDone)
                                {
                                    if (req.RequiredCourseItem != null && req.RequiredCourseItem.Type == ItemType.Quiz &&
                                        quizPassedMap.TryGetValue(req.RequiredCourseItemId, out var qP) && qP.HasPassed)
                                    {
                                        reqDone = true;
                                    }
                                    else if (req.RequiredCourseItem != null && req.RequiredCourseItem.Type == ItemType.Assignment &&
                                             req.RequiredCourseItem.Assignment != null &&
                                             assignmentCompletedMap.TryGetValue(req.RequiredCourseItem.Assignment.Id, out var aP) && aP)
                                    {
                                        reqDone = true;
                                    }
                                }

                                if (!reqDone)
                                {
                                    isLocked = true;
                                    missingTitles.Add(req.RequiredCourseItem?.Title ?? $"Item #{req.RequiredCourseItemId}");
                                }
                            }
                        }

                        return new StudentPlayerLessonItemViewModel
                        {
                            CourseItemId = ci.Id,
                            LessonId = ci.Lesson?.Id ?? 0,
                            QuizId = ci.Quiz?.Id,
                            AssignmentId = ci.Assignment?.Id,
                            BookId = ci.Book?.Id,
                            Type = ci.Type,
                            Title = ci.Title,
                            Position = ci.Position,
                            HasVideo = ci.Lesson != null && !string.IsNullOrWhiteSpace(ci.Lesson.VideoUrl),
                            HasContent = ci.Lesson != null && !string.IsNullOrWhiteSpace(ci.Lesson.ContentHtml),
                            IsCurrent = ci.Id == currentCourseItemId,
                            IsCompleted = isCompleted,
                            IsLocked = isLocked,
                            MissingPrerequisites = missingTitles,
                            HasPassedQuiz = isPassedQuiz,
                            BestQuizScore = bestQuizScore
                        };
                    }).ToList()
            }).ToList();

        int totalCompleted = completedLessonIds.Count;
        int progressPct = totalLessons > 0 ? (int)Math.Round((double)totalCompleted / totalLessons * 100) : 0;
        if (progressPct > 100) progressPct = 100;

        return (modules, totalCompleted, totalLessons, progressPct);
    }

    // =========================================================================
    // PHASE 4 — ASSIGNMENT STUDENT PLAYER & SUBMISSION
    // =========================================================================

    public async Task<StudentAssignmentViewModel?> GetStudentAssignmentAsync(int courseId, int courseItemId, string studentId, bool allowAdminPreview = false)
    {
        if (courseId <= 0 || courseItemId <= 0 || string.IsNullOrWhiteSpace(studentId)) return null;

        if (!allowAdminPreview)
        {
            var isEnrolled = await _context.Enrollments.AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
            if (!isEnrolled)
            {
                _logger.LogWarning("GetStudentAssignmentAsync: Student {StudentId} is not enrolled in Course {CourseId}.", studentId, courseId);
                return null;
            }
        }

        var courseItem = await _context.CourseItems
            .AsNoTracking()
            .Include(ci => ci.Chapter)
                .ThenInclude(ch => ch.Course)
                    .ThenInclude(c => c!.Teacher)
            .Include(ci => ci.Assignment)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Assignment);

        if (courseItem == null || courseItem.Assignment == null || courseItem.Chapter == null)
        {
            _logger.LogWarning("GetStudentAssignmentAsync: Assignment CourseItem ID {CourseItemId} not found.", courseItemId);
            return null;
        }

        if (courseItem.Chapter.CourseId != courseId)
        {
            _logger.LogWarning("GetStudentAssignmentAsync: CourseItem {CourseItemId} does not belong to Course {CourseId}.", courseItemId, courseId);
            return null;
        }

        var assignment = courseItem.Assignment;
        var course = courseItem.Chapter.Course!;

        // Load student submission history for this assignment
        var submissions = await _context.AssignmentSubmissions
            .AsNoTracking()
            .Where(s => s.AssignmentId == assignment.Id && s.StudentId == studentId)
            .Include(s => s.GradedBy)
            .Include(s => s.Attachments.Where(a => !a.IsDeleted))
            .OrderByDescending(s => s.AttemptNumber)
            .ToListAsync();

        var latestSub = submissions.FirstOrDefault(s => s.IsLatest) ?? submissions.FirstOrDefault();

        var submissionHistory = submissions.Select(s => new StudentAssignmentAttemptViewModel
        {
            SubmissionId = s.Id,
            AttemptNumber = s.AttemptNumber,
            SubmittedAt = s.SubmittedAt,
            Status = s.Status,
            Score = s.Score,
            MaxScore = assignment.MaxScore,
            Feedback = s.Feedback,
            FileUrl = s.FileUrl,
            OriginalFileName = s.OriginalFileName,
            ExternalUrl = s.ExternalUrl,
            TextResponse = s.TextResponse,
            IsLatest = s.IsLatest,
            Attachments = s.Attachments.Where(a => !a.IsDeleted).Select(a => new StudentAttachmentViewModel
            {
                Id = a.Id,
                OriginalFileName = a.OriginalFileName,
                FileSizeBytes = a.FileSizeBytes,
                ContentType = a.ContentType,
                StoragePath = a.StoragePath,
                IsDeleted = a.IsDeleted
            }).ToList()
        }).OrderBy(s => s.AttemptNumber).ToList();

        var nav = await BuildStudentCurriculumNavigationAsync(courseId, studentId, courseItemId, courseItem.ChapterId, allowAdminPreview);
        var (isAssignmentAccessible, missingAssignmentPrereqs) = await CheckItemAccessAsync(courseItem.Id, studentId, allowAdminPreview);

        return new StudentAssignmentViewModel
        {
            CourseId = course.Id,
            CourseCode = course.Code,
            CourseTitle = course.Title,
            TeacherName = course.Teacher?.FullName ?? course.Teacher?.UserName,
            ModuleId = courseItem.Chapter.Id,
            ModuleTitle = courseItem.Chapter.Title,
            ModulePosition = courseItem.Chapter.Position,
            CourseItemId = courseItem.Id,
            AssignmentId = assignment.Id,
            Title = courseItem.Title,
            Position = courseItem.Position,
            Instruction = assignment.Instruction,
            DueDate = assignment.DueDate,
            MaxScore = assignment.MaxScore,
            IsLocked = !isAssignmentAccessible,
            MissingPrerequisites = missingAssignmentPrereqs,
            HasSubmission = latestSub != null,
            LatestSubmissionId = latestSub?.Id,
            LatestAttemptNumber = latestSub?.AttemptNumber ?? 0,
            LatestStatus = latestSub?.Status,
            LatestScore = latestSub?.Score,
            LatestFeedback = latestSub?.Feedback,
            LatestSubmittedAt = latestSub?.SubmittedAt,
            LatestGradedAt = latestSub?.GradedAt,
            LatestGradedByName = latestSub?.GradedBy?.FullName ?? latestSub?.GradedBy?.UserName,
            LatestFileUrl = latestSub?.FileUrl,
            LatestOriginalFileName = latestSub?.OriginalFileName,
            LatestExternalUrl = latestSub?.ExternalUrl,
            LatestTextResponse = latestSub?.TextResponse,
            LatestAttachments = latestSub?.Attachments.Where(a => !a.IsDeleted).Select(a => new StudentAttachmentViewModel
            {
                Id = a.Id,
                OriginalFileName = a.OriginalFileName,
                FileSizeBytes = a.FileSizeBytes,
                ContentType = a.ContentType,
                StoragePath = a.StoragePath,
                IsDeleted = a.IsDeleted
            }).ToList() ?? new(),
            SubmissionHistory = submissionHistory,
            Modules = nav.Modules,
            CourseProgressPercentage = nav.ProgressPercentage,
            CompletedLessonsCount = nav.CompletedLessonsCount,
            TotalLessonsCount = nav.TotalLessonsCount
        };
    }

    public async Task<(CurriculumOperationResult Result, AssignmentSubmission? Submission)> SubmitAssignmentAsync(
        int courseId,
        int courseItemId,
        string studentId,
        Microsoft.AspNetCore.Http.IFormFile? file,
        string? externalUrl,
        string? textResponse,
        IEnumerable<Microsoft.AspNetCore.Http.IFormFile>? files = null)
    {
        if (courseId <= 0 || courseItemId <= 0 || string.IsNullOrWhiteSpace(studentId))
        {
            return (CurriculumOperationResult.ValidationError("Invalid course, item, or student information."), null);
        }

        var isEnrolled = await _context.Enrollments.AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (!isEnrolled)
        {
            return (CurriculumOperationResult.Forbidden("Student is not enrolled in this course."), null);
        }

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Chapter)
            .Include(ci => ci.Assignment)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Assignment);

        if (courseItem == null || courseItem.Assignment == null || courseItem.Chapter == null)
        {
            return (CurriculumOperationResult.NotFound("Assignment not found."), null);
        }

        if (courseItem.Chapter.CourseId != courseId)
        {
            return (CurriculumOperationResult.ValidationError("Assignment does not belong to the specified course."), null);
        }

        var assignment = courseItem.Assignment;

        // Combine files
        var uploadFiles = new List<Microsoft.AspNetCore.Http.IFormFile>();
        if (files != null)
        {
            uploadFiles.AddRange(files.Where(f => f != null && f.Length > 0));
        }
        if (file != null && file.Length > 0 && !uploadFiles.Contains(file))
        {
            uploadFiles.Insert(0, file);
        }

        if (uploadFiles.Count > 5)
        {
            return (CurriculumOperationResult.ValidationError("You can upload a maximum of 5 files per submission."), null);
        }

        // Validate External URL
        string? validExternalUrl = null;
        if (!string.IsNullOrWhiteSpace(externalUrl))
        {
            validExternalUrl = _htmlSanitizer.SanitizeHttpUrl(externalUrl);
            if (validExternalUrl == null)
            {
                return (CurriculumOperationResult.ValidationError("Invalid URL protocol. Only HTTP and HTTPS URLs are permitted."), null);
            }
        }

        bool hasFiles = uploadFiles.Any();
        bool hasUrl = !string.IsNullOrWhiteSpace(validExternalUrl);
        bool hasText = !string.IsNullOrWhiteSpace(textResponse);

        if (!hasFiles && !hasUrl && !hasText)
        {
            return (CurriculumOperationResult.ValidationError("Please provide a file upload, external URL, or written text response."), null);
        }

        var createdAttachments = new List<AssignmentAttachment>();
        var newlySavedPaths = new List<string>();
        string? firstSavedPath = null;
        string? firstOriginalName = null;
        long? firstFileSizeBytes = null;
        string? firstContentType = null;

        async Task CleanupSavedFilesAsync()
        {
            if (_storageService == null || newlySavedPaths.Count == 0) return;

            foreach (var storedPath in newlySavedPaths)
            {
                try
                {
                    var deleted = await _storageService.DeleteAssignmentFileAsync(storedPath);
                    if (!deleted)
                    {
                        _logger.LogWarning("SubmitAssignmentAsync cleanup: File '{StoredPath}' was not deleted or already absent.", storedPath);
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "SubmitAssignmentAsync cleanup failed for '{StoredPath}'.", storedPath);
                }
            }
        }

        if (hasFiles)
        {
            if (_storageService == null)
            {
                return (CurriculumOperationResult.Failed("File storage service is not configured."), null);
            }

            foreach (var f in uploadFiles)
            {
                if (f.Length > 10 * 1024 * 1024)
                {
                    await CleanupSavedFilesAsync();
                    return (CurriculumOperationResult.ValidationError($"File '{f.FileName}' exceeds the maximum allowed size of 10 MB."), null);
                }

                var saveResult = await _storageService.SaveAssignmentFileAsync(f, studentId, assignment.Id);
                if (!saveResult.Success)
                {
                    await CleanupSavedFilesAsync();
                    return (CurriculumOperationResult.ValidationError(saveResult.ErrorMessage ?? $"File upload failed for '{f.FileName}'."), null);
                }

                if (!string.IsNullOrWhiteSpace(saveResult.StoredPath))
                {
                    newlySavedPaths.Add(saveResult.StoredPath);
                }

                if (firstSavedPath == null)
                {
                    firstSavedPath = saveResult.StoredPath;
                    firstOriginalName = saveResult.OriginalName;
                    firstFileSizeBytes = saveResult.FileSize;
                    firstContentType = saveResult.ContentType;
                }

                createdAttachments.Add(new AssignmentAttachment
                {
                    StoragePath = saveResult.StoredPath ?? string.Empty,
                    OriginalFileName = saveResult.OriginalName ?? f.FileName,
                    FileSizeBytes = saveResult.FileSize ?? f.Length,
                    ContentType = saveResult.ContentType ?? "application/octet-stream",
                    UploadedAt = DateTime.UtcNow,
                    IsDeleted = false
                });
            }
        }

        // Determine status (Late vs Submitted - late allowed with zero penalties)
        var now = DateTime.UtcNow;
        var status = now > assignment.DueDate ? SubmissionStatus.Late : SubmissionStatus.Submitted;

        // Count prior attempts to set AttemptNumber
        var priorSubmissions = await _context.AssignmentSubmissions
            .Where(s => s.AssignmentId == assignment.Id && s.StudentId == studentId)
            .ToListAsync();

        int nextAttemptNumber = priorSubmissions.Count + 1;

        // Mark previous submissions as not latest
        foreach (var prev in priorSubmissions)
        {
            prev.IsLatest = false;
        }

        var newSubmission = new AssignmentSubmission
        {
            AssignmentId = assignment.Id,
            StudentId = studentId,
            AttemptNumber = nextAttemptNumber,
            IsLatest = true,
            FileUrl = firstSavedPath ?? validExternalUrl,
            OriginalFileName = firstOriginalName,
            FileSizeBytes = firstFileSizeBytes,
            ContentType = firstContentType,
            ExternalUrl = validExternalUrl,
            TextResponse = hasText ? textResponse!.Trim() : null,
            SubmittedAt = now,
            Status = status,
            Attachments = createdAttachments
        };

        try
        {
            _context.AssignmentSubmissions.Add(newSubmission);
            await _context.SaveChangesAsync();
        }
        catch
        {
            await CleanupSavedFilesAsync();
            throw;
        }

        _logger.LogInformation("Student {StudentId} submitted Assignment {AssignmentId} (Attempt {Attempt}, Status {Status}, Attachments {Count}, ExternalUrl {HasUrl}).",
            studentId, assignment.Id, nextAttemptNumber, status, createdAttachments.Count, hasUrl);

        return (CurriculumOperationResult.Ok($"Assignment submitted successfully! (Attempt #{nextAttemptNumber}{(status == SubmissionStatus.Late ? " - Submitted Late" : "")})", newSubmission.Id), newSubmission);
    }

    // =========================================================================
    // PHASE 4 — BOOK READER
    // =========================================================================

    public async Task<StudentBookViewModel?> GetStudentBookAsync(int courseId, int courseItemId, string studentId, bool allowAdminPreview = false)
    {
        if (courseId <= 0 || courseItemId <= 0 || string.IsNullOrWhiteSpace(studentId)) return null;

        var isEnrolled = await _context.Enrollments.AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (!isEnrolled && !allowAdminPreview)
        {
            _logger.LogWarning("GetStudentBookAsync: Student {StudentId} is not enrolled in Course {CourseId}.", studentId, courseId);
            return null;
        }

        var courseItem = await _context.CourseItems
            .AsNoTracking()
            .Include(ci => ci.Chapter)
                .ThenInclude(ch => ch.Course)
                    .ThenInclude(c => c!.Teacher)
            .Include(ci => ci.Book)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Book);

        if (courseItem == null || courseItem.Book == null || courseItem.Chapter == null)
        {
            _logger.LogWarning("GetStudentBookAsync: Book CourseItem ID {CourseItemId} not found.", courseItemId);
            return null;
        }

        if (courseItem.Chapter.CourseId != courseId)
        {
            _logger.LogWarning("GetStudentBookAsync: CourseItem {CourseItemId} does not belong to Course {CourseId}.", courseItemId, courseId);
            return null;
        }

        var book = courseItem.Book;
        var course = courseItem.Chapter.Course!;

        var (isBookAccessible, missingBookPrereqs) = await CheckItemAccessAsync(courseItemId, studentId, allowAdminPreview);

        // Auto mark as completed for enrolled students only (Admin preview is strictly read-only and requires unlocked access)
        bool shouldMarkComplete = !allowAdminPreview && isBookAccessible;
        if (shouldMarkComplete)
        {
            await MarkLessonCompleteAsync(studentId, courseId, courseItemId);
        }

        var nav = await BuildStudentCurriculumNavigationAsync(courseId, studentId, courseItemId, courseItem.ChapterId, allowAdminPreview);

        var safeFileUrl = _htmlSanitizer.SanitizeResourceUrl(book.FileUrl) ?? string.Empty;

        string fileType = "PDF";
        if (!string.IsNullOrEmpty(safeFileUrl))
        {
            var ext = Path.GetExtension(safeFileUrl).TrimStart('.').ToUpperInvariant();
            if (!string.IsNullOrEmpty(ext)) fileType = ext;
        }

        return new StudentBookViewModel
        {
            CourseId = course.Id,
            CourseCode = course.Code,
            CourseTitle = course.Title,
            TeacherName = course.Teacher?.FullName ?? course.Teacher?.UserName,
            ModuleId = courseItem.Chapter.Id,
            ModuleTitle = courseItem.Chapter.Title,
            ModulePosition = courseItem.Chapter.Position,
            CourseItemId = courseItem.Id,
            BookId = book.Id,
            Title = courseItem.Title,
            Position = courseItem.Position,
            FileUrl = safeFileUrl,
            FileType = fileType,
            FileSize = book.FileSize,
            IsCompleted = shouldMarkComplete,
            IsLocked = !isBookAccessible,
            MissingPrerequisites = missingBookPrereqs,
            Modules = nav.Modules,
            CourseProgressPercentage = nav.ProgressPercentage,
            CompletedLessonsCount = nav.CompletedLessonsCount,
            TotalLessonsCount = nav.TotalLessonsCount
        };
    }

    // =========================================================================
    // PHASE 4 — STUDENT GRADEBOOK
    // =========================================================================

    public async Task<StudentGradesViewModel> GetStudentGradesAsync(string studentId, int? courseId = null)
    {
        var student = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId);
        var viewModel = new StudentGradesViewModel
        {
            StudentId = studentId,
            StudentName = student?.FullName ?? student?.UserName ?? "Student",
            UniversityId = student?.UniversityId,
            StudentEmail = student?.Email ?? "",
            Avatar = student?.Avatar,
            SelectedCourseId = courseId
        };

        // Load enrolled courses
        var enrollments = await _context.Enrollments
            .AsNoTracking()
            .Where(e => e.StudentId == studentId)
            .Include(e => e.Course)
            .OrderBy(e => e.Course.Code)
            .ToListAsync();

        viewModel.EnrolledCourses = enrollments.Select(e => new StudentGradeCourseOptionViewModel
        {
            CourseId = e.CourseId,
            Code = e.Course.Code,
            Title = e.Course.Title
        }).ToList();

        var enrolledCourseIds = enrollments.Select(e => e.CourseId).ToList();
        if (!enrolledCourseIds.Any())
        {
            return viewModel;
        }

        var targetCourseIds = courseId.HasValue && courseId.Value > 0 && enrolledCourseIds.Contains(courseId.Value)
            ? new List<int> { courseId.Value }
            : enrolledCourseIds;

        // Load assignments for target courses
        var assignments = await _context.Assignments
            .AsNoTracking()
            .Where(a => targetCourseIds.Contains(a.CourseItem.Chapter.CourseId))
            .Include(a => a.CourseItem)
                .ThenInclude(ci => ci.Chapter)
                    .ThenInclude(ch => ch.Course)
            .Include(a => a.Submissions.Where(s => s.StudentId == studentId))
                .ThenInclude(s => s.GradedBy)
            .ToListAsync();

        foreach (var a in assignments)
        {
            var latestSub = a.Submissions.FirstOrDefault(s => s.IsLatest)
                ?? a.Submissions.OrderByDescending(s => s.AttemptNumber).FirstOrDefault();

            bool isCompleted = latestSub != null && latestSub.Status == SubmissionStatus.Graded;
            double? score = latestSub?.Score;
            double maxScore = a.MaxScore > 0 ? a.MaxScore : 100.0;
            bool isPassed = score.HasValue && (score.Value / maxScore) >= 0.5; // 50% threshold

            string statusDisplay = "Not Submitted";
            if (latestSub != null)
            {
                statusDisplay = latestSub.Status switch
                {
                    SubmissionStatus.Graded => isPassed ? "Passed" : "Needs Improvement",
                    SubmissionStatus.Submitted => "Submitted",
                    SubmissionStatus.Late => "Submitted Late",
                    _ => latestSub.Status.ToString()
                };
            }

            viewModel.GradeItems.Add(new StudentGradeItemViewModel
            {
                CourseId = a.CourseItem.Chapter.CourseId,
                CourseCode = a.CourseItem.Chapter.Course.Code,
                CourseTitle = a.CourseItem.Chapter.Course.Title,
                AssessmentId = a.Id,
                CourseItemId = a.CourseItemId,
                AssessmentTitle = a.CourseItem.Title,
                AssessmentType = "Assignment",
                Score = score,
                MaxScore = maxScore,
                IsPassed = isPassed,
                IsCompleted = isCompleted,
                StatusDisplay = statusDisplay,
                Date = latestSub?.SubmittedAt ?? a.DueDate,
                Feedback = latestSub?.Feedback,
                GradedByName = latestSub?.GradedBy?.FullName ?? latestSub?.GradedBy?.UserName,
                DetailUrl = $"/Student/Course/{a.CourseItem.Chapter.CourseId}/Assignment/{a.CourseItemId}"
            });
        }

        // Load quizzes for target courses (only published quizzes, or quizzes with existing student submissions)
        var quizzes = await _context.Quizzes
            .AsNoTracking()
            .Where(q => targetCourseIds.Contains(q.CourseItem.Chapter.CourseId) &&
                        (q.IsPublished || q.Submissions.Any(s => s.StudentId == studentId)))
            .Include(q => q.CourseItem)
                .ThenInclude(ci => ci.Chapter)
                    .ThenInclude(ch => ch.Course)
            .Include(q => q.Submissions.Where(s => s.StudentId == studentId))
            .ToListAsync();

        foreach (var q in quizzes)
        {
            var bestSub = q.Submissions.OrderByDescending(s => s.Score).FirstOrDefault();
            bool isCompleted = bestSub != null;
            double? score = bestSub?.Score;
            double maxScore = 100.0;
            bool isPassed = bestSub != null && (bestSub.Score >= 50.0);

            string statusDisplay = "Not Attempted";
            if (bestSub != null)
            {
                statusDisplay = isPassed ? "Passed" : "Failed";
            }

            viewModel.GradeItems.Add(new StudentGradeItemViewModel
            {
                CourseId = q.CourseItem.Chapter.CourseId,
                CourseCode = q.CourseItem.Chapter.Course.Code,
                CourseTitle = q.CourseItem.Chapter.Course.Title,
                AssessmentId = q.Id,
                CourseItemId = q.CourseItemId,
                AssessmentTitle = q.CourseItem.Title,
                AssessmentType = "Quiz",
                Score = score,
                MaxScore = maxScore,
                IsPassed = isPassed,
                IsCompleted = isCompleted,
                StatusDisplay = statusDisplay,
                Date = bestSub?.SubmittedAt ?? DateTime.UtcNow,
                Feedback = isPassed ? $"Passed with score {score}%" : (score.HasValue ? $"Score: {score}% (Passing: 50%)" : null),
                DetailUrl = $"/Student/Course/{q.CourseItem.Chapter.CourseId}/Quiz/{q.CourseItemId}"
            });
        }

        viewModel.GradeItems = viewModel.GradeItems.OrderByDescending(g => g.Date).ToList();

        return viewModel;
    }

    // =========================================================================
    // PHASE 5A — COURSE ACCESS MODES (FREE vs SEQUENTIAL) & PREREQUISITES
    // =========================================================================

    public async Task<CourseAccessMode> GetCourseAccessModeAsync(int courseId)
    {
        if (courseId <= 0) return CourseAccessMode.Free;
        return await _context.Courses
            .Where(c => c.Id == courseId)
            .Select(c => c.AccessMode)
            .FirstOrDefaultAsync();
    }

    public async Task<CurriculumOperationResult> UpdateCourseAccessModeAsync(int courseId, CourseAccessMode mode, string userId, bool isAdmin)
    {
        if (courseId <= 0) return CurriculumOperationResult.ValidationError("Invalid course ID.");
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
        if (course == null) return CurriculumOperationResult.NotFound("Course not found.");

        if (!isAdmin && course.TeacherId != userId)
        {
            return CurriculumOperationResult.Forbidden("You are not authorized to modify settings for this course.");
        }

        course.AccessMode = mode;
        course.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated Course {CourseId} AccessMode to {Mode} by user {UserId}.", courseId, mode, userId);
        return CurriculumOperationResult.Success(course.Id, course.Title, $"Course access mode set to {mode}.");
    }

    public async Task<List<int>> GetItemPrerequisiteIdsAsync(int courseItemId)
    {
        if (courseItemId <= 0) return new List<int>();
        return await _context.CourseItemPrerequisites
            .Where(p => p.CourseItemId == courseItemId)
            .Select(p => p.RequiredCourseItemId)
            .ToListAsync();
    }

    public async Task<CurriculumOperationResult> SetItemPrerequisitesAsync(int courseItemId, IEnumerable<int> requiredItemIds, string userId, bool isAdmin)
    {
        if (courseItemId <= 0) return CurriculumOperationResult.ValidationError("Invalid course item ID.");

        var item = await _context.CourseItems
            .Include(ci => ci.Chapter)
                .ThenInclude(ch => ch.Course)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId);

        if (item == null) return CurriculumOperationResult.NotFound("Course item not found.");
        var course = item.Chapter.Course;

        if (!isAdmin && course.TeacherId != userId)
        {
            return CurriculumOperationResult.Forbidden("You are not authorized to manage prerequisites for this course.");
        }

        return await SetItemPrerequisitesInternalAsync(courseItemId, requiredItemIds, course.Id);
    }

    private async Task<CurriculumOperationResult> SetItemPrerequisitesInternalAsync(int courseItemId, IEnumerable<int>? requiredItemIds, int courseId)
    {
        var distinctReqIds = (requiredItemIds ?? Enumerable.Empty<int>())
            .Where(id => id > 0 && id != courseItemId)
            .Distinct()
            .ToList();

        if (distinctReqIds.Any())
        {
            var validCount = await _context.CourseItems
                .Where(ci => distinctReqIds.Contains(ci.Id) && ci.Chapter.CourseId == courseId)
                .CountAsync();

            if (validCount != distinctReqIds.Count)
            {
                return CurriculumOperationResult.ValidationError("All prerequisites must belong to the same course.");
            }

            var hasCircular = await _context.CourseItemPrerequisites
                .Where(p => distinctReqIds.Contains(p.CourseItemId) && p.RequiredCourseItemId == courseItemId)
                .AnyAsync();

            if (hasCircular)
            {
                return CurriculumOperationResult.ValidationError("Circular prerequisite dependency detected.");
            }
        }

        var existing = await _context.CourseItemPrerequisites
            .Where(p => p.CourseItemId == courseItemId)
            .ToListAsync();

        _context.CourseItemPrerequisites.RemoveRange(existing);

        foreach (var reqId in distinctReqIds)
        {
            _context.CourseItemPrerequisites.Add(new CourseItemPrerequisite
            {
                CourseItemId = courseItemId,
                RequiredCourseItemId = reqId
            });
        }

        await _context.SaveChangesAsync();
        return CurriculumOperationResult.Success(courseItemId, string.Empty, "Prerequisites updated successfully.");
    }

    public async Task<(bool IsAccessible, List<string> MissingPrerequisites)> CheckItemAccessAsync(int courseItemId, string studentId, bool allowAdminPreview = false)
    {
        if (allowAdminPreview) return (true, new List<string>());
        if (courseItemId <= 0 || string.IsNullOrWhiteSpace(studentId)) return (false, new List<string> { "Invalid item or student." });

        var item = await _context.CourseItems
            .Include(ci => ci.Chapter)
                .ThenInclude(ch => ch.Course)
            .AsNoTracking()
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId);

        if (item == null || item.Chapter?.Course == null) return (false, new List<string> { "Item not found." });
        var course = item.Chapter.Course;

        if (course.AccessMode == CourseAccessMode.Free)
        {
            return (true, new List<string>());
        }

        var prerequisites = await _context.CourseItemPrerequisites
            .Include(p => p.RequiredCourseItem)
            .Where(p => p.CourseItemId == courseItemId)
            .AsNoTracking()
            .ToListAsync();

        if (!prerequisites.Any())
        {
            return (true, new List<string>());
        }

        var reqIds = prerequisites.Select(p => p.RequiredCourseItemId).ToList();

        var completedProgressIds = await _context.CourseProgresses
            .Where(cp => cp.StudentId == studentId && cp.IsCompleted && reqIds.Contains(cp.CourseItemId))
            .Select(cp => cp.CourseItemId)
            .ToListAsync();

        var completedVideoIds = await _context.VideoLessonProgresses
            .Where(vp => vp.StudentId == studentId && vp.IsCompleted && reqIds.Contains(vp.CourseItemId))
            .Select(vp => vp.CourseItemId)
            .ToListAsync();

        var passedQuizIds = await _context.QuizSubmissions
            .Where(qs => qs.StudentId == studentId && qs.IsPassed && reqIds.Contains(qs.Quiz.CourseItemId))
            .Select(qs => qs.Quiz.CourseItemId)
            .ToListAsync();

        var submittedAssignmentIds = await _context.AssignmentSubmissions
            .Where(s => s.StudentId == studentId && reqIds.Contains(s.Assignment.CourseItemId) &&
                        (s.Status == SubmissionStatus.Graded || s.Status == SubmissionStatus.Submitted || s.Status == SubmissionStatus.Late))
            .Select(s => s.Assignment.CourseItemId)
            .ToListAsync();

        var completedSet = completedProgressIds
            .Concat(completedVideoIds)
            .Concat(passedQuizIds)
            .Concat(submittedAssignmentIds)
            .ToHashSet();

        var missing = prerequisites
            .Where(p => !completedSet.Contains(p.RequiredCourseItemId))
            .Select(p => p.RequiredCourseItem?.Title ?? $"Item #{p.RequiredCourseItemId}")
            .Distinct()
            .ToList();

        if (missing.Any())
        {
            return (false, missing);
        }

        return (true, new List<string>());
    }
}
