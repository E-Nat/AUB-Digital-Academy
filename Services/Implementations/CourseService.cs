using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

public class CourseService : ICourseService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CourseService> _logger;

    public CourseService(ApplicationDbContext context, ILogger<CourseService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IEnumerable<Course>> GetAllCoursesAsync()
    {
        return await _context.Courses
            .Include(c => c.Major)
                .ThenInclude(m => m.Category)
            .Include(c => c.Semester)
            .Include(c => c.Teacher)
            .Include(c => c.Chapters)
            .Include(c => c.Enrollments)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<Course?> GetCourseByIdAsync(int id)
    {
        return await _context.Courses
            .Include(c => c.Major)
                .ThenInclude(m => m.Category)
            .Include(c => c.Semester)
            .Include(c => c.Teacher)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
            .Include(c => c.Enrollments)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<bool> CourseCodeExistsAsync(string code, int? excludeCourseId = null)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var normalizedCode = code.Trim().ToUpper();
        var query = _context.Courses.AsQueryable();

        if (excludeCourseId.HasValue)
        {
            query = query.Where(c => c.Id != excludeCourseId.Value);
        }

        return await query.AnyAsync(c => c.Code.ToUpper() == normalizedCode);
    }

    public async Task<Course> CreateCourseAsync(Course course)
    {
        ArgumentNullException.ThrowIfNull(course);

        // 1. Validate required fields
        if (string.IsNullOrWhiteSpace(course.Title))
        {
            throw new ArgumentException("Course title is required.", nameof(course.Title));
        }

        if (string.IsNullOrWhiteSpace(course.Code))
        {
            throw new ArgumentException("Course code is required.", nameof(course.Code));
        }

        course.Code = course.Code.Trim().ToUpper();
        course.Title = course.Title.Trim();

        // 2. Validate duplicate code
        if (await CourseCodeExistsAsync(course.Code))
        {
            throw new InvalidOperationException("A course with this course code already exists.");
        }

        // 3. Validate Teacher exists
        if (string.IsNullOrWhiteSpace(course.TeacherId))
        {
            throw new ArgumentException("Teacher selection is required.", nameof(course.TeacherId));
        }

        var teacherExists = await _context.Users.AnyAsync(u => u.Id == course.TeacherId);
        if (!teacherExists)
        {
            throw new InvalidOperationException("The selected teacher does not exist.");
        }

        // 4. Validate Major exists
        if (course.MajorId <= 0)
        {
            throw new ArgumentException("Major selection is required.", nameof(course.MajorId));
        }

        var majorExists = await _context.Majors.AnyAsync(m => m.Id == course.MajorId);
        if (!majorExists)
        {
            throw new InvalidOperationException("The selected major does not exist.");
        }

        // 5. Validate Semester exists
        if (course.SemesterId <= 0)
        {
            throw new ArgumentException("Semester selection is required.", nameof(course.SemesterId));
        }

        var semesterExists = await _context.Semesters.AnyAsync(s => s.Id == course.SemesterId);
        if (!semesterExists)
        {
            throw new InvalidOperationException("The selected semester does not exist.");
        }

        // 6. Set audit timestamps
        course.CreatedAt = DateTime.UtcNow;
        course.UpdatedAt = DateTime.UtcNow;

        try
        {
            _context.Courses.Add(course);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Course '{Title}' ({Code}) created with ID {CourseId} (Status: {Status}).",
                course.Title, course.Code, course.Id, course.Status);
            return course;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database update error creating course '{Title}' ({Code}).", course.Title, course.Code);
            if (await CourseCodeExistsAsync(course.Code))
            {
                throw new InvalidOperationException("A course with this course code already exists.", ex);
            }
            throw new InvalidOperationException("An error occurred while saving the course to the database. Please verify all selected items.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating course '{Title}' ({Code}).", course.Title, course.Code);
            throw;
        }
    }

    public async Task<bool> UpdateCourseAsync(Course course)
    {
        ArgumentNullException.ThrowIfNull(course);

        if (string.IsNullOrWhiteSpace(course.Title))
        {
            throw new ArgumentException("Course title is required.", nameof(course.Title));
        }

        if (string.IsNullOrWhiteSpace(course.Code))
        {
            throw new ArgumentException("Course code is required.", nameof(course.Code));
        }

        var existingCourse = await _context.Courses.FindAsync(course.Id);
        if (existingCourse == null)
        {
            return false;
        }

        var normalizedCode = course.Code.Trim().ToUpper();
        if (existingCourse.Code.ToUpper() != normalizedCode && await CourseCodeExistsAsync(normalizedCode, course.Id))
        {
            throw new InvalidOperationException($"A course with code '{normalizedCode}' already exists.");
        }

        // Validate Teacher
        if (string.IsNullOrWhiteSpace(course.TeacherId))
        {
            throw new ArgumentException("Teacher selection is required.", nameof(course.TeacherId));
        }

        var teacherExists = await _context.Users.AnyAsync(u => u.Id == course.TeacherId);
        if (!teacherExists)
        {
            throw new InvalidOperationException("The selected teacher does not exist.");
        }

        // Validate Major
        if (course.MajorId <= 0)
        {
            throw new ArgumentException("Major selection is required.", nameof(course.MajorId));
        }

        var majorExists = await _context.Majors.AnyAsync(m => m.Id == course.MajorId);
        if (!majorExists)
        {
            throw new InvalidOperationException("The selected major does not exist.");
        }

        // Validate Semester
        if (course.SemesterId <= 0)
        {
            throw new ArgumentException("Semester selection is required.", nameof(course.SemesterId));
        }

        var semesterExists = await _context.Semesters.AnyAsync(s => s.Id == course.SemesterId);
        if (!semesterExists)
        {
            throw new InvalidOperationException("The selected semester does not exist.");
        }

        existingCourse.Code = normalizedCode;
        existingCourse.Title = course.Title.Trim();
        existingCourse.Description = course.Description?.Trim();
        existingCourse.Status = course.Status;
        existingCourse.MajorId = course.MajorId;
        existingCourse.SemesterId = course.SemesterId;
        existingCourse.TeacherId = course.TeacherId;
        if (course.Thumbnail != null)
        {
            existingCourse.Thumbnail = string.IsNullOrWhiteSpace(course.Thumbnail) ? null : course.Thumbnail.Trim();
        }
        existingCourse.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Course ID {CourseId} ('{Title}') updated successfully.", existingCourse.Id, existingCourse.Title);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating course ID {CourseId}.", course.Id);
            throw;
        }
    }

    public async Task<bool> DeleteCourseAsync(int id)
    {
        var course = await _context.Courses.FindAsync(id);
        if (course == null)
        {
            return false;
        }

        try
        {
            _context.Courses.Remove(course);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Course ID {CourseId} deleted.", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting course ID {CourseId}.", id);
            throw;
        }
    }
}
