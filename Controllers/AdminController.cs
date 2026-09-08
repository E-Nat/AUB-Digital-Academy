using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AUBDigitalAcademy.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Admin Dashboard";
        ViewData["ActivePage"] = "Dashboard";
        return View();
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
}
