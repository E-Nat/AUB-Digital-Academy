using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            var categories = await _context.Categories
                .Include(c => c.Majors)
                .AsNoTracking()
                .ToListAsync();

            var courses = await _context.Courses
                .Include(c => c.Major)
                    .ThenInclude(m => m.Category)
                .Include(c => c.Teacher)
                .Include(c => c.Semester)
                .Include(c => c.Chapters)
                    .ThenInclude(ch => ch.CourseItems)
                .Where(c => c.Status == CourseStatus.Published)
                .OrderBy(c => c.Code)
                .AsNoTracking()
                .ToListAsync();

            var totalStudentsCount = await _context.Users
                .AsNoTracking()
                .CountAsync(u => u.IsActive);

            ViewBag.Categories = categories;
            ViewBag.Majors = categories.SelectMany(c => c.Majors).ToList();
            ViewBag.Courses = courses;
            ViewBag.PublishedCoursesCount = courses.Count;
            ViewBag.TotalStudentsCount = totalStudentsCount > 0 ? totalStudentsCount : 1200;
            ViewBag.TotalProgramsCount = categories.SelectMany(c => c.Majors).Count();
        }
        catch
        {
            ViewBag.Categories = new List<Category>();
            ViewBag.Majors = new List<Major>();
            ViewBag.Courses = new List<Course>();
            ViewBag.PublishedCoursesCount = 0;
            ViewBag.TotalStudentsCount = 1200;
            ViewBag.TotalProgramsCount = 0;
        }

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
