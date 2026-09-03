using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface ICurriculumService
{
    /// <summary>
    /// Retrieves the complete curriculum hierarchy (Course -> Chapters -> CourseItems -> Lessons) for a course.
    /// </summary>
    Task<CourseCurriculumViewModel?> GetCurriculumAsync(int courseId);

    /// <summary>
    /// Gets all Chapters (Modules) for a specific Course, ordered by Position.
    /// </summary>
    Task<IEnumerable<Chapter>> GetChaptersByCourseIdAsync(int courseId);

    /// <summary>
    /// Gets all Modules with their items as ViewModels for a specific Course.
    /// </summary>
    Task<List<CurriculumModuleViewModel>> GetModulesByCourseIdAsync(int courseId);

    /// <summary>
    /// Gets a Chapter (Module) by its primary key ID.
    /// </summary>
    Task<Chapter?> GetChapterByIdAsync(int chapterId);

    /// <summary>
    /// Creates a new Chapter (Module) under a course.
    /// </summary>
    Task<CurriculumOperationResult> CreateChapterAsync(int courseId, string title, int? position = null);

    /// <summary>
    /// Updates an existing Chapter (Module) title and position, with optional course ownership verification.
    /// </summary>
    Task<CurriculumOperationResult> UpdateChapterAsync(int chapterId, string title, int position, int? expectedCourseId = null);

    /// <summary>
    /// Safely deletes a Chapter (Module) if no student activity is attached to its items, with optional course ownership verification.
    /// </summary>
    Task<CurriculumOperationResult> DeleteChapterAsync(int chapterId, int? expectedCourseId = null);

    /// <summary>
    /// Gets a Lesson for editing via its parent CourseItem ID.
    /// </summary>
    Task<LessonEditViewModel?> GetLessonAsync(int courseItemId);

    /// <summary>
    /// Creates a new CourseItem of Type=Lesson with its 1:1 child Lesson record.
    /// </summary>
    Task<CurriculumOperationResult> CreateLessonAsync(int chapterId, string title, string? contentHtml, string? videoUrl, int? position = null);

    /// <summary>
    /// Updates an existing Lesson and its parent CourseItem title and position.
    /// </summary>
    Task<CurriculumOperationResult> UpdateLessonAsync(int courseItemId, string title, string? contentHtml, string? videoUrl, int position);

    /// <summary>
    /// Safely deletes a Lesson (CourseItem + Lesson) if no student completion record exists.
    /// </summary>
    Task<CurriculumOperationResult> DeleteLessonAsync(int courseItemId);

    /// <summary>
    /// Helper to compute the next auto-incremented position for a Chapter in a Course.
    /// </summary>
    Task<int> GetNextChapterPositionAsync(int courseId);

    /// <summary>
    /// Helper to compute the next auto-incremented position for a CourseItem in a Chapter.
    /// </summary>
    Task<int> GetNextItemPositionAsync(int chapterId);
}
