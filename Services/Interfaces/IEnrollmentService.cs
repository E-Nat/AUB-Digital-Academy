using AUBDigitalAcademy.Models;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface IEnrollmentService
{
    /// <summary>
    /// Enrolls a student in a course after validating student, course status, and duplicate prevention.
    /// </summary>
    Task<EnrollmentOperationResult> EnrollStudentAsync(string studentId, int courseId, bool isAdmin = false);

    /// <summary>
    /// Retrieves all courses a student is enrolled in, with course details included.
    /// </summary>
    Task<IEnumerable<Enrollment>> GetStudentEnrollmentsAsync(string studentId);

    /// <summary>
    /// Retrieves all students enrolled in a specific course, with student details included.
    /// </summary>
    Task<IEnumerable<Enrollment>> GetCourseEnrollmentsAsync(int courseId);

    /// <summary>
    /// Checks whether a specific student is actively enrolled in a course.
    /// </summary>
    Task<bool> IsStudentEnrolledAsync(string studentId, int courseId);

    /// <summary>
    /// Retrieves an enrollment by its ID.
    /// </summary>
    Task<Enrollment?> GetEnrollmentByIdAsync(int enrollmentId);

    /// <summary>
    /// Retrieves an enrollment by student ID and course ID.
    /// </summary>
    Task<Enrollment?> GetEnrollmentAsync(string studentId, int courseId);

    /// <summary>
    /// Safely removes an enrollment if no student academic activity (progress, submissions) exists.
    /// </summary>
    Task<EnrollmentOperationResult> RemoveEnrollmentAsync(int enrollmentId, string? requestedByUserId = null, bool isAdmin = false);

    /// <summary>
    /// Safely removes an enrollment by student ID and course ID if no student academic activity exists.
    /// </summary>
    Task<EnrollmentOperationResult> RemoveEnrollmentByStudentAndCourseAsync(string studentId, int courseId, string? requestedByUserId = null, bool isAdmin = false);

    /// <summary>
    /// Checks if a student has any recorded learning progress, quiz submissions, or assignment submissions in a course.
    /// </summary>
    Task<bool> HasStudentLearningActivityAsync(string studentId, int courseId);
}
