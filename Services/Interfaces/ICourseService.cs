using AUBDigitalAcademy.Models;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface ICourseService
{
    Task<IEnumerable<Course>> GetAllCoursesAsync();
    Task<Course?> GetCourseByIdAsync(int id);
    Task<Course> CreateCourseAsync(Course course);
    Task<bool> UpdateCourseAsync(Course course);
    Task<bool> DeleteCourseAsync(int id);
    Task<bool> CourseCodeExistsAsync(string code, int? excludeCourseId = null);
}
