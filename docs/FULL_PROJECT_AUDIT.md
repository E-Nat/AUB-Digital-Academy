# AUB Digital Academy — Full Project Audit Report

> **Audit Date:** 2026-11-26  
> **Environment Context:** Local Development (.NET 10.0 ASP.NET Core MVC + EF Core 10.0.5 + SQL Server 2022 + Docker Compose)  
> **Audit Scope:** Read-only full inspection of Controllers, Services, Models, Data/Migrations, Views, Static Assets (`wwwroot`), Configuration, Docker, and Live Build/Test Execution.  
> **Reference Baseline:** [`docs/project.md`](./project.md) and [`WORK_PROGRESS.md`](../WORK_PROGRESS.md)

---

## 1. Executive Summary & Overall Project Health

**Overall Health:** **Moderate / Solid Phase 1–4 Core, Critical Auth/Workflow/UI Gaps**

AUB Digital Academy has a substantial **Phase 1–4 foundation**: 4-role RBAC (`Admin`, `Teacher`, `Assistance`, `Student`), ASP.NET Core Identity, Telegram 2FA bot integration (`@AUBLinkBot` + `TelegramPollingService`), course/chapter/item CRUD, student enrollment with safety guards, lesson/book/quiz/assignment players, secure file uploads outside `wwwroot` with magic-byte validation, faculty grading with TA `CanGrade` enforcement, and Admin soft-delete/restore for attachments. The project builds with **0 errors** and **139/139** tests pass across the **9 CLI integration test runners** wired into `Program.cs`.

However, live source inspection uncovered **multiple critical security vulnerabilities, broken workflows, dead UI links, and Phase 5–6 feature gaps** that were previously undocumented or mischaracterized:

1. **Critical Security Findings (P0):**
   - `POST /api/Auth/login` (`Controllers/Api/AuthController.cs`) **completely bypasses mandatory Telegram 2FA**, account lockout checks, and `IsTelegramVerified`, and uses a **hardcoded fallback HS256 JWT secret** in source code.
   - `System.Net.Http.HttpClient.ITelegramService` logs **full Telegram Bot API request URLs (including the bot token)** at `Information` level in console/stdout logs because `appsettings.json` does not suppress `System.Net.Http.HttpClient`.
   - ~~Google OAuth (`AccountController.ExternalLoginCallback`) bypasses mandatory Telegram 2FA and sets `IsTelegramVerified = true` on new accounts.~~ *(Resolved 2026-11-27 in `TASK-P0-04`.)*
   - ~~Stored XSS vectors exist in `Views/Student/Lesson.cshtml` (`@Html.Raw(Model.ContentHtml)` without server-side sanitization) and `Views/Academic/Course.cshtml` (unescaped `innerHTML` concatenation for TA names/emails).~~ *(Resolved 2026-11-26 in `TASK-P0-03`.)*
   - ~~`SeedData.EnsureSeedUserAsync` runs unconditionally on every startup and resets seeded accounts back to weak hardcoded passwords (`admin123`, `teacher123`, `assistance123`, `student123`).~~ *(Resolved 2026-11-27 in `TASK-P0-04`.)*

2. **Broken Core Workflows & Logic Bugs (P1):**
   - Self-service **Password Reset** (`AccountController.ForgotPassword` / `ResetPassword` POST) is a non-functional stub that never resets passwords or sends tokens.
   - **Student Registration** (`AccountController.Register`) sends verification codes when `TelegramChatId` is null (falling back to global `Telegram:ChatId` or simulation logs) and redirects to `VerifyTelegram` instead of `LinkTelegram`.
   - **Broken Navigation Links (404 / 403):** Login "Activate Your Account" links to non-existent `/pages/authentication/activate-account.html`; `_Layout.cshtml` links to `/Profile` (empty `SettingsController`), `/Teacher/Submissions` (missing required `assignmentId`), `/Assistance/Dashboard` (for TA breadcrumbs), and redirects all roles' `Ctrl+K` global search to `/Admin/Users`; `TeacherService` generates `ManageUrl = "/Teacher/Courses/Details/{id}"` (404, actual route is `/Teacher/CourseDetails/{id}`); `Admin/Settings.cshtml` links to `/User/Profile` (404); Teachers have no UI link to `/Academic/Curriculum/{id}` from `Views/Teacher/CourseDetails.cshtml`.
   - **Progress & Grading Logic Bugs:** `GetStudentBookAsync` marks `Book` items complete in `CourseProgresses`, inflating lesson completion percentages (`>100%` clamped in `CurriculumService`); `GetStudentQuizPlayerAsync` and `SubmitQuizAsync` do not enforce `Quiz.IsPublished`; `TeacherService` excludes `SubmissionStatus.Late` from `PendingReviewsCount` and does not filter `IsLatest` in `GetTeacherGradesAsync`; quiz `TimeLimitMinutes` is client-side only.

3. **Missing Features (Phase 5 & Deferred Admin/Academic Modules):**
   - Category/Major/Semester CRUD, `SettingsController` Profile/ChangePassword, Video Watch Tracking (>=90% anti-seek + resume), Sequential Access & Multi-Prerequisites, Lesson Discussions, In-App Notification Center, Announcements, Login History, Real Reports/System Lookups, and Final Grades/Transcripts.

---

## 2. Feature Audit Summary

*(See [`docs/FEATURE_STATUS.md`](./FEATURE_STATUS.md) for the complete role-by-role feature matrix.)*

- **Admin:** Executive Dashboard (`AdminController.Index`), User Management (`UserController`), Course Management & TA Assignment (`AcademicController`), Enrollment Management (`EnrollmentController`), and Attachment Soft-Delete/Restore (`AdminController.Files`) are **implemented**. Academic lookup CRUD (`Category`, `Major`, `Semester`, `CourseItem`), `Reports`, `Settings`, `LoginHistory`, `Announcements`, and non-LMS placeholder tabs (`Schedule`, `Payments`, `Invoices`, `Payroll`) are **placeholders or missing**.
- **Teacher:** Dashboard, Courses, CourseDetails, Assignments, Submissions, Grading, Quizzes, Books (`TeacherController` + `TeacherService`), and Curriculum/Question authoring (`AcademicController` + `CurriculumService`) are **implemented on backend**, but **UI navigation is disconnected** (no link from `Teacher/CourseDetails` to `/Academic/Curriculum/{id}`, broken `ManageUrl` on Dashboard cards, broken `/Teacher/Submissions` sidebar link, missing Edit Question UI in `QuizQuestions.cshtml`).
- **Teaching Assistant (Assistance):** Shared Faculty Dashboard (`TeacherController`) with `CourseAssistant` scoping and `CanGrade` enforcement is **implemented**. Only Admin can assign/unassign TAs and toggle `CanGrade` (`AcademicController`). Breadcrumb in `_Layout.cshtml` generates broken `/Assistance/Dashboard`.
- **Student:** Dashboard, MyCourses, CourseOverview, Lesson Player, Book Reader, Quiz Engine (attempt limits, best score, exhaustion-gated answer reveal), Assignment Upload/Resubmission (<=5 files, <=10MB, PDF/ZIP/DOCX + URL + text), and Gradebook are **implemented** (with progress inflation and draft-quiz access bugs noted). Phase 5 features (>=90% video tracking, sequential access, discussions, notifications, announcements) are **not started**.

---

## 3. Security Audit

### 3.1 Authentication, 2FA, and JWT
| Severity | Finding | Location | Evidence / Impact |
|---|---|---|---|
| **Critical** | **API Login Bypasses Mandatory Telegram 2FA & Lockout** | [`AuthController.cs`](../Controllers/Api/AuthController.cs#L41-L93) | `POST /api/Auth/login` verifies password only, skips `IsLockedOutAsync` / `AccessFailedAsync`, ignores `IsTelegramVerified` / `TelegramChatId`, and mints a 7-day JWT. Contradicts `docs/project.md` Sec. 11 mandatory 2FA. |
| **Critical** | **Hardcoded Fallback JWT Secret in Source** | [`AuthController.cs`](../Controllers/Api/AuthController.cs#L181-L183), [`AuthController.cs`](../Controllers/Api/AuthController.cs#L220-L222) | Falls back to a hardcoded string literal when `Jwt:Secret` / `Jwt:Key` are unset (neither key exists in `appsettings.json` or `.env.example`). Anyone reading the repo can forge valid 7-day Admin JWTs for `/api/auth/me` and `/api/admin/me`. |
| **High** | **Telegram Bot Token Exposed in Application Logs** | [`Program.cs`](../Program.cs#L103), [`appsettings.json`](../appsettings.json#L16-L20), [`TelegramService.cs`](../Services/Implementations/TelegramService.cs#L131) | `AddHttpClient<ITelegramService, TelegramService>()` logs full `https://api.telegram.org/bot<TOKEN>/...` URLs via `System.Net.Http.HttpClient` at `Information` level every 20s during polling and on every message send. |
| **High (Resolved)** | ~~**Google OAuth Bypasses Mandatory Telegram 2FA**~~ | [`AccountController.cs`](../Controllers/AccountController.cs), [`AuthService.cs`](../Services/Implementations/AuthService.cs) | **Resolved (2026-11-27, `TASK-P0-04`):** `ExternalLoginCallback` now resolves via `UserManager.FindByLoginAsync`, enforces `IsActive`, lockout, `CanSignInAsync`, linked Telegram 2FA, replay protection, and `VerifyTelegram` OTP before issuing an Identity cookie. |
| **High** | **Self-Service Registration Verification Leakage** | [`AccountController.cs`](../Controllers/AccountController.cs#L224-L232), [`TelegramService.cs`](../Services/Implementations/TelegramService.cs#L104-L107) | Newly registered students have `TelegramChatId == null`. `Register` calls `SendVerificationCodeAsync` with `null` `customChatId` (falling back to shared `Telegram:ChatId` or logging the OTP in simulation mode) and redirects to `VerifyTelegram` instead of `LinkTelegram`. |
| **Medium** | **Unauthenticated Telegram Link Status Polling** | [`AccountController.cs`](../Controllers/AccountController.cs#L266-L275) | `CheckTelegramLinkStatus(string userId)` does not validate `AUB_PendingVerification` cookie/session, allowing unauthenticated callers who know a user GUID to query link status. |
| **Medium** | **Simulation Mode Logs Plaintext 2FA Codes** | [`TelegramService.cs`](../Services/Implementations/TelegramService.cs#L123-L127) | If `BotToken` or `ChatId` is empty, logs the 6-digit OTP at `Information` level without a `[DEV SIMULATION]` guard or environment check. |
| **Medium (Resolved)** | ~~**Weak Seed Passwords & Auto-Reset on Startup**~~ | [`SeedData.cs`](../Data/SeedData.cs) | **Resolved (2026-11-27, `TASK-P0-04`):** Initial local-only Development seed users are created strictly when the user store is empty in Development/`--test-*` runs (`CreateSeedUserIfMissingAsync`); existing passwords, active/lockout flags, profile fields, roles, and deletions are never overwritten. |
| **Low** | **Weak Password Complexity Policy** | [`Program.cs`](../Program.cs#L30-L40) | `RequiredLength = 6`, `RequireUppercase = false`, `RequireNonAlphanumeric = false`. Acceptable for local dev; should be strengthened before staging/prod. |

### 3.2 Authorization, RBAC, Ownership & IDOR Analysis
- **Recently Fixed `AcademicController` & `CurriculumService` IDOR (Verified):**
  - `AcademicController` has `[Authorize(Roles = "Admin,Teacher")]` at class level ([`AcademicController.cs:15`](../Controllers/AcademicController.cs#L15)).
  - `Course`, `CreateCourse`, `DeleteCourse`, `GetCourseAssistantsJson`, `AssignCourseAssistant`, `UnassignCourseAssistant`, and `ToggleAssistantCanGrade` are strictly decorated with `[Authorize(Roles = "Admin")]`.
  - `CanManageCourseAsync` (Admin or `course.TeacherId == currentUserId`) and `ResolveCourseIdFrom*Async` helpers enforce ownership across `GetCourseJson`, `EditCourse`, `Curriculum`, `GetCourseModulesJson`, `GetModuleJson`, `CreateModule`, `EditModule`, `DeleteModule`, `GetModuleLessonsJson`, `GetLessonJson`, `CreateLesson`, `EditLesson`, `DeleteLesson`, `GetQuizJson`, `CreateQuiz`, `EditQuiz`, `DeleteQuiz`, `GetBookJson`, `CreateBook`, `EditBook`, `DeleteBook`, `GetAssignmentJson`, `CreateAssignment`, `EditAssignment`, `DeleteAssignment`, `QuizQuestions`, `GetQuestionJson`, `AddQuestion`, `EditQuestion`, and `DeleteQuestion`.
  - `EditCourse` (L424-427) prevents non-Admin callers from reassigning `TeacherId`.
  - `CurriculumService` also validates `expectedCourseId` / `expectedChapterId` on updates and deletes, preventing cross-course item movement even if mismatched IDs are supplied.
  - **Remaining Gap in Test Verification:** No automated HTTP/controller tests exercise `AcademicController` cross-teacher `403 Forbidden` responses (existing CLI runners only test `TeacherService` / `CurriculumService` directly).
- **Seeded TA Permissions (Verified):**
  - `Program.cs:123-125` and `SeedData.cs:305-335` gate automatic `CS301` TA `CanGrade = true` seeding strictly to `Development` or `--test-*` CLI runners. Newly assigned TAs in `AcademicController.AssignCourseAssistant` (L2070) default to `CanGrade = false`.
- **Remaining Authorization Gaps:**
  - **Unpublished Quiz Access:** `CurriculumService.GetStudentQuizPlayerAsync` (`L1631`) and `SubmitQuizAsync` (`L1775`) do not check `quiz.IsPublished`, allowing enrolled students to access and submit draft quizzes via direct `courseItemId`.
  - **Admin Access on `StudentController` Sub-Pages:** `StudentController` permits `Admin` (`[Authorize(Roles = "Student,Admin")]`), and `Course`, `Learn`, `Lesson`, `CompleteLesson`, `SubmitQuiz`, `QuizResult` bypass enrollment for Admins. However, `Quiz` (`L618`), `Assignment` (`L786`), and `Book` (`L928`) delegate enrollment checks to `CurriculumService` without passing `isAdmin`, causing 404 for unenrolled Admins.
  - **Blind Lesson Completion:** `StudentController.CompleteLesson` (`L500-563`) accepts POST requests without verifying video watch duration (known Phase 5 gap).

### 3.3 XSS, CSRF, SQL Injection, and File Upload Security
   - **Stored XSS Vectors (Resolved 2026-11-26 — `TASK-P0-03`):**
     - [`Views/Student/Lesson.cshtml:135`](../Views/Student/Lesson.cshtml) now renders `@Html.Raw(safeContentHtml)` sanitized through `IHtmlSanitizationService` (`Ganss.Xss.HtmlSanitizer`), while `CurriculumService` also sanitizes `ContentHtml` at write and read boundaries to protect both new and legacy rows.
     - [`Views/Academic/Course.cshtml`](../Views/Academic/Course.cshtml) TA modal table rows now use safe DOM APIs (`document.createElement`, `textContent`, `replaceChildren`) instead of `innerHTML` concatenation.
     - `Book.FileUrl`, `Lesson.VideoUrl`, `Course.Thumbnail`, `User.Avatar`, and assignment external URLs are validated on write (`CurriculumService`, `CourseService`, `AcademicController`) and sanitized at render time across `Student/Book.cshtml`, `Teacher/Books.cshtml`, `Student/Assignment.cshtml`, `Teacher/Submissions.cshtml`, `Home/Index.cshtml`, `Admin/Index.cshtml`, `User/Details.cshtml`, and `User/Index.cshtml`.
     - **Remaining low-risk / out-of-scope note:** Unused legacy prototype scripts under `wwwroot/js/components/` (`course-modal.js`, `teacher-sidebar.js`, etc.) are not referenced by any Razor view and should be removed in `TASK-P4-02`.
- **CSRF Protection:** Cookie auth uses `SameSite=Lax`, `HttpOnly=true`. All mutating POST forms/endpoints verified use `[ValidateAntiForgeryToken]`. Note: `AddCors("AllowFrontend")` in `Program.cs:85-100` enables `AllowCredentials()` for localhost ports `5500`, `5000`, `5236` (acceptable for local dev; ensure restricted in production).
- **SQL Injection:** All service queries use parameterized EF Core LINQ. The only `ExecuteSqlRawAsync` call (`SeedData.cs:541`) executes a static constant DDL string with zero user input.
- **File Upload Security (`AssignmentStorageService.cs`):**
  - Stores uploads outside `wwwroot` at `App_Data/Uploads/Assignments` using GUID filenames (`Guid.NewGuid():N + ext`).
  - Enforces max 5 files/submission, max 10 MB/file, extension whitelist (`.pdf`, `.zip`, `.docx`), MIME type matching, and magic-byte validation (`%PDF`, `PK` ZIP header + OpenXML `[Content_Types].xml` + `word/` entry check for `.docx`, explicitly blocking `MZ` and `ELF` headers).
  - Canonical path traversal guard on both save and read.
  - **Minor Bugs:**
    1. In `CurriculumService.SubmitAssignmentAsync` (`L2372-2402`), files are saved to disk inside the loop before validating subsequent files in the same upload batch; if file #2 or #3 fails validation, earlier files remain orphaned on disk.
    2. `TeacherController.DownloadSubmission` (`L174-211`) serves legacy `submission.FileUrl` without checking `AssignmentAttachment.IsDeleted` (whereas `DownloadAttachment` at `L227` checks `IsDeleted`).

---

## 4. Database Audit

### 4.1 Schema, Relationships & EF Core Configuration
- **Provider & Migrations:** SQL Server 2022 via EF Core `10.0.5`. Four migrations exist under `Data/Migrations/` and `dotnet ef migrations list` confirms all 4 are applied with zero pending model drift:
  1. `20261124185257_InitialSqlServerCreate`
  2. `20261124205150_AddCourseAssistantJoinTable`
  3. `20261124222031_Phase4_StudentLearningAssessmentWorkflow`
  4. `20261125081514_Phase4_FinalRelationalAttachmentsAndQuizSecurity`
- **Unique Indexes & Constraints (`ApplicationDbContext.cs`):**
  - `User`: filtered unique indexes on `UniversityId` and `TelegramChatId` (`IS NOT NULL`).
  - `TelegramLinkToken`: unique `TokenHash`, composite `(UserId, IsUsed, ExpiresAt)`.
  - `Category.Name`, `Major.Code`, `Semester.Code`, `Course.Code`: unique indexes.
  - 1:1 `CourseItem` extensions (`Lesson`, `Book`, `Quiz`, `Assignment`): unique `CourseItemId` FKs with `Cascade` delete.
  - `Enrollment`: unique `(StudentId, CourseId)` with `Restrict` on `Course` delete.
  - `CourseProgress`: unique `(StudentId, CourseItemId)`.
  - `CourseAssistant`: unique `(CourseId, AssistantId)`.
  - `AssignmentSubmission`: composite `(AssignmentId, StudentId, IsLatest)`.
  - `AssignmentAttachment`: `Restrict` delete on `Submission`, `DeletedBy`, `RestoredBy` (preserves audit history).
- **Schema Gaps & Observations:**
  - `CourseProgress.StudentId` (`Models/CourseProgress.cs:11`) has no navigation property or FK constraint to `AspNetUsers` configured in `ApplicationDbContext.OnModelCreating`.
  - `docs/project.md` mentions `MajorCourse`, `Sections`, `Grades`/`tblGrades`, `LoginHistory`, `ItemTypes`/`ViewAnswerType`, and Phase 5 tables (`VideoProgress`, `LessonPrerequisite`, `LessonDiscussion`, `Notification`, `Announcement`). Currently `Course` links directly to `MajorId` and `SemesterId`, `Chapter` links directly to `CourseId`, `ItemType` is a C# enum, and the remaining tables do not exist yet.
  - `SeedData.EnsureBaselineSchemaAsync` (`SeedData.cs:501-542`) contains legacy DDL fallback SQL; harmless on up-to-date DBs.

---

## 5. Backend Architecture Audit

- **Layered Structure:** Generally clean separation (`Controllers/` -> `Services/Interfaces/` & `Services/Implementations/` -> `Data/ApplicationDbContext`), following `docs/CODING_PRINCIPLES.md` and `[ActivatorUtilitiesConstructor]` rules (`StudentController.cs:61`).
- **Controller Bloat / Coupling:**
  - `AcademicController.cs` is **2,133 lines** and mixes course management, module/lesson/quiz/book/assignment/question CRUD, TA management, and direct `_context` queries (`CanManageCourseAsync`, `PopulateDropdownsAsync`, TA assignment endpoints).
  - `AdminController.Index` (`L31-263`) and `AdminController.Files` (`L294-343`) query `_context` directly instead of using an `IAdminDashboardService` / `IFileManagementService`.
  - `AccountController.cs` (772 lines) contains direct `_context` queries for `TelegramVerificationRequests`.
  - `EnrollmentController.cs` directly queries `_context.CourseAssistants` and `_context.Courses`.
- **Service Layer Observations:**
  - `CurriculumService.cs` is **2,666 lines** (god service handling curriculum authoring, lesson player, quiz engine, assignment submission, book reader, and student gradebook).
  - `AcademicController` constructor (`L35`) and `StudentController` legacy constructors (`L35, L54`) instantiate `new CurriculumService(...)` without `IAssignmentStorageService` if DI is bypassed in unit tests.
  - `AcademicController` duplicates dropdown population logic in `PopulateDropdownsAsync` (`L1856-1910`) and `PopulateEditDropdownsAsync` (`L1913-1967`).
  - `Models/Enums/QuestionType.cs` defines `Essay`, which is unsupported in `CurriculumService` (`AddQuestionAsync`, `UpdateQuestionAsync`, `SubmitQuizAsync`).
- **Exception Handling & Validation:**
  - Global `/Home/Error` handler is only active outside `Development` (`Program.cs:222`).
  - `HomeController.Index` (`L51-59`) and `TeacherService.GetTeacherDashboardAsync` (`L207-210`) catch `Exception` broadly and silently fall back to empty data (hiding real DB faults).
  - ViewModels use DataAnnotations consistently; dual AJAX/form responses (`X-Requested-With == "XMLHttpRequest"`) follow `docs/ROUTING_CONVENTIONS.md`.

---

## 6. Frontend & UI/UX Audit

### 6.1 Broken Links, Dead Pages & UI-Backend Disconnects
| Issue | Location | Impact |
|---|---|---|
| Dead "Activate Your Account" link | [`Views/Account/Login.cshtml:164`](../Views/Account/Login.cshtml#L164) | Links to non-existent `/pages/authentication/activate-account.html` (404) instead of `/Account/Register`. |
| Broken Teacher Dashboard "Manage Course" URL | [`TeacherService.cs:201,251`](../Services/Implementations/TeacherService.cs#L201), [`Views/Teacher/Dashboard.cshtml:239`](../Views/Teacher/Dashboard.cshtml#L239) | Sets `ManageUrl = "/Teacher/Courses/Details/{id}"`, which 404s; `TeacherController.CourseDetails` responds at `/Teacher/CourseDetails/{id}`. |
| Broken `/Profile` Sidebar & Dropdown Links | [`Views/Shared/_Layout.cshtml:127,158,255`](../Views/Shared/_Layout.cshtml#L127), [`Controllers/SettingsController.cs`](../Controllers/SettingsController.cs) | `SettingsController` is empty; `Views/Settings/Profile.cshtml` and `ChangePassword.cshtml` are 1-line placeholders. Clicking Settings/Profile 404s for Teachers, TAs, and Students. |
| Broken `/User/Profile` Link | [`Views/Admin/Settings.cshtml:26`](../Views/Admin/Settings.cshtml#L26) | Links to `/User/Profile`, which does not exist in `UserController` (404). |
| Broken `/Teacher/Submissions` Sidebar Link | [`Views/Shared/_Layout.cshtml:114`](../Views/Shared/_Layout.cshtml#L114) | `TeacherController.Submissions(int assignmentId)` requires `assignmentId`; clicking sidebar link triggers `"Assignment not found."` error redirect. Sidebar also lacks links to `Assignments`, `Quizzes`, and `Books`. |
| Broken TA Breadcrumb Link | [`Views/Shared/_Layout.cshtml:199`](../Views/Shared/_Layout.cshtml#L199) | Uses `/@userRole/Dashboard`, producing `/Assistance/Dashboard` (404) for TAs instead of `/Teacher/Dashboard`. |
| Topbar `Ctrl+K` Search Redirects All Roles to Admin | [`wwwroot/js/site.js:124`](../wwwroot/js/site.js#L124) | Hardcodes `window.location.href = '/Admin/Users?search=...'`, causing 403 Access Denied for Teachers, TAs, and Students. |
| Teachers Lack UI Path to Curriculum Authoring | [`Views/Teacher/CourseDetails.cshtml:23-30`](../Views/Teacher/CourseDetails.cshtml#L23-L30), [`Views/Academic/Curriculum.cshtml:104,155`](../Views/Academic/Curriculum.cshtml#L104) | No button links Teachers from `Teacher/CourseDetails` to `/Academic/Curriculum/{id}`; `Curriculum.cshtml` "Back to Courses" links to Admin-only `/Academic/Course` (403 for Teachers). |
| Missing Edit Question UI | [`Views/Academic/QuizQuestions.cshtml:93-104`](../Views/Academic/QuizQuestions.cshtml#L93-L104) | Backend has `GetQuestionJson` and `EditQuestion`, but UI only renders `Add` and `Delete` modals. |
| Hardcoded Term on Student Dashboard | [`Views/Student/Dashboard.cshtml:70`](../Views/Student/Dashboard.cshtml#L70) | Displays hardcoded `"Spring 2026"` instead of querying the current `Semester`. |
| Admin Placeholder Views | `Views/Admin/{Schedule,Payments,Invoices,Payroll,Reports,Settings}.cshtml`, `Views/Academic/{Category,Major,Semester,CourseItem}.cshtml` | Static cards or 1-line stub files with no backend backing. |

### 6.2 UX, Validation, Accessibility & Responsive Notes
- **Client-Side Quiz Timer Only:** `Views/Student/Quiz.cshtml` (`L361-392`) runs a JS countdown that resets on page refresh and is not enforced on `SubmitQuizAsync`.
- **Dark Mode Support:** `_Layout.cshtml` initializes `data-bs-theme` from `localStorage`/system preference without FOUC flash; however, several Teacher/Student/Admin cards hardcode `bg-white` and `text-dark` classes, reducing dark-mode contrast consistency.
- **Accessibility:** Auth and Welcome layouts include semantic landmarks, `aria-label`s, `aria-live` alerts, and skip links (`_WelcomeLayout.cshtml:27`). Some icon-only action buttons in tables (`Course.cshtml`, `Enrollment/CourseStudents.cshtml`) rely on `title` without `aria-label`.

---

## 7. Testing & Code Quality Audit

### 7.1 Build & Test Verification (Executed 2026-11-26)
- **`dotnet build --no-restore`:** Succeeded (**0 Errors, 2 Warnings**):
  - `NU1902` (Moderate) and `NU1903` (High) on NuGet package `System.Security.Cryptography.Pkcs` `9.0.4`.
- **`dotnet test --no-build`:** No test projects found (no xUnit/NUnit/MSTest project exists).
- **Wired CLI Integration Test Suites (`Program.cs` -> `Data/*TestRunner.cs`):** All 9 wired suites executed live against local SQL Server 2022 (`aub_sqlserver`) and **139/139 tests passed**:
  - `--test-auth`: **12/12 PASSED**
  - `--test-assistance-role`: **14/14 PASSED**
  - `--test-teacher-portal`: **14/14 PASSED**
  - `--test-user-management`: **14/14 PASSED**
  - `--test-registration-telegram`: **14/14 PASSED**
  - `--test-course-integration`: **16/16 PASSED**
  - `--test-enrollment-ui`: **13/13 PASSED**
  - `--test-quiz`: **16/16 PASSED**
  - `--test-phase4`: **26/26 PASSED**

### 7.2 Testing Gaps & Debt
1. **10 Unwired Test Runners in `Data/`:** `CourseCreationTestRunner`, `CourseDeleteTestRunner`, `CurriculumEndToEndIntegrationTestRunner`, `EnrollmentBackendTestRunner`, `LessonCrudTestRunner`, `ModuleCrudTestRunner`, `ModuleUiIntegrationTestRunner`, `StudentFlowIntegrationTestRunner`, `StudentPlayerIntegrationTestRunner`, and `StudentProgressIntegrationTestRunner` are compiled into `Program` assembly but not invoked by `Program.cs`.
2. **Zero Automated HTTP / Controller Integration Tests:** All 19 runners test EF Core / Services directly. Controller `[Authorize]` attributes, routing, anti-forgery, `Api/AuthController`, and HTTP 403 IDOR rejections in `AcademicController` have no automated test coverage.
3. **Production Assembly Bloat:** Test runners live inside `AUBDigitalAcademy.csproj` instead of a dedicated `tests/` project.

---

## 8. External Integrations Audit

- **Telegram Bot (`@AUBLinkBot`):** `TelegramService` + `TelegramPollingService` (`BackgroundService` long polling with `timeout=20s`, `HttpClient` timeout `60s`, SHA-256 hashed 15-minute `TelegramLinkToken` deep links `/start link_<token>` and `/link <token>`, `/status`, `/help`, `/about`). Operational, except for the plaintext bot-token HTTP logging bug and `Register` null-ChatId fallback.
- **SQL Server 2022 & Docker:** `docker-compose.yml` defines healthy `aub_sqlserver` (`mcr.microsoft.com/mssql/server:2022-latest`, persistent volume `aub_sqlserver_data`) and `aub_digital_academy` (`Dockerfile` multi-stage .NET 10.0 build, Data Protection key volume `dataprotection_keys`, port `5236:8080`). Local `.env` loader (`Program.cs:257-301`) preserves Docker/OS env vars.
- **Google OAuth:** Optional in `Program.cs:70-87` (`SaveTokens = false`, `OnRemoteFailure` safe redirect); `GoogleAuthService` / `IGoogleAuthService` are empty stubs; `ExternalLoginCallback` now enforces `UserManager.FindByLoginAsync`, active/lockout checks, replay protection, and mandatory Telegram 2FA (`TASK-P0-04`).
- **Email / SMTP:** Not configured (`Api/AuthController.ForgotPassword` checks `Smtp:Host`, which is absent in `appsettings.json` and `.env.example`).

---

## 9. Performance & Technical Debt

- **N+1 Query Patterns:**
  - `EnrollmentController.Index` (`L105-123`) and `EnrollmentController.CourseStudents` (`L287-302`) execute `HasStudentLearningActivityAsync` (3 separate DB queries) inside a `foreach` loop per enrollment row.
- **Over-Fetching in Memory:**
  - `AdminController.Index` (`L74-109`) loads entire `Majors -> Courses -> Enrollments` and `Semesters -> Courses -> Enrollments` entity graphs into memory solely to compute counts.
  - `UserService.GetUsersAsync` (`L65-70`) loads full `TaughtCourses`, `AssistedCourses`, and `Enrollments` collections for all users just to read `.Count`.
  - `CurriculumService.GetStudentAssignmentAsync` and `GetStudentBookAsync` call `BuildStudentCurriculumNavigationAsync`, re-querying the entire course graph after already loading the item.
- **Unbounded Queries / Missing Pagination:**
  - `AdminController.Files`, `UserController.Index`, `AcademicController.Course`, and `TeacherController.Grades` load all rows without server-side pagination.
- **Dead / Unused Files:**
  - C#: `Controllers/SettingsController.cs`, `Data/DbInitializer.cs`, `Models/Role.cs`, `Services/Interfaces/IGoogleAuthService.cs`, `Services/Implementations/GoogleAuthService.cs`.
  - Views: `Views/Shared/_Navbar.cshtml`, `_AdminLayout.cshtml`, `_StudentLayout.cshtml`, `Views/Settings/{Profile,ChangePassword}.cshtml`, `Views/Academic/{Category,Major,Semester,CourseItem}.cshtml`.
  - Assets: `wwwroot/css/login.css`, `wwwroot/js/{chart,validation}.js`, `wwwroot/js/components/{carousel,course-modal,hero-3d,sidebar,student-layout,teacher-sidebar}.js`.
- **Technical Debt Markers:** `Student/CourseOverview.cshtml:59` (`Content Coming Soon` badge for empty courses); no `TODO`/`FIXME` comments left in C# code, though `Book` auto-completion inflates progress (`CurriculumService.cs:2487` & `2153` clamp).
