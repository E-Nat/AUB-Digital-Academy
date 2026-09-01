using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;

namespace AUBDigitalAcademy.Controllers;

[Authorize(Roles = "Teacher,Admin")]
public class TeacherController : Controller
{
    private readonly ApplicationDbContext? _context;
    private readonly UserManager<User>? _userManager;
    private readonly ILogger<TeacherController>? _logger;

    public TeacherController(
        ApplicationDbContext? context = null,
        UserManager<User>? userManager = null,
        ILogger<TeacherController>? logger = null)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        ViewData["Title"] = "Teacher Dashboard";
        ViewData["ActivePage"] = "Dashboard";

        var viewModel = new TeacherDashboardViewModel
        {
            CurrentTerm = "Spring Semester 2026",
            AvailableTerms = new List<string>
            {
                "Spring Semester 2026",
                "Fall Semester 2025",
                "Summer Term 2025"
            },
            ActiveCoursesCount = 3,
            PendingReviewsCount = 14,
            UpcomingSessionsCount = 5,
            AttentionItems = new List<TeacherAttentionItemViewModel>
            {
                new()
                {
                    AssignmentId = 101,
                    Title = "REST API Project",
                    CourseName = "Full-Stack Web Development",
                    PendingCount = 8,
                    IsUrgent = true,
                    DueDateFormatted = "Due yesterday",
                    ReviewUrl = "/Teacher/Submissions?assignmentId=101"
                },
                new()
                {
                    AssignmentId = 102,
                    Title = "Frontend Responsive UI Layout",
                    CourseName = "Full-Stack Web Development",
                    PendingCount = 4,
                    IsUrgent = false,
                    DueDateFormatted = "Due in 2 days",
                    ReviewUrl = "/Teacher/Submissions?assignmentId=102"
                },
                new()
                {
                    AssignmentId = 103,
                    Title = "Data Cleaning & Visualization",
                    CourseName = "Python for Data Science & AI",
                    PendingCount = 2,
                    IsUrgent = false,
                    DueDateFormatted = "Due in 3 days",
                    ReviewUrl = "/Teacher/Submissions?assignmentId=103"
                }
            },
            TodaySchedule = new List<TeacherScheduleItemViewModel>
            {
                new()
                {
                    Id = 1,
                    StudentName = "Sokha Chan",
                    StudentAvatar = "",
                    StudentInitials = "SC",
                    Topic = "Backend Architecture",
                    TimeDisplay = "Today · 3:00 PM",
                    SessionUrl = "/Teacher/Sessions/1",
                    IsToday = true
                },
                new()
                {
                    Id = 2,
                    StudentName = "Sreyneng Sok",
                    StudentAvatar = "",
                    StudentInitials = "SS",
                    Topic = "Database Schema Normalization",
                    TimeDisplay = "Tomorrow · 2:00 PM",
                    SessionUrl = "/Teacher/Sessions/2",
                    IsToday = false
                }
            },
            Courses = new List<TeacherCourseItemViewModel>
            {
                new()
                {
                    Id = 1,
                    Title = "Full-Stack Web Development",
                    CoverImageUrl = "",
                    StudentCount = 45,
                    ModuleCount = 5,
                    ManageUrl = "/Teacher/Courses/1",
                    GradientClass = "course-gradient-1",
                    IconClass = "bi-code-slash"
                },
                new()
                {
                    Id = 2,
                    Title = "Python for Data Science & AI",
                    CoverImageUrl = "",
                    StudentCount = 38,
                    ModuleCount = 6,
                    ManageUrl = "/Teacher/Courses/2",
                    GradientClass = "course-gradient-2",
                    IconClass = "bi-cpu"
                },
                new()
                {
                    Id = 3,
                    Title = "Database Systems & Architecture",
                    CoverImageUrl = "",
                    StudentCount = 52,
                    ModuleCount = 4,
                    ManageUrl = "/Teacher/Courses/3",
                    GradientClass = "course-gradient-3",
                    IconClass = "bi-database"
                }
            }
        };

        // If context and current user exist with real courses, blend DB counts
        if (_context != null && _userManager != null && User.Identity?.IsAuthenticated == true)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser != null)
                {
                    var dbCourses = await _context.Courses
                        .Where(c => c.TeacherId == currentUser.Id)
                        .Include(c => c.Enrollments)
                        .Include(c => c.Chapters)
                        .Take(3)
                        .ToListAsync();

                    if (dbCourses.Any())
                    {
                        viewModel.ActiveCoursesCount = await _context.Courses.CountAsync(c => c.TeacherId == currentUser.Id);
                        viewModel.Courses = dbCourses.Select((c, index) => new TeacherCourseItemViewModel
                        {
                            Id = c.Id,
                            Title = c.Title,
                            CoverImageUrl = c.Thumbnail ?? "",
                            StudentCount = c.Enrollments.Count,
                            ModuleCount = c.Chapters.Count,
                            ManageUrl = $"/Teacher/Courses/{c.Id}",
                            GradientClass = $"course-gradient-{(index % 3) + 1}",
                            IconClass = index % 3 == 0 ? "bi-code-slash" : index % 3 == 1 ? "bi-cpu" : "bi-database"
                        }).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not fetch dynamic teacher dashboard DB data, using default view model.");
            }
        }

        return View(viewModel);
    }

    [HttpGet]
    public IActionResult Courses()
    {
        ViewData["Title"] = "My Courses";
        ViewData["ActivePage"] = "Courses";
        return View();
    }

    [HttpGet]
    public IActionResult Assignments()
    {
        ViewData["Title"] = "Assignments";
        ViewData["ActivePage"] = "Assignments";
        return View();
    }

    [HttpGet]
    public IActionResult Submissions()
    {
        ViewData["Title"] = "Submissions";
        ViewData["ActivePage"] = "Submissions";
        return View("Assignments");
    }

    [HttpGet]
    public IActionResult Grades()
    {
        ViewData["Title"] = "Grades";
        ViewData["ActivePage"] = "Grades";
        return View();
    }

    [HttpGet]
    public IActionResult Books()
    {
        ViewData["Title"] = "Books & Resources";
        ViewData["ActivePage"] = "Books";
        return View();
    }

    [HttpGet]
    public IActionResult Quizzes()
    {
        ViewData["Title"] = "Quizzes";
        ViewData["ActivePage"] = "Quizzes";
        return View();
    }
}
