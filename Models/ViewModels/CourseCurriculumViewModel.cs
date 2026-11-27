using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models.ViewModels;

public class CourseCurriculumViewModel
{
    // Course Context
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string? CourseDescription { get; set; }
    public string? Thumbnail { get; set; }
    public CourseStatus Status { get; set; } = CourseStatus.Draft;
    public CourseAccessMode AccessMode { get; set; } = CourseAccessMode.Free;
    public string? MajorName { get; set; }
    public string? MajorCode { get; set; }
    public string? SemesterName { get; set; }
    public string? TeacherName { get; set; }

    // Curriculum Hierarchy
    public List<CurriculumModuleViewModel> Modules { get; set; } = new();

    // Summary KPIs
    public int TotalModules => Modules.Count;
    public int TotalLessons => Modules.Sum(m => m.Items.Count(i => i.Type == ItemType.Lesson));
    public int TotalBooks => Modules.Sum(m => m.Items.Count(i => i.Type == ItemType.Book));
    public int TotalQuizzes => Modules.Sum(m => m.Items.Count(i => i.Type == ItemType.Quiz));
    public int TotalAssignments => Modules.Sum(m => m.Items.Count(i => i.Type == ItemType.Assignment));
    public int TotalItems => Modules.Sum(m => m.Items.Count);

    // Form Models for Modals / AJAX
    public ModuleCreateViewModel ModuleCreateModel { get; set; } = new();
    public ModuleEditViewModel ModuleEditModel { get; set; } = new();
    public LessonCreateViewModel LessonCreateModel { get; set; } = new();
    public LessonEditViewModel LessonEditModel { get; set; } = new();
    public BookCreateViewModel BookCreateModel { get; set; } = new();
    public BookEditViewModel BookEditModel { get; set; } = new();
    public AssignmentCreateViewModel AssignmentCreateModel { get; set; } = new();
    public AssignmentEditViewModel AssignmentEditModel { get; set; } = new();
    public QuizCreateViewModel QuizCreateModel { get; set; } = new();
    public QuizEditViewModel QuizEditModel { get; set; } = new();
}

public class CurriculumModuleViewModel
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }

    private List<CurriculumItemViewModel> _items = new();

    /// <summary>
    /// Gets or sets the items in this module.
    /// Strict Module Isolation: When Id > 0, items belonging to other modules (where ChapterId != Id and ChapterId > 0) are strictly excluded.
    /// </summary>
    public List<CurriculumItemViewModel> Items
    {
        get => Id > 0
            ? _items.Where(i => i.ChapterId == 0 || i.ChapterId == Id).ToList()
            : _items;
        set => _items = value ?? new();
    }

    public int LessonCount => Items.Count(i => i.Type == ItemType.Lesson);
    public int BookCount => Items.Count(i => i.Type == ItemType.Book);
    public int QuizCount => Items.Count(i => i.Type == ItemType.Quiz);
    public int AssignmentCount => Items.Count(i => i.Type == ItemType.Assignment);
}

public class CurriculumItemViewModel
{
    public int Id { get; set; }
    public int ChapterId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
    public ItemType Type { get; set; }

    // Lesson-specific payload (populated when Type == ItemType.Lesson)
    public int? LessonId { get; set; }
    public string? VideoUrl { get; set; }
    public string? ContentHtml { get; set; }
    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoUrl);
    public bool HasContent => !string.IsNullOrWhiteSpace(ContentHtml);

    // Book-specific payload (populated when Type == ItemType.Book)
    public int? BookId { get; set; }
    public string? FileUrl { get; set; }
    public string? FileType { get; set; }
    public long? FileSize { get; set; }
    public bool HasFile => !string.IsNullOrWhiteSpace(FileUrl);

    // Assignment-specific payload (populated when Type == ItemType.Assignment)
    public int? AssignmentId { get; set; }
    public string? Instruction { get; set; }
    public double? MaxScore { get; set; }
    public DateTime? DueDate { get; set; }

    // Quiz-specific payload (populated when Type == ItemType.Quiz)
    public int? QuizId { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public double? PassingScore { get; set; }
    public int? MaxAttempts { get; set; }
    public bool IsPublished { get; set; }
    public int QuestionCount { get; set; }
    public List<int> PrerequisiteItemIds { get; set; } = new();
    public List<string> PrerequisiteItemTitles { get; set; } = new();
}
