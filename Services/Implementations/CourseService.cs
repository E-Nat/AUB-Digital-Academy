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
    private readonly IHtmlSanitizationService _htmlSanitizer;

    public CourseService(
        ApplicationDbContext context,
        ILogger<CourseService> logger,
        IHtmlSanitizationService? htmlSanitizer = null)
    {
        _context = context;
        _logger = logger;
        _htmlSanitizer = htmlSanitizer ?? new HtmlSanitizationService();
    }

    public async Task<IEnumerable<Course>> GetAllCoursesAsync()
    {
        return await _context.Courses
            .AsNoTracking()
            .Include(c => c.Major)
                .ThenInclude(m => m.Category)
            .Include(c => c.Semester)
            .Include(c => c.Teacher)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
            .Include(c => c.Enrollments)
            .AsSplitQuery()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<Course?> GetCourseByIdAsync(int id)
    {
        return await _context.Courses
            .AsNoTracking()
            .Include(c => c.Major)
                .ThenInclude(m => m.Category)
            .Include(c => c.Semester)
            .Include(c => c.Teacher)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
            .Include(c => c.Enrollments)
            .AsSplitQuery()
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

        // 5b. Validate Thumbnail URL / Path if provided
        if (!string.IsNullOrWhiteSpace(course.Thumbnail))
        {
            var safeThumbnail = _htmlSanitizer.SanitizeResourceUrl(course.Thumbnail);
            if (safeThumbnail == null)
            {
                throw new ArgumentException("Invalid thumbnail URL or path. Only http://, https://, or safe relative paths are permitted.", nameof(course.Thumbnail));
            }
            course.Thumbnail = safeThumbnail;
        }
        else
        {
            course.Thumbnail = null;
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

        string? sanitizedThumbnail = null;
        if (!string.IsNullOrWhiteSpace(course.Thumbnail))
        {
            sanitizedThumbnail = _htmlSanitizer.SanitizeResourceUrl(course.Thumbnail);
            if (sanitizedThumbnail == null)
            {
                throw new ArgumentException("Invalid thumbnail URL or path. Only http://, https://, or safe relative paths are permitted.", nameof(course.Thumbnail));
            }
        }

        existingCourse.Code = normalizedCode;
        existingCourse.Title = course.Title.Trim();
        existingCourse.Description = course.Description?.Trim();
        existingCourse.Status = course.Status;
        existingCourse.MajorId = course.MajorId;
        existingCourse.SemesterId = course.SemesterId;
        existingCourse.TeacherId = course.TeacherId;
        existingCourse.Thumbnail = sanitizedThumbnail;
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

    public async Task<CourseDeleteResult> DeleteCourseAsync(int id)
    {
        if (id <= 0)
        {
            return CourseDeleteResult.Failed("Invalid course ID.");
        }

        // 1. Find Course with curriculum dependencies loaded for safe cascade tracking
        var course = await _context.Courses
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Lesson)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Book)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Quiz!)
                        .ThenInclude(q => q.Questions)
                            .ThenInclude(qu => qu.Options)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Assignment)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id);

        // 2. Check if Course exists
        if (course == null)
        {
            _logger.LogWarning("Safe delete course: Course ID {CourseId} not found.", id);
            return CourseDeleteResult.NotFound("Course not found.");
        }

        // 3. Check student activity dependencies
        // 3a. Student Enrollments
        var enrollmentCount = await _context.Enrollments
            .CountAsync(e => e.CourseId == id);

        // 3b. Student CourseProgress
        var progressCount = await _context.CourseProgresses
            .CountAsync(cp => cp.CourseItem.Chapter.CourseId == id);

        // 3c. Student Quiz Submissions
        var quizSubmissionCount = await _context.QuizSubmissions
            .CountAsync(qs => qs.Quiz.CourseItem.Chapter.CourseId == id);

        // 3d. Student Assignment Submissions
        var assignmentSubmissionCount = await _context.AssignmentSubmissions
            .CountAsync(asub => asub.Assignment.CourseItem.Chapter.CourseId == id);

        // 4. Determine whether deletion is allowed
        if (enrollmentCount > 0 || progressCount > 0 || quizSubmissionCount > 0 || assignmentSubmissionCount > 0)
        {
            var reasons = new List<string>();
            if (enrollmentCount > 0) reasons.Add($"{enrollmentCount} student enrollment{(enrollmentCount > 1 ? "s" : "")}");
            if (progressCount > 0) reasons.Add($"{progressCount} student progress record{(progressCount > 1 ? "s" : "")}");
            if (quizSubmissionCount > 0) reasons.Add($"{quizSubmissionCount} quiz submission{(quizSubmissionCount > 1 ? "s" : "")}");
            if (assignmentSubmissionCount > 0) reasons.Add($"{assignmentSubmissionCount} assignment submission{(assignmentSubmissionCount > 1 ? "s" : "")}");

            var details = string.Join(", ", reasons);
            var blockedMessage = $"This course cannot be deleted because it contains student enrollment or learning records ({details}).";

            _logger.LogWarning("Safe delete blocked for course ID {CourseId} ('{Title}'): {Details}", id, course.Title, details);

            return CourseDeleteResult.BlockedByStudentData(
                course.Id,
                course.Title,
                blockedMessage,
                enrollmentCount,
                progressCount,
                quizSubmissionCount,
                assignmentSubmissionCount);
        }

        // 5. Safe delete allowed: Remove course (and let EF Core cascade delete curriculum)
        try
        {
            _context.Courses.Remove(course);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Course ID {CourseId} ('{Title}') and its curriculum were safely deleted.", id, course.Title);
            return CourseDeleteResult.Success(id, course.Title, "Course deleted successfully.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database update error during safe delete of course ID {CourseId}.", id);
            return CourseDeleteResult.Failed("A database constraint prevented the course from being deleted.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting course ID {CourseId}.", id);
            return CourseDeleteResult.Failed("An unexpected error occurred while deleting the course. Please try again.");
        }
    }
}
