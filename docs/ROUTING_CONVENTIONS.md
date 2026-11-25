# 🛣️ Routing & URL Naming Conventions

This document defines the official routing and URL design standards for **AUB Digital Academy**. Follow these rules when adding new controllers, views, or endpoints.

---

## 1. 🏛️ Core Route Taxonomy (Role-Based Prefixes)

Routes are structured hierarchically by role and operational domain:

| Domain / Role | Prefix | Target Audience | Example Routes |
|---|---|---|---|
| **Public Website** | `/` or `/Home` | Prospective students & general public | `/`, `/Home/Index`, `/Home/Privacy` |
| **Authentication** | `/Account` | All users (login, 2FA, password recovery) | `/Account/Login`, `/Account/Register`, `/Account/VerifyTelegram` |
| **Admin Operations** | `/Admin` | University Administrators & Superadmins | `/Admin`, `/Admin/Dashboard`, `/Admin/Settings` |
| **Academic Catalog** | `/Academic` | Academic Heads & Course Managers | `/Academic/Course`, `/Academic/Curriculum`, `/Academic/CreateCourse` |
| **Enrollment** | `/Enrollment` | Registrars & Admissions Officers | `/Enrollment`, `/Enrollment/Create`, `/Enrollment/CourseStudents/{id}` |
| **User Directory** | `/User` | User & Staff Administrators | `/User`, `/User/Create`, `/User/Details/{id}`, `/User/Edit/{id}` |
| **Faculty Portal** | `/Teacher` | Instructors & Professors | `/Teacher/Dashboard`, `/Teacher/Courses`, `/Teacher/Assignments`, `/Teacher/Grades` |
| **Student Portal** | `/Student` | Enrolled Students | `/Student/Dashboard`, `/Student/MyCourses`, `/Student/Lesson/{id}`, `/Student/Quiz/{id}` |

---

## 2. 🔤 URL Casing & Formatting Rules

### A. Controller & Action Names
* In ASP.NET Core MVC, use **PascalCase** matching the Controller and Action method names:
  * ✅ `/Academic/CreateCourse`
  * ✅ `/Teacher/MyCourses`
  * ✅ `/Student/BookReader`
  * ❌ `/academic/create_course` *(avoid snake_case)*
  * ❌ `/Teacher/my-courses` *(unless explicitly specified via `[Route("Teacher/my-courses")]`)*

### B. Route Parameters vs. Query Parameters
* **Use Route Parameters (`{id}`) for Resource Identification**:
  * ✅ `/Student/Lesson/42`
  * ✅ `/Enrollment/CourseStudents/10`
  * ✅ `/User/Details/usr_98124`
* **Use Query Parameters (`?key=value`) for Filtering, Searching, & Pagination**:
  * ✅ `/Academic/Course?page=1&pageSize=20&search=Cybersecurity`
  * ✅ `/Teacher/Submissions?status=Pending&courseId=12`
  * ✅ `/Account/Login?returnUrl=%2FStudent%2FDashboard`

---

## 3. 🎯 Route Declarations in Controllers

### Standard Pattern (Convention + Explicit Routes)
Decorate controllers and actions clearly:

```csharp
namespace AUBDigitalAcademy.Controllers;

[Authorize(Roles = "Student")]
[Route("Student")] // Base role prefix
public class StudentController : Controller
{
    // Multiple friendly route aliases for the landing dashboard
    [HttpGet]
    [Route("")]
    [Route("Dashboard")]
    [Route("Index")]
    public async Task<IActionResult> Dashboard()
    {
        // ...
        return View(viewModel);
    }

    // Detail view with integer ID route constraint
    [HttpGet]
    [Route("Lesson/{id:int}")]
    public async Task<IActionResult> Lesson(int id)
    {
        // ...
        return View(viewModel);
    }
}
```

---

## 4. ⚡ HTTP Verbs & Action Method Standards

Every action must explicitly declare its HTTP verb attribute:

```csharp
// 1. GET: Read-only, safe, idempotent (Renders View or returns Data)
[HttpGet]
public async Task<IActionResult> Edit(string id) { ... }

// 2. POST: State-altering operations (Creates, Updates, Deletes)
[HttpPost]
[ValidateAntiForgeryToken] // MANDATORY for all POST operations
public async Task<IActionResult> Edit(UserEditViewModel model) { ... }

// 3. POST/DELETE with Safe Delete Pattern:
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Delete(int id)
{
    var result = await _courseService.DeleteCourseAsync(id);
    if (!result.Success)
    {
        TempData["ErrorMessage"] = result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }
    TempData["SuccessMessage"] = "Course deleted successfully.";
    return RedirectToAction(nameof(Index));
}
```

---

## 5. 🔄 Dual Response Pattern (AJAX & HTML View Support)

Many interactive modals (e.g. Course Creation, Telegram Verification) submit via AJAX (`fetch` / `XMLHttpRequest`). Controller actions should gracefully support both:

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> CreateCourse(CourseCreateViewModel model)
{
    bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    if (!ModelState.IsValid)
    {
        if (isAjax)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return Json(new { success = false, message = "Validation failed.", errors });
        }
        await PopulateDropdownsAsync(model);
        return View(model);
    }

    var createdCourse = await _courseService.CreateCourseAsync(model);

    if (isAjax)
    {
        return Json(new
        {
            success = true,
            message = "Course created successfully.",
            courseId = createdCourse.Id,
            redirectUrl = Url.Action("Course", "Academic")
        });
    }

    TempData["SuccessMessage"] = "Course created successfully.";
    return RedirectToAction(nameof(Course));
}
```

---

## 6. 🚫 Anti-Patterns to Avoid

| Anti-Pattern | Why It Is Dangerous | Correct Alternative |
|---|---|---|
| **Using `GET` for State Changes** (e.g. `/Course/Delete?id=5`) | Search engine crawlers or browser pre-fetching can trigger accidental deletion! | Always use `[HttpPost]` with `[ValidateAntiForgeryToken]`. |
| **Hardcoding URLs in Views** (e.g. `<a href="/Student/Dashboard">`) | Breaks if route patterns change; no compile-time checking. | Use ASP.NET Tag Helpers: `<a asp-controller="Student" asp-action="Dashboard">`. |
| **Catch-All Ambiguous Routes** (e.g. `{controller}/{action}/{*catchall}`) | Interferes with Identity and Area route resolution. | Use strongly constrained parameter routes like `{id:int}` or `{slug:alpha}`. |
