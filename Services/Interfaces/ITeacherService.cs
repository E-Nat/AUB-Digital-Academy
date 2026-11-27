using AUBDigitalAcademy.Models.ViewModels;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface ITeacherService
{
    Task<TeacherDashboardViewModel> GetTeacherDashboardAsync(string teacherId);
    Task<IEnumerable<TeacherCourseItemViewModel>> GetTeacherCoursesAsync(string teacherId);
    Task<TeacherCourseDetailsViewModel?> GetTeacherCourseDetailsAsync(int courseId, string teacherId);
    Task<List<StudentLessonVideoDetailViewModel>?> GetStudentCourseVideoProgressAsync(int courseId, string studentId, string requesterId, bool isAdmin = false);
    Task<IEnumerable<TeacherAssignmentItemViewModel>> GetTeacherAssignmentsAsync(string teacherId, int? courseId = null);
    Task<IEnumerable<TeacherSubmissionItemViewModel>> GetAssignmentSubmissionsAsync(int assignmentId, string teacherId);
    Task<TeacherGradingResult> GradeSubmissionAsync(int submissionId, double score, string? feedback, string teacherId);
    Task<IEnumerable<TeacherGradeItemViewModel>> GetTeacherGradesAsync(string teacherId, int? courseId = null);
    Task<IEnumerable<TeacherQuizItemViewModel>> GetTeacherQuizzesAsync(string teacherId, int? courseId = null);
    Task<IEnumerable<TeacherBookItemViewModel>> GetTeacherBooksAsync(string teacherId, int? courseId = null);
    Task<bool> HasAccessToCourseAsync(int courseId, string userId);
    Task<bool> CanGradeSubmissionAsync(int submissionId, string userId);
    Task<bool> CanGradeCourseAsync(int courseId, string userId);
    Task<bool> IsAssistantAssignedToCourseAsync(int courseId, string assistantId);
}
