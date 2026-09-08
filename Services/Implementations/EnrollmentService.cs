using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

public class EnrollmentService : IEnrollmentService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        ApplicationDbContext context,
        UserManager<User> userManager,
        ILogger<EnrollmentService> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<EnrollmentOperationResult> EnrollStudentAsync(string studentId, int courseId, bool isAdmin = false)
    {
        // 1. Validate parameters
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return EnrollmentOperationResult.InvalidStudent("Student ID is required.");
        }

        if (courseId <= 0)
        {
            return EnrollmentOperationResult.CourseNotFound("Invalid course ID.");
        }

        // 2. Validate Student identity & status
        var student = await _userManager.FindByIdAsync(studentId);
        if (student == null)
        {
            _logger.LogWarning("Enrollment rejected: Student with ID '{StudentId}' not found.", studentId);
            return EnrollmentOperationResult.StudentNotFound($"Student with ID '{studentId}' was not found.");
        }

        if (!student.IsActive)
        {
            _logger.LogWarning("Enrollment rejected: Student '{StudentId}' ({FullName}) is inactive.", studentId, student.FullName);
            return EnrollmentOperationResult.InvalidStudent($"Student account '{student.FullName}' is inactive.");
        }

        var isStudent = await _userManager.IsInRoleAsync(student, "Student");
        if (!isStudent)
        {
            _logger.LogWarning("Enrollment rejected: User '{StudentId}' ({FullName}) does not have Student role.", studentId, student.FullName);
            return EnrollmentOperationResult.InvalidStudent($"User '{student.FullName}' does not have the Student role.");
        }

        // 3. Validate Course exists & availability
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
        if (course == null)
        {
            _logger.LogWarning("Enrollment rejected: Course ID {CourseId} not found.", courseId);
            return EnrollmentOperationResult.CourseNotFound($"Course with ID '{courseId}' was not found.");
        }

        if (course.Status == CourseStatus.Archived)
        {
            _logger.LogWarning("Enrollment rejected: Course '{Title}' (ID {CourseId}) is archived.", course.Title, courseId);
            return EnrollmentOperationResult.CourseNotAvailable($"Course '{course.Title}' is archived and no longer accepting enrollments.");
        }

        if (course.Status == CourseStatus.Draft && !isAdmin)
        {
            _logger.LogWarning("Enrollment rejected: Course '{Title}' (ID {CourseId}) is in Draft status and user is not admin.", course.Title, courseId);
            return EnrollmentOperationResult.CourseNotAvailable($"Course '{course.Title}' is currently in draft and not yet available for enrollment.");
        }

        // 4. Check for duplicate enrollment (Database-safe check)
        var alreadyEnrolled = await _context.Enrollments.AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
        if (alreadyEnrolled)
        {
            _logger.LogInformation("Enrollment rejected: Student '{StudentId}' is already enrolled in Course {CourseId}.", studentId, courseId);
            return EnrollmentOperationResult.AlreadyEnrolled($"Student '{student.FullName}' is already enrolled in '{course.Title}'.");
        }

        // 5. Create and persist Enrollment record
        var enrollment = new Enrollment
        {
            StudentId = studentId,
            CourseId = courseId,
            EnrolledAt = DateTime.UtcNow
        };

        _context.Enrollments.Add(enrollment);

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Enrollment successful: Student '{StudentName}' ({StudentId}) enrolled in Course '{CourseTitle}' ({CourseId}), Enrollment ID: {EnrollmentId}",
                student.FullName, studentId, course.Title, courseId, enrollment.Id);

            return EnrollmentOperationResult.Success(
                enrollment.Id,
                studentId,
                courseId,
                course.Title,
                student.FullName,
                $"Successfully enrolled '{student.FullName}' in '{course.Title}'.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "DbUpdateException on enrolling student {StudentId} in course {CourseId}. Potential unique constraint conflict.", studentId, courseId);
            return EnrollmentOperationResult.AlreadyEnrolled($"Student '{student.FullName}' is already enrolled in '{course.Title}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error enrolling student {StudentId} in course {CourseId}.", studentId, courseId);
            return EnrollmentOperationResult.Failed($"Failed to complete enrollment due to database error: {ex.Message}");
        }
    }

    public async Task<IEnumerable<Enrollment>> GetStudentEnrollmentsAsync(string studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return Enumerable.Empty<Enrollment>();
        }

        return await _context.Enrollments
            .AsNoTracking()
            .Include(e => e.Course)
                .ThenInclude(c => c.Major)
            .Include(e => e.Course)
                .ThenInclude(c => c.Semester)
            .Include(e => e.Course)
                .ThenInclude(c => c.Teacher)
            .Where(e => e.StudentId == studentId)
            .OrderByDescending(e => e.EnrolledAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Enrollment>> GetCourseEnrollmentsAsync(int courseId)
    {
        if (courseId <= 0)
        {
            return Enumerable.Empty<Enrollment>();
        }

        return await _context.Enrollments
            .AsNoTracking()
            .Include(e => e.Student)
            .Where(e => e.CourseId == courseId)
            .OrderByDescending(e => e.EnrolledAt)
            .ToListAsync();
    }

    public async Task<bool> IsStudentEnrolledAsync(string studentId, int courseId)
    {
        if (string.IsNullOrWhiteSpace(studentId) || courseId <= 0)
        {
            return false;
        }

        return await _context.Enrollments
            .AnyAsync(e => e.StudentId == studentId && e.CourseId == courseId);
    }

    public async Task<Enrollment?> GetEnrollmentByIdAsync(int enrollmentId)
    {
        if (enrollmentId <= 0)
        {
            return null;
        }

        return await _context.Enrollments
            .Include(e => e.Course)
            .Include(e => e.Student)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);
    }

    public async Task<Enrollment?> GetEnrollmentAsync(string studentId, int courseId)
    {
        if (string.IsNullOrWhiteSpace(studentId) || courseId <= 0)
        {
            return null;
        }

        return await _context.Enrollments
            .Include(e => e.Course)
            .Include(e => e.Student)
            .FirstOrDefaultAsync(e => e.StudentId == studentId && e.CourseId == courseId);
    }

    public async Task<bool> HasStudentLearningActivityAsync(string studentId, int courseId)
    {
        if (string.IsNullOrWhiteSpace(studentId) || courseId <= 0)
        {
            return false;
        }

        var hasProgress = await _context.CourseProgresses
            .AnyAsync(cp => cp.StudentId == studentId && cp.CourseItem.Chapter.CourseId == courseId);

        if (hasProgress) return true;

        var hasQuiz = await _context.QuizSubmissions
            .AnyAsync(qs => qs.StudentId == studentId && qs.Quiz.CourseItem.Chapter.CourseId == courseId);

        if (hasQuiz) return true;

        var hasAssignment = await _context.AssignmentSubmissions
            .AnyAsync(asub => asub.StudentId == studentId && asub.Assignment.CourseItem.Chapter.CourseId == courseId);

        return hasAssignment;
    }

    public async Task<EnrollmentOperationResult> RemoveEnrollmentAsync(int enrollmentId, string? requestedByUserId = null, bool isAdmin = false)
    {
        if (enrollmentId <= 0)
        {
            return EnrollmentOperationResult.NotFound("Invalid enrollment ID.");
        }

        var enrollment = await _context.Enrollments
            .Include(e => e.Course)
            .Include(e => e.Student)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment == null)
        {
            return EnrollmentOperationResult.NotFound("Enrollment record was not found.");
        }

        // Ownership / Authorization check
        if (!string.IsNullOrWhiteSpace(requestedByUserId) && !isAdmin && enrollment.StudentId != requestedByUserId)
        {
            _logger.LogWarning("Unauthorized unenroll attempt: User '{Requester}' attempted to unenroll student '{StudentId}'.", requestedByUserId, enrollment.StudentId);
            return EnrollmentOperationResult.Unauthorized("You are not authorized to unenroll another student.");
        }

        // Safe Removal Analysis: Inspect academic dependencies
        var progressCount = await _context.CourseProgresses
            .CountAsync(cp => cp.StudentId == enrollment.StudentId && cp.CourseItem.Chapter.CourseId == enrollment.CourseId);

        var quizCount = await _context.QuizSubmissions
            .CountAsync(qs => qs.StudentId == enrollment.StudentId && qs.Quiz.CourseItem.Chapter.CourseId == enrollment.CourseId);

        var assignmentCount = await _context.AssignmentSubmissions
            .CountAsync(asub => asub.StudentId == enrollment.StudentId && asub.Assignment.CourseItem.Chapter.CourseId == enrollment.CourseId);

        var totalActivity = progressCount + quizCount + assignmentCount;
        if (totalActivity > 0)
        {
            var activityDetails = new List<string>();
            if (progressCount > 0) activityDetails.Add($"{progressCount} lesson progress record{(progressCount > 1 ? "s" : "")}");
            if (quizCount > 0) activityDetails.Add($"{quizCount} quiz submission{(quizCount > 1 ? "s" : "")}");
            if (assignmentCount > 0) activityDetails.Add($"{assignmentCount} assignment submission{(assignmentCount > 1 ? "s" : "")}");

            var details = string.Join(", ", activityDetails);
            _logger.LogWarning("Unenroll blocked: Student '{StudentId}' has recorded activity ({Details}) in Course {CourseId}.",
                enrollment.StudentId, details, enrollment.CourseId);

            return EnrollmentOperationResult.BlockedByStudentActivity(
                enrollment.Id,
                enrollment.StudentId,
                enrollment.CourseId,
                enrollment.Course.Title,
                enrollment.Student.FullName,
                $"Cannot unenroll student '{enrollment.Student.FullName}' because learning records exist in '{enrollment.Course.Title}' ({details}). Student academic history must be preserved.",
                progressCount,
                quizCount,
                assignmentCount);
        }

        // Safe to remove
        _context.Enrollments.Remove(enrollment);

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Enrollment ID {EnrollmentId} (Student '{StudentId}' from Course {CourseId}) safely removed.",
                enrollment.Id, enrollment.StudentId, enrollment.CourseId);

            return EnrollmentOperationResult.Success(
                enrollment.Id,
                enrollment.StudentId,
                enrollment.CourseId,
                enrollment.Course.Title,
                enrollment.Student.FullName,
                $"Enrollment for '{enrollment.Student.FullName}' in '{enrollment.Course.Title}' was successfully removed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting enrollment ID {EnrollmentId}.", enrollment.Id);
            return EnrollmentOperationResult.Failed($"Failed to remove enrollment due to database error: {ex.Message}");
        }
    }

    public async Task<EnrollmentOperationResult> RemoveEnrollmentByStudentAndCourseAsync(string studentId, int courseId, string? requestedByUserId = null, bool isAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(studentId) || courseId <= 0)
        {
            return EnrollmentOperationResult.NotFound("Invalid student ID or course ID.");
        }

        var enrollment = await _context.Enrollments
            .FirstOrDefaultAsync(e => e.StudentId == studentId && e.CourseId == courseId);

        if (enrollment == null)
        {
            return EnrollmentOperationResult.NotFound("Enrollment record was not found for this student and course.");
        }

        return await RemoveEnrollmentAsync(enrollment.Id, requestedByUserId, isAdmin);
    }
}
