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
    public string? MajorName { get; set; }
    public string? MajorCode { get; set; }
    public string? SemesterName { get; set; }
    public string? TeacherName { get; set; }

    // Curriculum Hierarchy
    public List<CurriculumModuleViewModel> Modules { get; set; } = new();

    // Summary KPIs
    public int TotalModules => Modules.Count;
    public int TotalLessons => Modules.Sum(m => m.Items.Count(i => i.Type == ItemType.Lesson));
    public int TotalItems => Modules.Sum(m => m.Items.Count);

    // Form Models for Modals/AJAX
    public ModuleCreateViewModel ModuleCreateModel { get; set; } = new();
    public ModuleEditViewModel ModuleEditModel { get; set; } = new();
    public LessonCreateViewModel LessonCreateModel { get; set; } = new();
    public LessonEditViewModel LessonEditModel { get; set; } = new();
}

public class CurriculumModuleViewModel
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }

    public List<CurriculumItemViewModel> Items { get; set; } = new();

    public int LessonCount => Items.Count(i => i.Type == ItemType.Lesson);
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
}
