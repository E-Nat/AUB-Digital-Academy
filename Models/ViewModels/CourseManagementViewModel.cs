using AUBDigitalAcademy.Models;

namespace AUBDigitalAcademy.Models.ViewModels;

public class CourseManagementViewModel
{
    public List<Course> Courses { get; set; } = new();
    public CourseCreateViewModel CreateModel { get; set; } = new();
    public CourseEditViewModel EditModel { get; set; } = new();

    // Summary KPIs
    public int TotalCourses => Courses.Count;
    public int PublishedCourses => Courses.Count(c => c.Status == Enums.CourseStatus.Published);
    public int DraftCourses => Courses.Count(c => c.Status == Enums.CourseStatus.Draft);
    public int ArchivedCourses => Courses.Count(c => c.Status == Enums.CourseStatus.Archived);
    public int TotalModules => Courses.Sum(c => c.Chapters?.Count ?? 0);
    public int TotalLessons => Courses.Sum(c => c.Chapters?.Sum(ch => ch.CourseItems?.Count ?? 0) ?? 0);
}
