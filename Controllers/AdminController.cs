using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;

namespace AUBDigitalAcademy.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext? _context;
    private readonly UserManager<User>? _userManager;

    public AdminController(ApplicationDbContext? context = null, UserManager<User>? userManager = null)
    {
        _context = context;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Executive Command Center";
        ViewData["ActivePage"] = "Dashboard";

        var model = new AdminDashboardViewModel
        {
            CurrentDateFormatted = DateTime.Now.ToString("MMMM d, yyyy"),
            AdminFullName = User.Identity?.Name ?? "Administrator"
        };

        if (_userManager != null)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null && !string.IsNullOrWhiteSpace(currentUser.FullName))
            {
                model.AdminFullName = currentUser.FullName;
            }

            var students = await _userManager.GetUsersInRoleAsync("Student");
            var teachers = await _userManager.GetUsersInRoleAsync("Teacher");
            model.TotalStudents = students.Count;
            model.TotalTeachers = teachers.Count;
        }

        if (_context != null)
        {
            // 1. Core KPIs
            model.TotalCourses = await _context.Courses.CountAsync();
            model.PublishedCourses = await _context.Courses.CountAsync(c => c.Status == CourseStatus.Published);
            model.DraftCourses = model.TotalCourses - model.PublishedCourses;
            model.TotalModules = await _context.Chapters.CountAsync();
            model.TotalEnrollments = await _context.Enrollments.CountAsync();
            model.TotalQuizzes = await _context.Quizzes.CountAsync();

            // 2. Term Metadata
            var currentSem = await _context.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.IsCurrent);
            if (currentSem != null)
            {
                model.CurrentSemesterName = currentSem.Name;
            }

            // 3. Majors Distribution (for Donut Chart & Legend)
            var majors = await _context.Majors
                .AsNoTracking()
                .Include(m => m.Courses)
                    .ThenInclude(c => c.Enrollments)
                .ToListAsync();

            var colorPalette = new[] { "#2563EB", "#0891B2", "#D97706", "#16A34A", "#7C3AED", "#4F46E5" };
            int totalMajorStudents = majors.Sum(m => m.Courses.Sum(c => c.Enrollments.Count));
            int colorIdx = 0;

            foreach (var m in majors)
            {
                int studentCount = m.Courses.Sum(c => c.Enrollments.Count);
                int courseCount = m.Courses.Count;
                double pct = totalMajorStudents > 0 ? Math.Round((double)studentCount / totalMajorStudents * 100, 1) : 0;

                model.MajorsDistribution.Add(new MajorEnrollmentStatViewModel
                {
                    MajorName = m.Name,
                    MajorCode = m.Code,
                    StudentCount = studentCount,
                    CourseCount = courseCount,
                    Percentage = pct,
                    ColorHex = colorPalette[colorIdx % colorPalette.Length]
                });
                colorIdx++;
            }

            // 4. Semesters Distribution (for Trend / Bar Chart)
            var semesters = await _context.Semesters
                .AsNoTracking()
                .Include(s => s.Courses)
                    .ThenInclude(c => c.Enrollments)
                .OrderBy(s => s.StartDate)
                .Take(6)
                .ToListAsync();

            foreach (var sem in semesters)
            {
                model.SemesterDistribution.Add(new SemesterCourseStatViewModel
                {
                    SemesterName = sem.Name,
                    SemesterCode = sem.Code,
                    CourseCount = sem.Courses.Count,
                    EnrollmentCount = sem.Courses.Sum(c => c.Enrollments.Count),
                    IsCurrent = sem.IsCurrent
                });
            }

            // 5. Recent Student Enrollments (up to 5)
            var recentEnrollmentsList = await _context.Enrollments
                .AsNoTracking()
                .Include(e => e.Student)
                .Include(e => e.Course)
                    .ThenInclude(c => c.Chapters)
                        .ThenInclude(ch => ch.CourseItems)
                .OrderByDescending(e => e.EnrolledAt)
                .Take(5)
                .ToListAsync();

            var studentIds = recentEnrollmentsList.Select(e => e.StudentId).Distinct().ToList();
            var progressRecords = await _context.CourseProgresses
                .AsNoTracking()
                .Where(cp => studentIds.Contains(cp.StudentId) && cp.IsCompleted)
                .ToListAsync();

            foreach (var e in recentEnrollmentsList)
            {
                int totalItems = e.Course?.Chapters.Sum(ch => ch.CourseItems.Count) ?? 0;
                var courseItemIds = e.Course?.Chapters.SelectMany(ch => ch.CourseItems.Select(ci => ci.Id)).ToHashSet() ?? new HashSet<int>();
                int completedItems = progressRecords.Count(cp => cp.StudentId == e.StudentId && courseItemIds.Contains(cp.CourseItemId));
                int progPct = totalItems > 0 ? (int)Math.Round((double)completedItems / totalItems * 100) : 0;

                model.RecentEnrollments.Add(new DashboardRecentEnrollmentViewModel
                {
                    EnrollmentId = e.Id,
                    StudentId = e.StudentId,
                    StudentName = e.Student?.FullName ?? "Student",
                    UniversityId = e.Student?.UniversityId ?? "N/A",
                    AvatarUrl = e.Student?.Avatar,
                    CourseId = e.CourseId,
                    CourseCode = e.Course?.Code ?? "AUB",
                    CourseTitle = e.Course?.Title ?? "Course",
                    EnrolledAt = e.EnrolledAt,
                    ProgressPercentage = Math.Min(100, Math.Max(0, progPct)),
                    Status = progPct >= 100 ? "Completed" : "Active"
                });
            }

            // 6. Upcoming / Published Quizzes (up to 4)
            var recentQuizzesList = await _context.Quizzes
                .AsNoTracking()
                .Include(q => q.CourseItem)
                    .ThenInclude(ci => ci.Chapter)
                        .ThenInclude(ch => ch.Course)
                .Include(q => q.Questions)
                .Where(q => q.IsPublished)
                .OrderByDescending(q => q.Id)
                .Take(4)
                .ToListAsync();

            foreach (var q in recentQuizzesList)
            {
                model.RecentQuizzes.Add(new DashboardQuizItemViewModel
                {
                    QuizId = q.Id,
                    Title = q.CourseItem?.Title ?? "Course Quiz",
                    CourseTitle = q.CourseItem?.Chapter?.Course?.Title ?? "Academic Course",
                    CourseCode = q.CourseItem?.Chapter?.Course?.Code ?? "AUB",
                    TimeLimitMinutes = q.TimeLimitMinutes,
                    PassingScore = q.PassingScore,
                    QuestionCount = q.Questions.Count,
                    IsPublished = q.IsPublished
                });
            }

            // 7. Real Recent Institutional Activity Log
            var recentActivities = new List<DashboardActivityItemViewModel>();

            foreach (var e in recentEnrollmentsList.Take(3))
            {
                recentActivities.Add(new DashboardActivityItemViewModel
                {
                    Title = $"Student Enrolled: {e.Student?.FullName ?? "Student"}",
                    Description = $"Enrolled in {e.Course?.Code} - {e.Course?.Title}",
                    Timestamp = e.EnrolledAt,
                    IconClass = "bi-person-check-fill",
                    IconColorClass = "icon-green"
                });
            }

            var recentSubmissions = await _context.QuizSubmissions
                .AsNoTracking()
                .Include(qs => qs.Student)
                .Include(qs => qs.Quiz)
                    .ThenInclude(q => q.CourseItem)
                .OrderByDescending(qs => qs.SubmittedAt)
                .Take(3)
                .ToListAsync();

            foreach (var qs in recentSubmissions)
            {
                recentActivities.Add(new DashboardActivityItemViewModel
                {
                    Title = $"Quiz Completed: {qs.Student?.FullName ?? "Student"}",
                    Description = $"Score: {qs.Score:F1}% on {qs.Quiz?.CourseItem?.Title ?? "Evaluation"} ({(qs.IsPassed ? "Passed" : "Did not pass")})",
                    Timestamp = qs.SubmittedAt,
                    IconClass = "bi-clipboard-check-fill",
                    IconColorClass = "icon-purple"
                });
            }

            var recentCreatedCourses = await _context.Courses
                .AsNoTracking()
                .Include(c => c.Teacher)
                .OrderByDescending(c => c.CreatedAt)
                .Take(2)
                .ToListAsync();

            foreach (var c in recentCreatedCourses)
            {
                recentActivities.Add(new DashboardActivityItemViewModel
                {
                    Title = $"Course Created: {c.Code}",
                    Description = $"{c.Title} (Instructor: {c.Teacher?.FullName ?? "Faculty"})",
                    Timestamp = c.CreatedAt,
                    IconClass = "bi-journal-bookmark-fill",
                    IconColorClass = "icon-cyan"
                });
            }

            model.RecentActivities = recentActivities
                .OrderByDescending(a => a.Timestamp)
                .Take(6)
                .Select(a =>
                {
                    var span = DateTime.UtcNow - a.Timestamp;
                    if (span.TotalMinutes < 60)
                        a.TimeAgo = $"{(int)Math.Max(1, span.TotalMinutes)}m ago";
                    else if (span.TotalHours < 24)
                        a.TimeAgo = $"{(int)span.TotalHours}h ago";
                    else
                        a.TimeAgo = $"{(int)span.TotalDays}d ago";
                    return a;
                })
                .ToList();
        }

        return View(model);
    }

    [HttpGet]
    public IActionResult Dashboard()
    {
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Courses()
    {
        return RedirectToAction("Course", "Academic");
    }

    [HttpGet]
    public IActionResult Enrollments(string? studentId = null)
    {
        return RedirectToAction("Index", "Enrollment", new { studentId });
    }

    [HttpGet]
    public IActionResult Users(string? role = null, string? search = null, bool? status = null)
    {
        return RedirectToAction("Index", "User", new { role, search, status });
    }
}

