using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Controllers;

[Authorize(Roles = "Teacher,Assistance,Admin")]
public class TeacherController : Controller
{
    private readonly ITeacherService _teacherService;
    private readonly IAssignmentStorageService _assignmentStorageService;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<TeacherController> _logger;
    private readonly IHtmlSanitizationService _htmlSanitizer;

    public TeacherController(
        ITeacherService teacherService,
        IAssignmentStorageService assignmentStorageService,
        ApplicationDbContext context,
        UserManager<User> userManager,
        ILogger<TeacherController> logger,
        IHtmlSanitizationService? htmlSanitizer = null)
    {
        _teacherService = teacherService;
        _assignmentStorageService = assignmentStorageService;
        _context = context;
        _userManager = userManager;
        _logger = logger;
        _htmlSanitizer = htmlSanitizer ?? new AUBDigitalAcademy.Services.Implementations.HtmlSanitizationService();
    }

    private async Task<string> GetCurrentTeacherIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        return user?.Id ?? string.Empty;
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        ViewData["Title"] = "Teacher Dashboard";
        ViewData["ActivePage"] = "Dashboard";

        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId))
        {
            return RedirectToAction("Login", "Account");
        }

        var viewModel = await _teacherService.GetTeacherDashboardAsync(teacherId);
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Courses()
    {
        ViewData["Title"] = "My Courses";
        ViewData["ActivePage"] = "Courses";

        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId)) return RedirectToAction("Login", "Account");

        var courses = await _teacherService.GetTeacherCoursesAsync(teacherId);
        return View(courses);
    }

    [HttpGet]
    [Route("Teacher/CourseDetails/{id:int?}")]
    [Route("Teacher/Courses/Details/{id:int}")]
    public async Task<IActionResult> CourseDetails(int id)
    {
        ViewData["Title"] = "Course Details";
        ViewData["ActivePage"] = "Courses";

        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId)) return RedirectToAction("Login", "Account");

        var courseDetails = await _teacherService.GetTeacherCourseDetailsAsync(id, teacherId);
        if (courseDetails == null)
        {
            TempData["ErrorMessage"] = "Course not found or you do not have permission to view it.";
            return RedirectToAction(nameof(Courses));
        }

        return View(courseDetails);
    }

    [HttpGet]
    public async Task<IActionResult> Assignments(int? courseId = null)
    {
        ViewData["Title"] = "Assignments";
        ViewData["ActivePage"] = "Assignments";

        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId)) return RedirectToAction("Login", "Account");

        ViewData["SelectedCourseId"] = courseId;
        var assignments = await _teacherService.GetTeacherAssignmentsAsync(teacherId, courseId);
        return View(assignments);
    }

    [HttpGet]
    public async Task<IActionResult> Submissions(int assignmentId)
    {
        ViewData["Title"] = "Assignment Submissions";
        ViewData["ActivePage"] = "Assignments";

        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId)) return RedirectToAction("Login", "Account");

        var assignment = await _context.Assignments
            .Include(a => a.CourseItem)
                .ThenInclude(ci => ci.Chapter)
            .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment == null)
        {
            TempData["ErrorMessage"] = "Assignment not found.";
            return RedirectToAction(nameof(Assignments));
        }

        var courseId = assignment.CourseItem?.Chapter?.CourseId ?? 0;
        if (!await _teacherService.HasAccessToCourseAsync(courseId, teacherId))
        {
            TempData["ErrorMessage"] = "You do not have access to this course's submissions.";
            return RedirectToAction(nameof(Assignments));
        }

        ViewData["AssignmentId"] = assignmentId;
        var submissions = (await _teacherService.GetAssignmentSubmissionsAsync(assignmentId, teacherId)).ToList();
        ViewBag.CanGrade = await _teacherService.CanGradeCourseAsync(courseId, teacherId);
        ViewBag.AssignmentTitle = assignment.CourseItem?.Title ?? "Assignment";
        ViewBag.MaxScore = assignment.MaxScore;
        return View(submissions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GradeSubmission(GradeSubmissionInputModel model)
    {
        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId)) return RedirectToAction("Login", "Account");

        var submission = await _context.AssignmentSubmissions.FirstOrDefaultAsync(s => s.Id == model.SubmissionId);
        int assignmentId = submission?.AssignmentId ?? 0;

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Invalid grade data submitted. Score must be a positive number.";
            return RedirectToAction(nameof(Submissions), new { assignmentId = assignmentId > 0 ? assignmentId : model.SubmissionId });
        }

        var result = await _teacherService.GradeSubmissionAsync(model.SubmissionId, model.Score, model.Feedback, teacherId);
        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        if (assignmentId > 0)
        {
            return RedirectToAction(nameof(Submissions), new { assignmentId });
        }

        return RedirectToAction(nameof(Assignments));
    }

    [HttpGet]
    [Route("Teacher/Assignment/Download/{submissionId:int}")]
    public async Task<IActionResult> DownloadSubmission(int submissionId)
    {
        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId)) return Challenge();

        var submission = await _context.AssignmentSubmissions
            .Include(s => s.Assignment)
                .ThenInclude(a => a.CourseItem)
                    .ThenInclude(ci => ci.Chapter)
            .FirstOrDefaultAsync(s => s.Id == submissionId);

        if (submission == null) return NotFound("Submission not found.");

        var courseId = submission.Assignment?.CourseItem?.Chapter?.CourseId ?? 0;
        if (!await _teacherService.HasAccessToCourseAsync(courseId, teacherId))
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(submission.FileUrl))
        {
            return BadRequest("No file attached to this submission.");
        }

        if (submission.FileUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            submission.FileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var safeUrl = _htmlSanitizer.SanitizeHttpUrl(submission.FileUrl);
            if (safeUrl == null)
            {
                return BadRequest("Invalid submission URL.");
            }
            return Redirect(safeUrl);
        }

        var fileResult = await _assignmentStorageService.GetAssignmentFileAsync(submission.FileUrl, submission.OriginalFileName);
        if (fileResult == null || fileResult.Value.FileBytes == null)
        {
            return NotFound("File not found on server.");
        }

        return File(fileResult.Value.FileBytes, fileResult.Value.ContentType, fileResult.Value.DownloadFileName);
    }

    [HttpGet]
    [Route("Teacher/Assignment/DownloadAttachment/{attachmentId:int}")]
    public async Task<IActionResult> DownloadAttachment(int attachmentId)
    {
        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId)) return Challenge();

        var attachment = await _context.AssignmentAttachments
            .Include(a => a.Submission)
                .ThenInclude(s => s.Assignment)
                    .ThenInclude(a => a.CourseItem)
                        .ThenInclude(ci => ci.Chapter)
            .FirstOrDefaultAsync(a => a.Id == attachmentId);

        if (attachment == null || attachment.IsDeleted) return NotFound("Attachment not found or has been removed.");

        var courseId = attachment.Submission?.Assignment?.CourseItem?.Chapter?.CourseId ?? 0;
        if (!await _teacherService.HasAccessToCourseAsync(courseId, teacherId) && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var fileResult = await _assignmentStorageService.GetAssignmentFileAsync(attachment.StoragePath, attachment.OriginalFileName);
        if (fileResult == null || fileResult.Value.FileBytes == null)
        {
            return NotFound("File not found on server.");
        }

        return File(fileResult.Value.FileBytes, fileResult.Value.ContentType, fileResult.Value.DownloadFileName);
    }

    [HttpGet]
    public async Task<IActionResult> Grades(int? courseId = null)
    {
        ViewData["Title"] = "Student Grades";
        ViewData["ActivePage"] = "Grades";

        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId)) return RedirectToAction("Login", "Account");

        var grades = await _teacherService.GetTeacherGradesAsync(teacherId, courseId);
        return View(grades);
    }

    [HttpGet]
    public async Task<IActionResult> Quizzes(int? courseId = null)
    {
        ViewData["Title"] = "Quizzes Overview";
        ViewData["ActivePage"] = "Quizzes";

        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId)) return RedirectToAction("Login", "Account");

        var quizzes = await _teacherService.GetTeacherQuizzesAsync(teacherId, courseId);
        return View(quizzes);
    }

    [HttpGet]
    public async Task<IActionResult> Books(int? courseId = null)
    {
        ViewData["Title"] = "Books & Resources";
        ViewData["ActivePage"] = "Books";

        var teacherId = await GetCurrentTeacherIdAsync();
        if (string.IsNullOrEmpty(teacherId)) return RedirectToAction("Login", "Account");

        var books = await _teacherService.GetTeacherBooksAsync(teacherId, courseId);
        return View(books);
    }
}
