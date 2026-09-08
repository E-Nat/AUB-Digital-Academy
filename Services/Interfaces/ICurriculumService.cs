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
    /// Creates a new Chapter (Module) using the ModuleCreateViewModel prepared in STEP 2.2.
    /// </summary>
    Task<CurriculumOperationResult> CreateChapterAsync(ModuleCreateViewModel model);

    /// <summary>
    /// Updates an existing Chapter (Module) title and position, with optional course ownership verification.
    /// </summary>
    Task<CurriculumOperationResult> UpdateChapterAsync(int chapterId, string title, int position, int? expectedCourseId = null);

    /// <summary>
    /// Updates an existing Chapter (Module) using the ModuleEditViewModel prepared in STEP 2.2.
    /// </summary>
    Task<CurriculumOperationResult> UpdateChapterAsync(ModuleEditViewModel model);

    /// <summary>
    /// Safely deletes a Chapter (Module) if no student activity is attached to its items, with optional course ownership verification.
    /// </summary>
    Task<CurriculumOperationResult> DeleteChapterAsync(int chapterId, int? expectedCourseId = null);

    /// <summary>
    /// Retrieves all lessons belonging to a specific Chapter (Module). Returns null if chapter does not exist.
    /// </summary>
    Task<IEnumerable<CurriculumItemViewModel>?> GetLessonsByChapterIdAsync(int chapterId);

    /// <summary>
    /// Retrieves all lessons belonging to a specific Module (alias for GetLessonsByChapterIdAsync).
    /// </summary>
    Task<IEnumerable<CurriculumItemViewModel>?> GetLessonsByModuleAsync(int moduleId);

    /// <summary>
    /// Gets a Lesson for editing via its parent CourseItem ID with optional module and course ownership verification.
    /// </summary>
    Task<LessonEditViewModel?> GetLessonAsync(int courseItemId, int? expectedChapterId = null, int? expectedCourseId = null);

    /// <summary>
    /// Gets a Lesson by ID (either CourseItem ID or Lesson entity ID) with optional module and course ownership verification.
    /// </summary>
    Task<LessonEditViewModel?> GetLessonByIdAsync(int lessonId, int? expectedChapterId = null, int? expectedCourseId = null);

    /// <summary>
    /// Creates a new CourseItem of Type=Lesson with its 1:1 child Lesson record, verifying module existence and course ownership.
    /// </summary>
    Task<CurriculumOperationResult> CreateLessonAsync(int chapterId, string title, string? contentHtml = null, string? videoUrl = null, int? position = null, int? expectedCourseId = null);

    /// <summary>
    /// Creates a new Lesson using the LessonCreateViewModel prepared in STEP 2.2.
    /// </summary>
    Task<CurriculumOperationResult> CreateLessonAsync(LessonCreateViewModel model);

    /// <summary>
    /// Updates an existing Lesson and its parent CourseItem title and position with strict ownership validation.
    /// </summary>
    Task<CurriculumOperationResult> UpdateLessonAsync(int courseItemId, string title, string? contentHtml, string? videoUrl, int position, int? expectedChapterId = null, int? expectedCourseId = null);

    /// <summary>
    /// Updates an existing Lesson using the LessonEditViewModel prepared in STEP 2.2.
    /// </summary>
    Task<CurriculumOperationResult> UpdateLessonAsync(LessonEditViewModel model);

    /// <summary>
    /// Safely deletes a Lesson (CourseItem + Lesson) if no student completion record exists, with strict ownership validation.
    /// </summary>
    Task<CurriculumOperationResult> DeleteLessonAsync(int courseItemId, int? expectedChapterId = null, int? expectedCourseId = null);

    /// <summary>
    /// Helper to compute the next auto-incremented position for a Chapter in a Course.
    /// </summary>
    Task<int> GetNextChapterPositionAsync(int courseId);

    /// <summary>
    /// Helper to compute the next auto-incremented position for a CourseItem in a Chapter.
    /// </summary>
    Task<int> GetNextItemPositionAsync(int chapterId);

    /// <summary>
    /// Loads complete Student Lesson Player data for a specific course and lesson (or CourseItem),
    /// strictly enforcing course-curriculum ownership, loading student completion status, and computing curriculum-ordered Previous/Next pointers.
    /// </summary>
    Task<StudentLessonPlayerViewModel?> GetStudentLessonPlayerAsync(int courseId, int lessonId, string? studentId = null);

    /// <summary>
    /// Returns the CourseItemId of the first available lesson in the course by Module Position then Item Position.
    /// Returns null if the course has no lessons.
    /// </summary>
    Task<int?> GetFirstLessonIdAsync(int courseId);

    /// <summary>
    /// Marks a Lesson (CourseItem) as complete for the given student in SQL Server, preventing duplicates idempotently.
    /// Strictly verifies that the course item belongs to the specified course.
    /// </summary>
    Task<CurriculumOperationResult> MarkLessonCompleteAsync(string studentId, int courseId, int courseItemId);

    /// <summary>
    /// Retrieves total count of completed lessons across all enrolled courses for a student.
    /// </summary>
    Task<int> GetStudentCompletedLessonsCountAsync(string studentId);

    /// <summary>
    /// Retrieves total lessons and completed lessons count per course for the specified student and course IDs.
    /// </summary>
    Task<Dictionary<int, (int TotalLessons, int CompletedLessons)>> GetStudentCoursesProgressAsync(string studentId, IEnumerable<int> courseIds);

    // ==========================================
    // STEP 4.3 QUIZ MANAGEMENT & EVALUATION
    // ==========================================

    /// <summary>
    /// Creates a new CourseItem of Type=Quiz with its 1:1 Quiz record, verifying module existence and course ownership.
    /// </summary>
    Task<CurriculumOperationResult> CreateQuizAsync(QuizCreateViewModel model);

    /// <summary>
    /// Updates an existing Quiz and its parent CourseItem title and position with strict ownership validation.
    /// </summary>
    Task<CurriculumOperationResult> UpdateQuizAsync(QuizEditViewModel model);

    /// <summary>
    /// Safely deletes a Quiz (CourseItem + Quiz + Questions) if no student submissions exist, with strict ownership validation.
    /// </summary>
    Task<CurriculumOperationResult> DeleteQuizAsync(int courseItemId, int? expectedChapterId = null, int? expectedCourseId = null);

    /// <summary>
    /// Gets a Quiz for editing via its parent CourseItem ID.
    /// </summary>
    Task<QuizEditViewModel?> GetQuizAsync(int courseItemId, int? expectedChapterId = null, int? expectedCourseId = null);

    /// <summary>
    /// Gets a Quiz by its primary key ID.
    /// </summary>
    Task<Quiz?> GetQuizByIdAsync(int quizId);

    /// <summary>
    /// Gets a Quiz with its questions and options for admin question management.
    /// </summary>
    Task<QuizQuestionsViewModel?> GetQuizQuestionsAsync(int quizId);

    /// <summary>
    /// Adds a question with its answer options to a Quiz, validating text, option count, and correct answer selection.
    /// </summary>
    Task<CurriculumOperationResult> AddQuestionAsync(int quizId, QuestionViewModel model);

    /// <summary>
    /// Updates an existing question and its answer options with validation.
    /// </summary>
    Task<CurriculumOperationResult> UpdateQuestionAsync(QuestionViewModel model);

    /// <summary>
    /// Deletes a question and its options.
    /// </summary>
    Task<CurriculumOperationResult> DeleteQuestionAsync(int questionId, int? expectedQuizId = null);

    /// <summary>
    /// Gets a question by ID with its options.
    /// </summary>
    Task<QuestionViewModel?> GetQuestionByIdAsync(int questionId);

    /// <summary>
    /// Loads complete Student Quiz Player data for a specific course and quiz item,
    /// verifying student enrollment, course ownership, and loading questions without exposing correct answer flags.
    /// </summary>
    Task<StudentQuizPlayerViewModel?> GetStudentQuizPlayerAsync(int courseId, int courseItemId, string studentId);

    /// <summary>
    /// Evaluates student quiz answers against SQL Server question options, computes score, determines pass/fail,
    /// and persists a QuizSubmission record in SQL Server.
    /// </summary>
    Task<(CurriculumOperationResult Result, StudentQuizResultViewModel? Outcome)> SubmitQuizAsync(string studentId, int courseId, int courseItemId, Dictionary<int, int> selectedOptions);

    /// <summary>
    /// Loads student quiz submission result, strictly verifying student ownership and course hierarchy.
    /// </summary>
    Task<StudentQuizResultViewModel?> GetStudentQuizResultAsync(int courseId, int courseItemId, int submissionId, string studentId);
}
