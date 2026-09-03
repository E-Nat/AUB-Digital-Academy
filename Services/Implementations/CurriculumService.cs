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

    public CurriculumService(ApplicationDbContext context, ILogger<CurriculumService> logger)
    {
        _context = context;
        _logger = logger;
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
                            ContentHtml = ci.Lesson?.ContentHtml
                        })
                        .ToList()
                })
                .ToList(),
            ModuleCreateModel = new ModuleCreateViewModel { CourseId = course.Id },
            LessonCreateModel = new LessonCreateViewModel { CourseId = course.Id }
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

    public async Task<LessonEditViewModel?> GetLessonAsync(int courseItemId)
    {
        if (courseItemId <= 0) return null;

        var item = await _context.CourseItems
            .Include(ci => ci.Lesson)
            .Include(ci => ci.Chapter)
            .AsNoTracking()
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Lesson);

        if (item == null) return null;

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

    public async Task<CurriculumOperationResult> CreateLessonAsync(
        int chapterId,
        string title,
        string? contentHtml,
        string? videoUrl,
        int? position = null)
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

            return CurriculumOperationResult.Success(courseItem.Id, courseItem.Title, "Lesson created successfully.", chapterId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to create lesson '{Title}' under Chapter {ChapterId}.", title, chapterId);
            return CurriculumOperationResult.Failed("An unexpected database error occurred while creating the lesson.");
        }
    }

    public async Task<CurriculumOperationResult> UpdateLessonAsync(
        int courseItemId,
        string title,
        string? contentHtml,
        string? videoUrl,
        int position)
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
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Lesson);

        if (courseItem == null)
        {
            return CurriculumOperationResult.NotFound("Lesson not found.");
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
            return CurriculumOperationResult.Success(courseItem.Id, courseItem.Title, "Lesson updated successfully.", courseItem.ChapterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update lesson ID {CourseItemId}.", courseItemId);
            return CurriculumOperationResult.Failed("An unexpected database error occurred while updating the lesson.");
        }
    }

    public async Task<CurriculumOperationResult> DeleteLessonAsync(int courseItemId)
    {
        if (courseItemId <= 0)
        {
            return CurriculumOperationResult.ValidationError("Invalid course item ID.");
        }

        var courseItem = await _context.CourseItems
            .Include(ci => ci.Lesson)
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Lesson);

        if (courseItem == null)
        {
            return CurriculumOperationResult.NotFound("Lesson not found.");
        }

        var progressCount = await _context.CourseProgresses
            .CountAsync(cp => cp.CourseItemId == courseItemId);

        if (progressCount > 0)
        {
            var warning = $"Cannot delete lesson '{courseItem.Title}' because {progressCount} student(s) have recorded progress on this item.";
            _logger.LogWarning("Safe delete blocked for lesson ID {CourseItemId}: {Warning}", courseItemId, warning);
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
            _logger.LogInformation("Lesson ID {CourseItemId} ('{Title}') deleted cleanly.", courseItemId, courseItem.Title);
            return CurriculumOperationResult.Success(courseItemId, courseItem.Title, "Lesson deleted successfully.", courseItem.ChapterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete lesson ID {CourseItemId}.", courseItemId);
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
}
