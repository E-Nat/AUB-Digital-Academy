using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Controllers;

[Authorize]
public class EnrollmentController : Controller
{
    private readonly IEnrollmentService _enrollmentService;
    private readonly ICourseService _courseService;
    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<EnrollmentController> _logger;

    public EnrollmentController(
        IEnrollmentService enrollmentService,
        ICourseService courseService,
        UserManager<User> userManager,
        ApplicationDbContext context,
        ILogger<EnrollmentController> logger)
    {
        _enrollmentService = enrollmentService;
        _courseService = courseService;
        _userManager = userManager;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Admin entry point: Manage student enrollments.
    /// View a selected student's enrolled courses, switch between students, and enroll/unenroll.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Index(string? studentId = null)
    {
        ViewData["Title"] = "Enrollment Management";
        ViewData["ActivePage"] = "Enrollments";

        // 1. Retrieve all users with "Student" role from SQL Server
        var studentUsers = await _userManager.GetUsersInRoleAsync("Student");
        var activeStudents = studentUsers
            .OrderBy(s => s.FullName)
            .ToList();

        // Count enrollments per student
        var studentEnrollmentCounts = await _context.Enrollments
            .GroupBy(e => e.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StudentId, x => x.Count);

        var totalActiveEnrollments = await _context.Enrollments.CountAsync();

        var viewModel = new AdminStudentEnrollmentsViewModel
        {
            TotalStudentsCount = activeStudents.Count,
            TotalActiveEnrollmentsCount = totalActiveEnrollments,
            AllStudents = activeStudents.Select(s => new StudentLookupItemViewModel
            {
                Id = s.Id,
                FullName = s.FullName,
                Email = s.Email ?? string.Empty,
                UniversityId = s.UniversityId,
                EnrolledCoursesCount = studentEnrollmentCounts.TryGetValue(s.Id, out var count) ? count : 0
            }).ToList()
        };

        // Determine target student
        User? selectedStudent = null;
        if (!string.IsNullOrWhiteSpace(studentId))
        {
            selectedStudent = activeStudents.FirstOrDefault(s => s.Id == studentId);
            if (selectedStudent == null)
            {
                selectedStudent = await _userManager.FindByIdAsync(studentId);
            }
        }

        // If no student specified or not found, default to first available student
        if (selectedStudent == null && activeStudents.Count > 0)
        {
            selectedStudent = activeStudents[0];
        }

        if (selectedStudent != null)
        {
            viewModel.SelectedStudentId = selectedStudent.Id;
            viewModel.StudentName = selectedStudent.FullName;
            viewModel.StudentEmail = selectedStudent.Email;
            viewModel.UniversityId = selectedStudent.UniversityId;
            viewModel.Avatar = selectedStudent.Avatar;
            viewModel.IsActive = selectedStudent.IsActive;

            // Load real student enrollments
            var enrollments = await _enrollmentService.GetStudentEnrollmentsAsync(selectedStudent.Id);
            var enrolledList = new List<StudentEnrollmentItemViewModel>();

            foreach (var e in enrollments)
            {
                var hasActivity = await _enrollmentService.HasStudentLearningActivityAsync(selectedStudent.Id, e.CourseId);
                enrolledList.Add(new StudentEnrollmentItemViewModel
                {
                    EnrollmentId = e.Id,
                    CourseId = e.CourseId,
                    CourseCode = e.Course.Code,
                    CourseTitle = e.Course.Title,
                    Description = e.Course.Description,
                    Thumbnail = e.Course.Thumbnail,
                    Status = e.Course.Status,
                    TeacherName = e.Course.Teacher?.FullName ?? "Instructor",
                    MajorName = e.Course.Major?.Name ?? "General",
                    SemesterName = e.Course.Semester?.Name ?? "Current Term",
                    EnrolledAt = e.EnrolledAt,
                    HasLearningActivity = hasActivity
                });
            }

            viewModel.EnrolledCourses = enrolledList;

            // Load available courses from SQL Server
            var allCourses = await _context.Courses
                .AsNoTracking()
                .Include(c => c.Teacher)
                .Where(c => c.Status != CourseStatus.Archived)
                .OrderBy(c => c.Title)
                .ToListAsync();

            var enrolledCourseIds = enrolledList.Select(e => e.CourseId).ToHashSet();

            viewModel.AvailableCourses = allCourses.Select(c => new AvailableCourseOptionViewModel
            {
                CourseId = c.Id,
                Title = c.Title,
                Code = c.Code,
                TeacherName = c.Teacher?.FullName ?? "Instructor",
                Status = c.Status,
                IsAlreadyEnrolled = enrolledCourseIds.Contains(c.Id)
            }).ToList();
        }

        return View(viewModel);
    }

    /// <summary>
    /// Student self-service: View list of courses the authenticated student is enrolled in.
    /// Ownership security: Always resolves student identity from authenticated claims.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Student,Admin")]
    public async Task<IActionResult> MyCourses()
    {
        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var enrollments = await _enrollmentService.GetStudentEnrollmentsAsync(currentUserId);
        
        var viewModels = enrollments.Select(e => new StudentEnrollmentItemViewModel
        {
            EnrollmentId = e.Id,
            CourseId = e.CourseId,
            CourseCode = e.Course.Code,
            CourseTitle = e.Course.Title,
            Description = e.Course.Description,
            Thumbnail = e.Course.Thumbnail,
            Status = e.Course.Status,
            TeacherName = e.Course.Teacher?.FullName ?? "Instructor",
            MajorName = e.Course.Major?.Name ?? "General",
            SemesterName = e.Course.Semester?.Name ?? "Current Term",
            EnrolledAt = e.EnrolledAt
        }).ToList();

        ViewData["Title"] = "My Enrolled Courses";
        return View(viewModels);
    }

    /// <summary>
    /// Enroll a student in a course.
    /// Student callers can only enroll themselves. Admin callers can enroll any valid student.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Student,Admin")]
    public async Task<IActionResult> Enroll(int courseId, string? studentId = null, string? returnUrl = null)
    {
        if (courseId <= 0)
        {
            TempData["ErrorMessage"] = "Invalid course selection.";
            return RedirectToLocalOrFallback(returnUrl, nameof(MyCourses), null);
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");
        string targetStudentId;

        if (isAdmin && !string.IsNullOrWhiteSpace(studentId))
        {
            targetStudentId = studentId;
        }
        else
        {
            // Ownership guard: Student callers cannot specify another student's ID
            targetStudentId = currentUserId;
        }

        var result = await _enrollmentService.EnrollStudentAsync(targetStudentId, courseId, isAdmin);

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return Json(new
            {
                success = result.IsSuccess,
                status = result.Status.ToString(),
                message = result.Message,
                enrollmentId = result.EnrollmentId
            });
        }

        if (isAdmin)
        {
            return RedirectToLocalOrFallback(returnUrl, nameof(Index), new { studentId = targetStudentId });
        }

        return RedirectToLocalOrFallback(returnUrl, nameof(MyCourses), null);
    }

    /// <summary>
    /// View students enrolled in a specific course.
    /// Admin can view any course; Teachers can only view courses they teach.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> CourseStudents(int courseId)
    {
        if (courseId <= 0)
        {
            return NotFound("Invalid course ID.");
        }

        var course = await _courseService.GetCourseByIdAsync(courseId);
        if (course == null)
        {
            return NotFound("Course not found.");
        }

        var currentUserId = _userManager.GetUserId(User);
        var isAdmin = User.IsInRole("Admin");

        // Teacher ownership verification
        if (!isAdmin && course.TeacherId != currentUserId)
        {
            _logger.LogWarning("Teacher '{UserId}' attempted unauthorized access to enrollments for course {CourseId} taught by '{TeacherId}'.",
                currentUserId, courseId, course.TeacherId);
            return Forbid();
        }

        var enrollments = await _enrollmentService.GetCourseEnrollmentsAsync(courseId);
        var enrolledStudentsList = new List<CourseStudentEnrollmentViewModel>();

        foreach (var e in enrollments)
        {
            var hasActivity = await _enrollmentService.HasStudentLearningActivityAsync(e.StudentId, courseId);
            enrolledStudentsList.Add(new CourseStudentEnrollmentViewModel
            {
                EnrollmentId = e.Id,
                StudentId = e.StudentId,
                FullName = e.Student?.FullName ?? "Unknown",
                Email = e.Student?.Email ?? string.Empty,
                UniversityId = e.Student?.UniversityId,
                Avatar = e.Student?.Avatar,
                IsActive = e.Student?.IsActive ?? false,
                EnrolledAt = e.EnrolledAt,
                HasLearningActivity = hasActivity
            });
        }

        // Retrieve available students not yet enrolled (for Admin "+ Enroll Student" modal)
        var enrolledStudentIds = enrolledStudentsList.Select(s => s.StudentId).ToHashSet();
        var allStudents = await _userManager.GetUsersInRoleAsync("Student");
        var availableStudents = allStudents
            .Where(s => !enrolledStudentIds.Contains(s.Id) && s.IsActive)
            .OrderBy(s => s.FullName)
            .Select(s => new StudentLookupItemViewModel
            {
                Id = s.Id,
                FullName = s.FullName,
                Email = s.Email ?? string.Empty,
                UniversityId = s.UniversityId
            })
            .ToList();

        var viewModel = new CourseEnrollmentListViewModel
        {
            CourseId = course.Id,
            CourseCode = course.Code,
            CourseTitle = course.Title,
            TeacherId = course.TeacherId,
            TeacherName = course.Teacher?.FullName ?? "Instructor",
            Status = course.Status,
            Students = enrolledStudentsList,
            AvailableStudents = availableStudents
        };

        ViewData["Title"] = $"Enrolled Students - {course.Title}";
        ViewData["ActivePage"] = "Courses";
        return View(viewModel);
    }

    /// <summary>
    /// Check whether a student is enrolled in a specific course.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CheckStatus(int courseId, string? studentId = null)
    {
        if (courseId <= 0)
        {
            return BadRequest("Invalid course ID.");
        }

        var currentUserId = _userManager.GetUserId(User);
        var isAdminOrTeacher = User.IsInRole("Admin") || User.IsInRole("Teacher");

        var targetStudentId = (isAdminOrTeacher && !string.IsNullOrWhiteSpace(studentId))
            ? studentId
            : currentUserId;

        if (string.IsNullOrEmpty(targetStudentId))
        {
            return Unauthorized();
        }

        var isEnrolled = await _enrollmentService.IsStudentEnrolledAsync(targetStudentId, courseId);
        return Json(new { isEnrolled, studentId = targetStudentId, courseId });
    }

    /// <summary>
    /// Safely unenroll from a course.
    /// Blocked if student has recorded academic activity (progress, submissions).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Student,Admin")]
    public async Task<IActionResult> Unenroll(int courseId, string? studentId = null, int? enrollmentId = null, string? returnUrl = null)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");
        string targetStudentId = (isAdmin && !string.IsNullOrWhiteSpace(studentId))
            ? studentId
            : currentUserId;

        EnrollmentOperationResult result;

        if (enrollmentId.HasValue && enrollmentId.Value > 0)
        {
            result = await _enrollmentService.RemoveEnrollmentAsync(enrollmentId.Value, currentUserId, isAdmin);
        }
        else
        {
            result = await _enrollmentService.RemoveEnrollmentByStudentAndCourseAsync(targetStudentId, courseId, currentUserId, isAdmin);
        }

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return Json(new
            {
                success = result.IsSuccess,
                status = result.Status.ToString(),
                message = result.Message
            });
        }

        if (isAdmin)
        {
            return RedirectToLocalOrFallback(returnUrl, nameof(Index), new { studentId = targetStudentId });
        }

        return RedirectToLocalOrFallback(returnUrl, nameof(MyCourses), null);
    }

    private IActionResult RedirectToLocalOrFallback(string? returnUrl, string fallbackAction, object? routeValues)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(fallbackAction, routeValues);
    }
}
