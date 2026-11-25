# 💎 Clean Code Principles & C# Standards

This document establishes the C# and .NET 10 clean code standards for **AUB Digital Academy**. All developers should follow these rules to ensure maintainability, performance, and security.

---

## 1. 🏗️ Architecture Layering & Responsibilities

```
┌──────────────────────────────────────────────────────────┐
│                   Controllers / Views                    │
│    (HTTP Handling, Routing, Model Validation, Razor)     │
└────────────────────────────┬─────────────────────────────┘
                             │ Calls
┌────────────────────────────▼─────────────────────────────┐
│                      Service Layer                       │
│  (Business Logic, Calculations, Telegram Bot, Workflow)  │
└────────────────────────────┬─────────────────────────────┘
                             │ Calls
┌────────────────────────────▼─────────────────────────────┐
│                       Data Layer                         │
│     (ApplicationDbContext, EF Core, Repositories)       │
└──────────────────────────────────────────────────────────┘
```

### A. Controllers
* **Controllers must be thin**: Handle incoming HTTP requests, validate `ModelState`, delegate work to services, and return an `IActionResult` (`View`, `Json`, `Redirect`).
* Do not write business logic, database transactions, or Telegram API calls directly in controllers.

### B. Services (`Services/Interfaces/` & `Services/Implementations/`)
* Contains the business logic.
* Always define an interface (`ICourseService`) paired with an implementation (`CourseService`).
* Register services in `Program.cs` with the appropriate lifetime (typically `AddScoped`).

### C. Models vs. ViewModels (`Models/` vs `Models/ViewModels/`)
* **Domain Models (`Models/`)**: Represent database entities (`Course`, `Chapter`, `User`, `Enrollment`).
* **ViewModels (`Models/ViewModels/`)**: Represent data shaped specifically for a Razor View or form submission.
* **Rule**: Never bind HTTP forms directly to Domain Models (prevents over-posting attacks). Always bind to a strongly typed ViewModel.

---

## 2. 🔌 Dependency Injection & Constructor Rules

### The Ambiguous Constructor Rule (Critical)
ASP.NET Core's DI engine (`ActivatorUtilities`) will throw an `InvalidOperationException` if a class has multiple constructors whose parameters can all be resolved by DI.

* **Rule**: If you provide an alternate constructor for unit tests, **always decorate the DI constructor with `[ActivatorUtilitiesConstructor]`**:

```csharp
public class StudentController : Controller
{
    // Constructor 1: Used by Unit Tests with manual mocks
    public StudentController(
        IEnrollmentService enrollmentService,
        ICourseService courseService,
        UserManager<User> userManager,
        ApplicationDbContext context,
        ILogger<StudentController> logger)
    { ... }

    // Constructor 2: Used by ASP.NET Core Runtime Dependency Injection
    [ActivatorUtilitiesConstructor]
    public StudentController(
        IEnrollmentService enrollmentService,
        ICourseService courseService,
        ICurriculumService curriculumService,
        UserManager<User> userManager,
        ApplicationDbContext context,
        ILogger<StudentController> logger)
    { ... }
}
```

---

## 3. ⚡ Async / Await Standards

All I/O operations (database queries, HTTP calls, file I/O) must be fully asynchronous.

* ✅ **Do**: Use `async Task<IActionResult>`, `await context.SaveChangesAsync()`, `await ToListAsync()`.
* ❌ **Never Do**: Call `.Result` or `.Wait()` on asynchronous tasks. This blocks the thread pool and can cause deadlocks!

```csharp
// ❌ ANTI-PATTERN: Blocking call
var courses = _context.Courses.ToList();
var user = _userManager.FindByIdAsync(id).Result; // Can deadlock!

// ✅ CLEAN PRACTICE: Non-blocking asynchronous execution
var courses = await _context.Courses.AsNoTracking().ToListAsync();
var user = await _userManager.FindByIdAsync(id);
```

---

## 4. 🚀 Entity Framework Core Performance Rules

### A. Use `AsNoTracking()` for Read-Only Queries
When rendering pages or returning JSON where entities will not be modified or saved:
```csharp
// Saves memory and CPU cycles by disabling EF Core change tracking
var courses = await _context.Courses
    .Include(c => c.Teacher)
    .Include(c => c.Major)
    .AsNoTracking()
    .ToListAsync();
```

### B. Prevent N+1 Query Traps
Always eagerly load required child navigation properties with `.Include()` and `.ThenInclude()`, rather than accessing them in loops:

```csharp
// ✅ Pre-loads Course -> Chapters -> CourseItems in a single optimized SQL query
var course = await _context.Courses
    .Include(c => c.Chapters)
        .ThenInclude(ch => ch.CourseItems)
    .FirstOrDefaultAsync(c => c.Id == id);
```

---

## 5. 🛡️ Security & Defensive Coding

1. **Anti-Forgery Protection**:
   * Every `<form>` submitting via POST must include `@Html.AntiForgeryToken()`.
   * Every POST/PUT/DELETE action method must be decorated with `[ValidateAntiForgeryToken]`.
2. **Never Log Sensitive Information**:
   * Do not log passwords, connection strings, or Telegram verification codes in production.
   * In local development simulation mode, log codes with a clear `[DEV SIMULATION]` prefix.
3. **Safe Deletion Guardrails**:
   * Enforce business logic before deleting records (e.g. do not delete a course if students are already enrolled with grades). Return a structured result:
     ```csharp
     public record CourseDeleteResult(bool Success, string Message);
     ```
4. **Data Protection Persistence**:
   * Always persist Data Protection keys to disk (`App_Data/DataProtection-Keys`) so authentication tickets and session tokens survive app restarts.
