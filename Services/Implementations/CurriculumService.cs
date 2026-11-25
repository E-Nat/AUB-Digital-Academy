using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

public class CurriculumService : ICurriculumService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CurriculumService> _logger;
    private readonly IAssignmentStorageService? _storageService;

    public CurriculumService(
        ApplicationDbContext context,
        ILogger<CurriculumService> logger,
        IAssignmentStorageService? storageService = null)
    {
        _context = context;
        _logger = logger;
        _storageService = storageService;
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

        var viewModel = new CourseCurriculumViewModel
        {
            CourseId = course.Id,
            CourseCode = course.Code,
            CourseTitle = course.Title,
            CourseDescription = course.Description,
            Thumbnail = course.Thumbnail,
            Status = course.Status,
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
                            QuestionCount = ci.Quiz?.Questions?.Count ?? 0
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

        var assignmentSubCount = await _context.AssignmentSubmissions
            .CountAsync(asub => asub.Assignment.CourseItem.ChapterId == chapterId);

        int totalStudentData = progressCount + quizSubCount + assignmentSubCount;
        if (totalStudentData > 0)
        {
            var warning = $"Cannot delete module '{chapter.Title}' because it contains active student learning records ({progressCount} progress records, {quizSubCount + assignmentSubCount} submissions).";
            _logger.LogWarning("Safe delete blocked for module ID {ChapterId}: {Warning}", chapterId, warning);
            return CurriculumOperationResult.BlockedByStudentData(
                chapter.Id,
                chapter.Title,
                warning,
                progressCount,
                quizSubCount + assignmentSubCount);
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

        return new LessonEditViewModel
        {
            CourseItemId = item.Id,
            LessonId = item.Lesson?.Id ?? 0,
            ChapterId = item.ChapterId,
            CourseId = item.Chapter?.CourseId ?? 0,
            Title = item.Title,
            Position = item.Position,
            VideoUrl = item.Lesson?.VideoUrl,
            ContentHtml = item.Lesson?.ContentHtml
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
                ContentHtml = string.IsNullOrWhiteSpace(contentHtml) ? null : contentHtml.Trim(),
                VideoUrl = string.IsNullOrWhiteSpace(videoUrl) ? null : videoUrl.Trim()
            };
            _context.Lessons.Add(lesson);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation("Lesson '{Title}' created with CourseItemID {CourseItemId} and LessonID {LessonId}.",
                courseItem.Title, courseItem.Id, lesson.Id);

            return CurriculumOperationResult.Success(courseItem.Id, courseItem.Title, "Lesson created successfully.", chapterId, lesson.Id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to create lesson '{Title}' under Chapter {ChapterId}.", title, chapterId);
            return CurriculumOperationResult.Failed("An unexpected database error occurred while creating the lesson.");
        }
    }

    public Task<CurriculumOperationResult> CreateLessonAsync(LessonCreateViewModel model)
    {
        if (model == null) return Task.FromResult(CurriculumOperationResult.ValidationError("Lesson creation payload is required."));
        return CreateLessonAsync(
            model.ChapterId,
            model.Title,
            model.ContentHtml,
            model.VideoUrl,
            model.Position,
            model.CourseId > 0 ? model.CourseId : null);
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
                ContentHtml = string.IsNullOrWhiteSpace(contentHtml) ? null : contentHtml.Trim(),
                VideoUrl = string.IsNullOrWhiteSpace(videoUrl) ? null : videoUrl.Trim()
            };
            _context.Lessons.Add(courseItem.Lesson);
        }
        else
        {
            courseItem.Lesson.ContentHtml = string.IsNullOrWhiteSpace(contentHtml) ? null : contentHtml.Trim();
            courseItem.Lesson.VideoUrl = string.IsNullOrWhiteSpace(videoUrl) ? null : videoUrl.Trim();
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

    public Task<CurriculumOperationResult> UpdateLessonAsync(LessonEditViewModel model)
    {
        if (model == null) return Task.FromResult(CurriculumOperationResult.ValidationError("Lesson update payload is required."));
        int targetId = model.CourseItemId > 0 ? model.CourseItemId : model.LessonId;
        return UpdateLessonAsync(
            targetId,
            model.Title,
            model.ContentHtml,
            model.VideoUrl,
            model.Position,
            model.ChapterId > 0 ? model.ChapterId : null,
            model.CourseId > 0 ? model.CourseId : null);
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

    public async Task<StudentLessonPlayerViewModel?> GetStudentLessonPlayerAsync(int courseId, int lessonId, string? studentId = null)
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
        if (!string.IsNullOrWhiteSpace(studentId))
        {
            var completedList = await _context.CourseProgresses
                .AsNoTracking()
                .Where(cp => cp.StudentId == studentId && cp.IsCompleted && cp.CourseItem.Chapter.CourseId == courseId)
                .Select(cp => cp.CourseItemId)
                .ToListAsync();
            completedItemIds = new HashSet<int>(completedList);
        }

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
                    .Where(ci => (ci.Type == ItemType.Lesson && ci.Lesson != null) || (ci.Type == ItemType.Quiz && ci.Quiz != null))
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
                        IsCurrent = ci.Id == current.CourseItem.Id,
                        IsCompleted = completedItemIds.Contains(ci.Id)
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
            ContentHtml = current.Lesson.ContentHtml,
            VideoUrl = current.Lesson.VideoUrl,
            IsCompleted = completedItemIds.Contains(current.CourseItem.Id),
            CurrentLessonIndex = currentIndex + 1,
            TotalLessonsCount = orderedLessons.Count,
            CompletedLessonsCount = completedItemIds.Count,
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

        // Idempotent duplicate check: Check if CourseProgress already exists for this student and CourseItem
        var existingProgress = await _context.CourseProgresses
            .FirstOrDefaultAsync(cp => cp.StudentId == studentId && cp.CourseItemId == courseItemId);

        if (existingProgress != null)
        {
            _logger.LogInformation("MarkLessonCompleteAsync: Lesson ID {CourseItemId} is already completed by student {StudentId}.", courseItemId, studentId);
            return CurriculumOperationResult.Success(courseItemId, courseItem.Title, "Lesson is already marked as complete.");
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

    public async Task<int> GetStudentCompletedLessonsCountAsync(string studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId)) return 0;

        return await _context.CourseProgresses
            .AsNoTracking()
            .CountAsync(cp => cp.StudentId == studentId && cp.IsCompleted);
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
                .Where(cp => cp.StudentId == studentId && cp.IsCompleted && idList.Contains(cp.CourseItem.Chapter.CourseId))
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

        if (courseItem.Quiz != null && courseItem.Quiz.Submissions.Any())
        {
            _logger.LogWarning("Safe delete blocked for Quiz ID {QuizId}: Student submissions exist.", courseItem.Quiz.Id);
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
            FileUrl = model.FileUrl.Trim(),
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
        courseItem.Book.FileUrl = model.FileUrl.Trim();
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

    public async Task<StudentQuizPlayerViewModel?> GetStudentQuizPlayerAsync(int courseId, int courseItemId, string studentId)
    {
        if (courseId <= 0 || courseItemId <= 0 || string.IsNullOrWhiteSpace(studentId)) return null;

        // Enrollment check
        var isEnrolled = await _context.Enrollments.AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (!isEnrolled)
        {
            _logger.LogWarning("GetStudentQuizPlayerAsync: Student {StudentId} is not enrolled in Course {CourseId}.", studentId, courseId);
            return null;
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

        // Student submissions for this quiz
        var allSubmissions = await _context.QuizSubmissions
            .AsNoTracking()
            .Where(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId)
            .OrderByDescending(qs => qs.SubmittedAt)
            .ToListAsync();

        var latestSubmission = allSubmissions.FirstOrDefault();
        var firstSubmission = allSubmissions.LastOrDefault();
        int effectiveMaxAttempts = firstSubmission != null && firstSubmission.MaxAttemptsAllowed > 0
            ? firstSubmission.MaxAttemptsAllowed
            : (quiz.MaxAttempts > 0 ? quiz.MaxAttempts : 3);
        var bestScore = allSubmissions.Any() ? allSubmissions.Max(s => s.Score) : (double?)null;

        // Query completed lesson IDs for progress
        var completedList = await _context.CourseProgresses
            .AsNoTracking()
            .Where(cp => cp.StudentId == studentId && cp.IsCompleted && cp.CourseItem.Chapter.CourseId == courseId)
            .Select(cp => cp.CourseItemId)
            .ToListAsync();
        var completedItemIds = new HashSet<int>(completedList);

        var totalLessons = course.Chapters.SelectMany(ch => ch.CourseItems.Where(ci => ci.Type == ItemType.Lesson)).Count();
        var progressPct = totalLessons > 0 ? (int)Math.Round((double)completedItemIds.Count / totalLessons * 100) : 0;

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
                    .Where(ci => (ci.Type == ItemType.Lesson && ci.Lesson != null) || (ci.Type == ItemType.Quiz && ci.Quiz != null))
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
            MaxAttempts = effectiveMaxAttempts,
            PassingScore = quiz.PassingScore,
            IsPublished = quiz.IsPublished,
            HasSubmission = latestSubmission != null,
            AttemptCount = allSubmissions.Count,
            LatestSubmissionId = latestSubmission?.Id,
            LatestScore = latestSubmission?.Score,
            BestScore = bestScore,
            LatestIsPassed = latestSubmission?.IsPassed,
            LatestSubmittedAt = latestSubmission?.SubmittedAt,
            CourseProgressPercentage = progressPct,
            CompletedLessonsCount = completedItemIds.Count,
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

    public async Task<(CurriculumOperationResult Result, StudentQuizResultViewModel? Outcome)> SubmitQuizAsync(
        string studentId, int courseId, int courseItemId, Dictionary<int, int> selectedOptions)
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
        var isEnrolled = await _context.Enrollments.AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (!isEnrolled)
        {
            _logger.LogWarning("SubmitQuizAsync: Student {StudentId} is not enrolled in Course {CourseId}.", studentId, courseId);
            return (CurriculumOperationResult.Forbidden("Student is not enrolled in this course."), null);
        }

        var courseItem = await _context.CourseItems
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

        // Query student's first attempt to determine effective MaxAttempts (future-only limit rule)
        var firstAttempt = await _context.QuizSubmissions
            .AsNoTracking()
            .Where(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId)
            .OrderBy(qs => qs.SubmittedAt)
            .FirstOrDefaultAsync();

        int effectiveMaxAttempts = firstAttempt != null && firstAttempt.MaxAttemptsAllowed > 0
            ? firstAttempt.MaxAttemptsAllowed
            : (quiz.MaxAttempts > 0 ? quiz.MaxAttempts : 3);

        // Enforce attempt limit
        var existingAttemptsCount = await _context.QuizSubmissions
            .CountAsync(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId);
        if (existingAttemptsCount >= effectiveMaxAttempts)
        {
            _logger.LogWarning("SubmitQuizAsync: Student {StudentId} has reached MaxAttempts ({Max}) for Quiz {QuizId}.",
                studentId, effectiveMaxAttempts, quiz.Id);
            return (CurriculumOperationResult.Forbidden($"You have reached the maximum allowed attempts ({effectiveMaxAttempts}) for this quiz."), null);
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
            if (selectedOptions != null && selectedOptions.TryGetValue(q.Id, out int optId))
            {
                selectedOptId = optId;
            }

            var correctOpt = q.Options.FirstOrDefault(o => o.IsCorrect);
            var selectedOpt = selectedOptId.HasValue ? q.Options.FirstOrDefault(o => o.Id == selectedOptId.Value) : null;
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
                CreatedAt = DateTime.UtcNow
            });
        }

        double score = totalPoints > 0 ? Math.Round((earnedPoints / totalPoints) * 100.0, 2) : 0.0;
        bool isPassed = totalPoints > 0 && score >= 50.0;

        var submission = new QuizSubmission
        {
            QuizId = quiz.Id,
            StudentId = studentId,
            Score = score,
            IsPassed = isPassed,
            MaxAttemptsAllowed = effectiveMaxAttempts,
            SubmittedAt = DateTime.UtcNow
        };

        _context.QuizSubmissions.Add(submission);
        await _context.SaveChangesAsync();

        // Persist individual answers linked to this submission
        foreach (var ans in answersToInsert)
        {
            ans.QuizSubmissionId = submission.Id;
            ans.CreatedAt = submission.SubmittedAt;
        }
        _context.QuizSubmissionAnswers.AddRange(answersToInsert);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Student {StudentId} submitted Quiz ID {QuizId} (Submission ID {SubmissionId}, Attempt {Attempt}/{Max}): Score={Score}%, IsPassed={IsPassed}.",
            studentId, quiz.Id, submission.Id, existingAttemptsCount + 1, quiz.MaxAttempts, score, isPassed);

        var allAttemptScores = await _context.QuizSubmissions
            .Where(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId)
            .Select(qs => qs.Score)
            .ToListAsync();
        double bestScore = allAttemptScores.Any() ? allAttemptScores.Max() : score;

        var outcome = new StudentQuizResultViewModel
        {
            CourseId = courseId,
            CourseCode = courseItem.Chapter.Course?.Code ?? "",
            CourseTitle = courseItem.Chapter.Course?.Title ?? "",
            CourseItemId = courseItemId,
            QuizId = quiz.Id,
            QuizTitle = courseItem.Title,
            PassingScore = 50.0,
            SubmissionId = submission.Id,
            AttemptNumber = existingAttemptsCount + 1,
            MaxAttempts = effectiveMaxAttempts,
            AreAttemptsExhausted = areAttemptsExhausted,
            Score = score,
            BestScore = bestScore,
            IsPassed = isPassed,
            SubmittedAt = submission.SubmittedAt,
            TotalPoints = totalPoints,
            EarnedPoints = earnedPoints,
            TotalQuestions = questions.Count,
            CorrectQuestionsCount = correctCount,
            Questions = questionBreakdown
        };

        return (CurriculumOperationResult.Ok("Quiz submitted successfully.", submission.Id), outcome);
    }

    public async Task<StudentQuizResultViewModel?> GetStudentQuizResultAsync(int courseId, int courseItemId, int submissionId, string studentId)
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

        // Student isolation check: student can only view their own submission
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
            .Where(qs => qs.QuizId == quiz.Id && qs.StudentId == studentId)
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

    private async Task<(List<StudentPlayerModuleViewModel> Modules, int CompletedLessonsCount, int TotalLessonsCount, int ProgressPercentage)> BuildStudentCurriculumNavigationAsync(
        int courseId,
        string studentId,
        int currentCourseItemId,
        int currentChapterId)
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

        var completedList = await _context.CourseProgresses
            .AsNoTracking()
            .Where(cp => cp.StudentId == studentId && cp.IsCompleted && cp.CourseItem.Chapter.CourseId == courseId)
            .Select(cp => cp.CourseItemId)
            .ToListAsync();
        var completedItemIds = new HashSet<int>(completedList);

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
                            HasPassedQuiz = isPassedQuiz,
                            BestQuizScore = bestQuizScore
                        };
                    }).ToList()
            }).ToList();

        int totalCompleted = completedItemIds.Count;
        int progressPct = totalLessons > 0 ? (int)Math.Round((double)totalCompleted / totalLessons * 100) : 0;
        if (progressPct > 100) progressPct = 100;

        return (modules, totalCompleted, totalLessons, progressPct);
    }

    // =========================================================================
    // PHASE 4 — ASSIGNMENT STUDENT PLAYER & SUBMISSION
    // =========================================================================

    public async Task<StudentAssignmentViewModel?> GetStudentAssignmentAsync(int courseId, int courseItemId, string studentId)
    {
        if (courseId <= 0 || courseItemId <= 0 || string.IsNullOrWhiteSpace(studentId)) return null;

        var isEnrolled = await _context.Enrollments.AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (!isEnrolled)
        {
            _logger.LogWarning("GetStudentAssignmentAsync: Student {StudentId} is not enrolled in Course {CourseId}.", studentId, courseId);
            return null;
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

        var nav = await BuildStudentCurriculumNavigationAsync(courseId, studentId, courseItemId, courseItem.ChapterId);

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
            var trimmedUrl = externalUrl.Trim();
            var lower = trimmedUrl.ToLowerInvariant();
            if (lower.StartsWith("javascript:") || lower.StartsWith("data:") || lower.StartsWith("file:") || lower.StartsWith("vbscript:"))
            {
                return (CurriculumOperationResult.ValidationError("Invalid URL protocol. Only HTTP and HTTPS URLs are permitted."), null);
            }
            if (!trimmedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !trimmedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return (CurriculumOperationResult.ValidationError("URL must begin with http:// or https://."), null);
            }
            validExternalUrl = trimmedUrl;
        }

        bool hasFiles = uploadFiles.Any();
        bool hasUrl = !string.IsNullOrWhiteSpace(validExternalUrl);
        bool hasText = !string.IsNullOrWhiteSpace(textResponse);

        if (!hasFiles && !hasUrl && !hasText)
        {
            return (CurriculumOperationResult.ValidationError("Please provide a file upload, external URL, or written text response."), null);
        }

        var createdAttachments = new List<AssignmentAttachment>();
        string? firstSavedPath = null;
        string? firstOriginalName = null;
        long? firstFileSizeBytes = null;
        string? firstContentType = null;

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
                    return (CurriculumOperationResult.ValidationError($"File '{f.FileName}' exceeds the maximum allowed size of 10 MB."), null);
                }

                var saveResult = await _storageService.SaveAssignmentFileAsync(f, studentId, assignment.Id);
                if (!saveResult.Success)
                {
                    return (CurriculumOperationResult.ValidationError(saveResult.ErrorMessage ?? $"File upload failed for '{f.FileName}'."), null);
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

        _context.AssignmentSubmissions.Add(newSubmission);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Student {StudentId} submitted Assignment {AssignmentId} (Attempt {Attempt}, Status {Status}, Attachments {Count}, ExternalUrl {HasUrl}).",
            studentId, assignment.Id, nextAttemptNumber, status, createdAttachments.Count, hasUrl);

        return (CurriculumOperationResult.Ok($"Assignment submitted successfully! (Attempt #{nextAttemptNumber}{(status == SubmissionStatus.Late ? " - Submitted Late" : "")})", newSubmission.Id), newSubmission);
    }

    // =========================================================================
    // PHASE 4 — BOOK READER
    // =========================================================================

    public async Task<StudentBookViewModel?> GetStudentBookAsync(int courseId, int courseItemId, string studentId)
    {
        if (courseId <= 0 || courseItemId <= 0 || string.IsNullOrWhiteSpace(studentId)) return null;

        var isEnrolled = await _context.Enrollments.AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (!isEnrolled)
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

        // Auto mark as completed for course progress
        await MarkLessonCompleteAsync(studentId, courseId, courseItemId);

        var nav = await BuildStudentCurriculumNavigationAsync(courseId, studentId, courseItemId, courseItem.ChapterId);

        string fileType = "PDF";
        if (!string.IsNullOrEmpty(book.FileUrl))
        {
            var ext = Path.GetExtension(book.FileUrl).TrimStart('.').ToUpperInvariant();
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
            FileUrl = book.FileUrl ?? "",
            FileType = fileType,
            FileSize = book.FileSize,
            IsCompleted = true,
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

        // Load quizzes for target courses
        var quizzes = await _context.Quizzes
            .AsNoTracking()
            .Where(q => targetCourseIds.Contains(q.CourseItem.Chapter.CourseId))
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
}
