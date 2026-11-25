# 🏛️ MVC Architecture & Razor Guide

This guide explains how the Model-View-Controller (MVC) architecture is organized in **AUB Digital Academy**, including the Razor layout hierarchy, ViewModels, and services.

---

## 1. 🗂️ Project Directory Breakdown

```text
AUB-Digital-Academy-Backend/
├── 📁 Controllers/            # MVC Controller endpoints (Account, Academic, Admin, Student, Teacher)
├── 📁 Data/                   # EF Core DbContext, SeedData, Schema Initializers
├── 📁 Models/                 # Domain Entities (Course, User, Chapter, Quiz)
│   ├── 📁 Enums/              # Domain Enums (CourseStatus, ItemType, etc.)
│   └── 📁 ViewModels/         # Strongly-typed Razor ViewModels & DTOs
├── 📁 Services/               # Business Logic Layer
│   ├── 📁 Interfaces/         # Service contracts (ICourseService, ITelegramService)
│   └── 📁 Implementations/    # Concrete business implementations
├── 📁 Views/                  # Razor View Templates (.cshtml)
│   ├── 📁 Academic/           # Course Management, Curriculum Editor, Quizzes
│   ├── 📁 Account/            # Login, Register, VerifyTelegram, ResetPassword
│   ├── 📁 Admin/              # Admin Executive Dashboard
│   ├── 📁 Home/               # Public Landing Website
│   ├── 📁 Shared/             # Master Layouts & Shared Partials
│   ├── 📁 Student/            # Student Workspace & Lesson Player
│   └── 📁 Teacher/            # Faculty Workspace, Grading & Quizzes
└── 📁 wwwroot/                # Static assets (CSS, JS, Bootstrap, Images, Logos)
```

---

## 2. 🎨 Razor Layout Hierarchy

The application uses dedicated, role-specific layouts located in [Views/Shared/](file:///d:/AUB_PROJECT/AUB-Digital-Academy-Backend/Views/Shared):

| Layout File | Intended Use | Features |
|---|---|---|
| **`_Layout.cshtml`** | Authenticated Portals (Admin, Teacher, Student) | Collapsible 240px SaaS sidebar, dark/light theme switch, breadcrumbs, top navigation bar, notification popovers. |
| **`_WelcomeLayout.cshtml`** | Public Landing Page (`Home/Index`) | Transparent-to-solid sticky navbar, announcement bar, dynamic footer, smooth scroll anchors. |
| **`_AuthLayout.cshtml`** | Sign-In & Registration (`Account/*`) | Split-screen layout: Interactive constellation canvas on the left, authentication cards on the right. |

### Shared Partial Views:
* `_Sidebar.cshtml`: Dynamic navigation links based on user role (`User.IsInRole("Admin")`, etc.).
* `_Navbar.cshtml`: Top navbar with quick search, profile dropdown, and logout confirmation.
* `_ValidationScriptsPartial.cshtml`: jQuery validation and unobtrusive validation scripts.

---

## 3. 📦 ViewModels vs. Domain Entities

| Concept | Domain Entity (`Models/`) | ViewModel (`Models/ViewModels/`) |
|---|---|---|
| **Purpose** | Mirrors database tables and relationships. | Mirrors what a specific UI page or form needs. |
| **Example** | `Course.cs` (contains DB keys, foreign keys, timestamps, navigation collections). | `CourseCreateViewModel.cs` (contains form fields, dropdown `SelectList` items, client validation attributes). |
| **Security** | Dangerous to bind directly to HTTP POST requests (risk of mass assignment). | Safe to bind because only intended fields are included. |

### Example: Building a ViewModel in a Controller

```csharp
[HttpGet]
public async Task<IActionResult> Course()
{
    var courses = await _courseService.GetAllCoursesAsync();
    var categories = await _context.Categories.AsNoTracking().ToListAsync();

    var viewModel = new CourseManagementViewModel
    {
        Courses = courses,
        Categories = categories.Select(c => new SelectListItem(c.Name, c.Id.ToString())),
        TotalCourses = courses.Count,
        PublishedCourses = courses.Count(c => c.Status == CourseStatus.Published)
    };

    return View(viewModel);
}
```

---

## 4. 🏷️ Razor Tag Helpers Reference

Always prefer ASP.NET Core Tag Helpers over raw HTML anchors or form tags:

```razor
<!-- ✅ Standard Controller Action Link -->
<a asp-controller="Academic" asp-action="Course" class="btn btn-primary">
    Manage Courses
</a>

<!-- ✅ Link with Route ID Parameter -->
<a asp-controller="Student" asp-action="Lesson" asp-route-id="@lesson.Id">
    Start Lesson
</a>

<!-- ✅ Secure Anti-Forgery Form -->
<form asp-controller="Account" asp-action="Login" method="post">
    <!-- Form fields using asp-for -->
    <div class="mb-3">
        <label asp-for="LoginId" class="form-label">Student ID</label>
        <input asp-for="LoginId" class="form-control" />
        <span asp-validation-for="LoginId" class="text-danger small"></span>
    </div>
    <button type="submit" class="btn btn-primary">Sign In</button>
</form>
```

---

## 5. 🌐 Serving Static Assets (`wwwroot/`)

All static files are hosted in `wwwroot/`:
* `css/`: Theme variables, component styling, layout sheets.
* `js/`: Modular UI scripts, chart initialization, interactive hero canvas.
* `lib/`: Vendored libraries (Bootstrap 5.3, Bootstrap Icons, jQuery Validation).

### Cache Busting with `asp-append-version`
Always include `asp-append-version="true"` when linking local CSS and JS files in Razor views. This appends a unique SHA256 hash to the URL, guaranteeing users immediately receive updated assets when you deploy changes:

```razor
<link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
<script src="~/js/dashboard.js" asp-append-version="true"></script>
```
