using AUBDigitalAcademy.Models;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface ICourseService
{
    Task<IEnumerable<Course>> GetAllCoursesAsync();
    Task<Course?> GetCourseByIdAsync(int id);
    Task<Course> CreateCourseAsync(Course course);
    Task<bool> UpdateCourseAsync(Course course);
    Task<CourseDeleteResult> DeleteCourseAsync(int id);
    Task<bool> CourseCodeExistsAsync(string code, int? excludeCourseId = null);
}
