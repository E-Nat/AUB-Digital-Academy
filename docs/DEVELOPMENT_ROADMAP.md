# AUB Digital Academy — Prioritized Development Roadmap

> **Generated:** 2026-11-26  
> **Source Audit:** [`docs/FULL_PROJECT_AUDIT.md`](./FULL_PROJECT_AUDIT.md)  
> **Product Spec:** [`docs/project.md`](./project.md)

---

## Priority Classification

- **P0** — Critical bugs and security vulnerabilities
- **P1** — Core functionality and broken workflows
- **P2** — Missing features and incomplete modules
- **P3** — UI/UX improvements and performance optimization
- **P4** — Refactoring and technical debt

---

## Phase 1: P0 — Critical Security & Integrity Fixes

### `TASK-P0-01`: Fix API JWT Auth 2FA Bypass & Remove Hardcoded JWT Secret — **[COMPLETED 2026-11-26]**
- **Description:** Enforce mandatory Telegram 2FA (or disable/replace `/api/Auth/login` with a 2-step challenge) and remove the hardcoded fallback HS256 JWT secret in `Controllers/Api/AuthController.cs`. Add lockout checks (`IsLockedOutAsync` / `AccessFailedAsync`), `nbf`/`iss`/`aud` validation, and fail-fast configuration validation if JWT auth is enabled without a secret.
- **Why it is needed:** `POST /api/Auth/login` previously bypassed Telegram 2FA and account lockout completely, and any attacker with source access could forge Admin JWTs using the hardcoded secret literal.
- **Affected files:** `Services/Interfaces/IAuthService.cs`, `Services/Implementations/AuthService.cs`, `Controllers/Api/AuthController.cs`, `Controllers/AccountController.cs`, `Services/Implementations/TelegramService.cs`, `Data/ApiAuth2FaIntegrationTestRunner.cs`, `appsettings.json`, `.env.example`, `docker-compose.yml`, `Program.cs`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** M
- **Acceptance criteria:**
  - API login rejects unverified/unlinked users and does not issue a final JWT without Telegram 2FA verification. *(Verified)*
  - Lockout is enforced on failed API logins. *(Verified)*
  - No hardcoded JWT secret fallback exists in source code; missing `Jwt:Secret` fails safely. *(Verified)*
- **Required tests:** `--test-api-auth` (12/12 passed), `--test-auth` (12/12 passed), `--test-registration-telegram` (14/14 passed). *(All executed and passed on 2026-11-26; local devs must set `JWT_SECRET` >= 32 chars in `.env` or `dotnet user-secrets` to issue API tokens.)*

### `TASK-P0-02`: Redact Telegram Bot Token from HTTP Client Logs & Harden OTP Logging
- **Description:** Suppress `System.Net.Http.HttpClient` `Information` request-URI logging (or configure a custom redacting `HttpMessageHandler`/log filter) so the Telegram Bot token is never written to stdout/logs. Restrict `TelegramService.SendVerificationCodeAsync` plaintext OTP simulation logging strictly to `IHostEnvironment.IsDevelopment()`.
- **Why it is needed:** `AddHttpClient<ITelegramService, TelegramService>()` currently logs the full `https://api.telegram.org/bot<TOKEN>/...` URL every 20 seconds during polling and on every message send.
- **Affected files:** `Program.cs`, `appsettings.json`, `Services/Implementations/TelegramService.cs`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** S
- **Acceptance criteria:**
  - Startup and polling logs contain zero occurrences of the Telegram Bot token URL segment.
  - Simulation OTP logging only occurs when `IsDevelopment()` is true.
- **Required tests:** Log-output verification test asserting `bot` token string never appears in logger output during `GetUpdatesAsync` and `SendVerificationCodeAsync`.

### `TASK-P0-03`: Sanitize HTML Lesson Content & Fix Frontend Stored XSS Vectors
- **Description:** Sanitize `Lesson.ContentHtml` server-side (e.g., using `HtmlSanitizer`) before storing/rendering via `@Html.Raw` in `Views/Student/Lesson.cshtml`. Replace unescaped `innerHTML` concatenation in `Views/Academic/Course.cshtml` (TA modal) with `textContent` / DOM element creation. Validate `http://`/`https://` (or relative `/`) protocols on `Book.FileUrl` and `Course.Thumbnail`.
- **Why it is needed:** `@Html.Raw(Model.ContentHtml)` and `innerHTML` string concatenation expose students and admins to Stored XSS if malicious markup is entered.
- **Affected files:** `AUBDigitalAcademy.csproj`, `Services/Implementations/CurriculumService.cs`, `Views/Student/Lesson.cshtml`, `Views/Academic/Course.cshtml`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** M
- **Acceptance criteria:**
  - `<script>`, `onerror=`, and `javascript:` payloads in lesson HTML, TA names/emails, and book URLs are stripped or rejected.
  - Safe formatting tags (`<p>`, `<ul>`, `<b>`, `<a>` with safe href) still render properly in `Lesson.cshtml`.
- **Required tests:** Unit tests verifying `<script>` and event-handler attributes are stripped from `ContentHtml`, and `javascript:` URLs are rejected for Books and Assignments.

### `TASK-P0-04`: Enforce Telegram 2FA on Google OAuth & Gate `SeedData` Password Resets
- **Description:** Update `AccountController.ExternalLoginCallback` to require Telegram linking/2FA instead of setting `IsTelegramVerified = true` and signing in directly; add null guard on email claim. Gate `SeedData.EnsureSeedUserAsync` so existing user passwords/lockouts/verification flags are never overwritten on startup outside initial empty-DB dev seeding.
- **Why it is needed:** Google OAuth bypasses mandatory 2FA, and `SeedData` silently resets Admin/Teacher/TA/Student passwords back to weak defaults (`admin123`, etc.) on every app restart.
- **Affected files:** `Controllers/AccountController.cs`, `Data/SeedData.cs`, `Program.cs`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** M
- **Acceptance criteria:**
  - Google OAuth users without verified Telegram must complete Telegram pairing/2FA before receiving an authenticated session cookie.
  - Changing the Admin or Teacher password persists across application restarts.
- **Required tests:** Integration tests verifying password changes survive `SeedData.InitializeAsync` re-runs, and OAuth callback enforces Telegram verification.

### `TASK-P0-05`: Fix `Register` Telegram Flow & Secure `CheckTelegramLinkStatus`
- **Description:** Fix `AccountController.Register` so newly registered students without `TelegramChatId` are routed through `LinkTelegram` (personal pairing) rather than falling back to shared `Telegram:ChatId` via `VerifyTelegram`. Enforce `ValidatePendingVerificationSession(userId)` on `CheckTelegramLinkStatus`.
- **Why it is needed:** Currently, new students' private registration OTPs can be sent to the global `Telegram:ChatId` group/channel, and `CheckTelegramLinkStatus` lacks session ownership validation.
- **Affected files:** `Controllers/AccountController.cs`, `Services/Implementations/TelegramService.cs`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** S
- **Acceptance criteria:**
  - New students registering with Student ID + Name + Password are directed to `LinkTelegram`, link `@AUBLinkBot`, and then verify via `VerifyTelegram` on their own `ChatId`.
  - `CheckTelegramLinkStatus` returns `401`/`{ linked: false }` without a valid `AUB_PendingVerification` cookie for that `userId`.
- **Required tests:** Registration + Telegram link integration test and negative test for unauthenticated `CheckTelegramLinkStatus`.

### `TASK-P0-06`: Upgrade NuGet `System.Security.Cryptography.Pkcs` to Resolve NU1902/NU1903
- **Description:** Identify the transitive dependency pulling `System.Security.Cryptography.Pkcs` `9.0.4` and update packages or add a safe direct version constraint to eliminate `NU1902` (moderate) and `NU1903` (high) security advisories.
- **Why it is needed:** `dotnet build` reports two known security vulnerability warnings (`GHSA-2fmv-rv2c-59x7`, `GHSA-846f-f764-76mx`).
- **Affected files:** `AUBDigitalAcademy.csproj`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** S
- **Acceptance criteria:** `dotnet build` completes with 0 errors and 0 `NU1902`/`NU1903` warnings.
- **Required tests:** `dotnet build` and `dotnet list package --vulnerable`.

---

## Phase 2: P1 — Core Functionality & Broken Workflows

### `TASK-P1-01`: Fix All Broken Navigation Links & Dead Routes Across Portals
- **Description:**
  1. Fix `Views/Account/Login.cshtml:164` link from `/pages/authentication/activate-account.html` to `/Account/Register`.
  2. Fix `TeacherService.cs` `ManageUrl` (`L201, L251`) to `/Teacher/CourseDetails/{c.Id}` (or add `[Route("Teacher/Courses/Details/{id:int}")]` alias on `TeacherController.CourseDetails`).
  3. Fix `_Layout.cshtml:114` Teacher sidebar link from `/Teacher/Submissions` to `/Teacher/Assignments` (and add links to `Quizzes` and `Books`).
  4. Fix `_Layout.cshtml:199` breadcrumb to use `/@dashboardArea/Dashboard` so TAs don't hit `/Assistance/Dashboard` (404).
  5. Fix `wwwroot/js/site.js:124` `Ctrl+K` global search so it routes per role or hides for non-Admins instead of redirecting everyone to `/Admin/Users`.
  6. Add a "Manage Curriculum" button in `Views/Teacher/CourseDetails.cshtml` linking to `/Academic/Curriculum/@Model.Id`, and fix `Views/Academic/Curriculum.cshtml` "Back to Courses" link to route Teachers to `/Teacher/Courses` and Admins to `/Academic/Course`.
- **Why it is needed:** Multiple primary navigation paths currently return 404 or 403 or trap Teachers away from curriculum authoring.
- **Affected files:** `Views/Account/Login.cshtml`, `Services/Implementations/TeacherService.cs`, `Controllers/TeacherController.cs`, `Views/Shared/_Layout.cshtml`, `wwwroot/js/site.js`, `Views/Teacher/CourseDetails.cshtml`, `Views/Academic/Curriculum.cshtml`
- **Dependencies:** None
- **Priority:** P1
- **Estimated complexity:** M
- **Acceptance criteria:** Every sidebar item, breadcrumb, card button, and auth action link resolves to HTTP 200 for its intended role.
- **Required tests:** Automated HTTP route smoke test across all 4 roles verifying 200 OK (and 403 where restricted).

### `TASK-P1-02`: Implement `SettingsController` (Profile & Change Password)
- **Description:** Implement `Profile` (GET/POST) and `ChangePassword` (GET/POST) in `Controllers/SettingsController.cs` (supporting `/Profile`, `/Settings/Profile`, `/Settings/ChangePassword`) and build the corresponding Razor views (`Views/Settings/Profile.cshtml`, `Views/Settings/ChangePassword.cshtml`). Fix `Views/Admin/Settings.cshtml:26` `/User/Profile` link.
- **Why it is needed:** `/Profile` is linked in `_Layout.cshtml` for Teachers, TAs, and Students, and `/User/Profile` is linked in `Admin/Settings.cshtml`, all currently returning 404.
- **Affected files:** `Controllers/SettingsController.cs`, `Views/Settings/Profile.cshtml`, `Views/Settings/ChangePassword.cshtml`, `Views/Admin/Settings.cshtml`, `Models/ViewModels/`
- **Dependencies:** None
- **Priority:** P1
- **Estimated complexity:** M
- **Acceptance criteria:** Authenticated users in any role can view/update allowed profile fields (e.g., `FullName`, Telegram link status) and change their password with current-password verification.
- **Required tests:** Integration tests for profile update, invalid current password rejection, and valid password change.

### `TASK-P1-03`: Implement Self-Service Password Reset via Telegram 2FA
- **Description:** Implement `AccountController.ForgotPassword` and `ResetPassword` (POST) using ASP.NET Identity reset tokens + verified Telegram 2FA code delivery (since SMTP is unconfigured and Telegram is the verified 2FA channel), or wire SMTP when configured. Ensure `IsLockedOutAsync` / `IsActive` checks and single-use code rules apply.
- **Why it is needed:** Currently `AccountController.ResetPassword` POST is an empty stub that redirects to confirmation without changing the password.
- **Affected files:** `Controllers/AccountController.cs`, `Views/Account/ForgotPassword.cshtml`, `Views/Account/ResetPassword.cshtml`, `Models/ViewModels/ResetPasswordViewModel.cs`
- **Dependencies:** `TASK-P0-05`
- **Priority:** P1
- **Estimated complexity:** M
- **Acceptance criteria:** Only users with a verified `TelegramChatId` (or configured SMTP email) can complete a password reset, and the new password works on subsequent login.
- **Required tests:** End-to-end password reset integration test (happy path + expired/invalid code rejection).

### `TASK-P1-04`: Fix Progress Calculation Inflation, Draft Quiz Exposure & Teacher Grading/KPI Bugs
- **Description:**
  1. Filter `CourseProgresses` counts in `CurriculumService` (`GetStudentCoursesProgressAsync`, `GetStudentQuizPlayerAsync`, `BuildStudentCurriculumNavigationAsync`, `AdminController.Index`) by `CourseItem.Type == ItemType.Lesson` (or align numerator and denominator) so viewing `Book` items never inflates lesson completion above 100%.
  2. Enforce `quiz.IsPublished` in `GetStudentQuizPlayerAsync` and `SubmitQuizAsync`.
  3. Pass `isAdmin` bypass into `CurriculumService.GetStudentQuizPlayerAsync`, `GetStudentAssignmentAsync`, and `GetStudentBookAsync` so Admins viewing `StudentController` routes don't get 404.
  4. Update `TeacherService.GetTeacherDashboardAsync` and `GetTeacherAssignmentsAsync` to include `IsLatest && (Status == Submitted || Status == Late)` in `PendingReviewsCount`.
  5. Update `TeacherService.GetTeacherGradesAsync` to filter graded assignments by `s.IsLatest` and show each student's official best score (`Max(Score)`) for quizzes.
  6. Clean up partially saved files in `CurriculumService.SubmitAssignmentAsync` if a later file in the same batch fails validation, and check `IsDeleted` in `TeacherController.DownloadSubmission`.
- **Why it is needed:** Fixes multiple verified data-accuracy bugs affecting student progress, quiz security, and faculty grading views.
- **Affected files:** `Services/Implementations/CurriculumService.cs`, `Services/Implementations/TeacherService.cs`, `Controllers/StudentController.cs`, `Controllers/TeacherController.cs`, `Controllers/AdminController.cs`
- **Dependencies:** None
- **Priority:** P1
- **Estimated complexity:** M
- **Acceptance criteria:** Book reads never push lesson progress >100%; unpublished quizzes return 404/403 to students; Late submissions appear in Teacher pending reviews; stale superseded assignment grades are hidden from Gradebook.
- **Required tests:** Regression tests for Book vs Lesson progress math, draft quiz rejection, Late submission KPI counts, and multi-attempt assignment gradebook display.

### `TASK-P1-05`: Wire Edit Question UI & Enforce Server-Side Quiz Time Limit
- **Description:** Add Edit Question modal and button in `Views/Academic/QuizQuestions.cshtml` wired to `AcademicController.GetQuestionJson` and `EditQuestion`. Add server-side quiz attempt start timestamp (`StartedAt` on `QuizSubmission` or session/token) so `TimeLimitMinutes` cannot be bypassed by refreshing the browser. Also clarify or remove unsupported `QuestionType.Essay` until essay grading is designed.
- **Why it is needed:** Edit Question backend endpoints have no UI, and the quiz timer currently resets on browser refresh.
- **Affected files:** `Views/Academic/QuizQuestions.cshtml`, `Models/QuizSubmission.cs`, `Services/Implementations/CurriculumService.cs`, `Controllers/StudentController.cs`
- **Dependencies:** None
- **Priority:** P1
- **Estimated complexity:** M
- **Acceptance criteria:** Faculty can edit existing quiz questions/options in UI; server rejects or auto-closes submissions beyond `TimeLimitMinutes` (+ reasonable grace period).
- **Required tests:** Quiz edit UI/service test and server-side timer expiration test.

---

## Phase 3: P2 — Missing Features & Incomplete Modules

### `TASK-P2-01`: Academic Lookup CRUD (Categories, Majors, Semesters)
- **Description:** Implement full CRUD endpoints in `AcademicController` (Admin-only) and replace the 1-line placeholder views (`Views/Academic/Category.cshtml`, `Major.cshtml`, `Semester.cshtml`) with functional management views, plus safe-delete checks when courses reference a Major or Semester.
- **Why it is needed:** Required by `docs/project.md` Sec. 3.2–3.3; currently only seeded data exists with no UI to add or edit terms/majors/categories.
- **Affected files:** `Controllers/AcademicController.cs`, `Services/`, `Views/Academic/Category.cshtml`, `Views/Academic/Major.cshtml`, `Views/Academic/Semester.cshtml`, `Views/Shared/_Sidebar.cshtml`
- **Dependencies:** None
- **Priority:** P2
- **Estimated complexity:** M
- **Acceptance criteria:** Admin can list, create, edit, set current semester, and safely delete unused Categories, Majors, and Semesters; Teacher/TA/Student receive 403.
- **Required tests:** Integration tests for CRUD, unique code validation, and delete protection when courses depend on a Major/Semester.

### `TASK-P2-02`: Phase 5A — Video Watch Tracking (>=90% Anti-Seek & Resume)
- **Description:** Add `VideoLessonProgress` entity (tracking `QualifyingWatchedSeconds`, `WatchedDurationSeconds`, `LastPositionSeconds`, `VideoDurationSeconds`, watched intervals/segments, `IsCompleted`, `LastActivityAt`, `CompletedAt`) and migration. Integrate YouTube IFrame API (`enablejsapi=1` + `YT.Player`) and HTML5 `<video>` listeners (`timeupdate`, `seeking`, `seeked`, `ended`) in `Views/Student/Lesson.cshtml` with debounced playback heartbeat API. Gate `CompleteLesson` so video lessons require >=90% qualifying watched playback (seeking to 90% does not qualify) and support resume.
- **Why it is needed:** Core Phase 5 requirement in `docs/project.md` Sec. 8.1.
- **Affected files:** `Models/`, `Data/ApplicationDbContext.cs`, `Data/Migrations/`, `Services/Implementations/CurriculumService.cs`, `Controllers/StudentController.cs`, `Views/Student/Lesson.cshtml`
- **Dependencies:** `TASK-P1-04`
- **Priority:** P2
- **Estimated complexity:** L
- **Acceptance criteria:** Seeking past unwatched segments does not mark >=90% watched; continuous playback reaching >=90% marks video lesson complete; reopening lesson resumes from `LastPositionSeconds`.
- **Required tests:** Unit/integration tests for anti-seek interval merging, >=90% threshold boundary, resume position persistence, and rejection of premature `CompleteLesson` calls.

### `TASK-P2-03`: Phase 5A — Course Access Modes (`Free` vs `Sequential`) & Multi-Prerequisites
- **Description:** Add `CourseAccessMode` (`Free = 0`, `Sequential = 1`) on `Course` and `CourseItemPrerequisite` join table (`CourseItemId`, `RequiredCourseItemId`) supporting multiple prerequisites per item. Enforce prerequisite completion server-side across `StudentController` (`Learn`, `Lesson`, `Book`, `Quiz`, `Assignment`, `SubmitQuiz`, `SubmitAssignment`). Switching access mode preserves existing progress.
- **Why it is needed:** Core Phase 5 requirement in `docs/project.md` Sec. 8.2.
- **Open Product Decision Required:** Owner must confirm what constitutes "completion" for non-video items (`Book`, `Quiz`, `Assignment`) when used as prerequisites in Sequential Access.
- **Affected files:** `Models/Course.cs`, `Models/CourseItemPrerequisite.cs`, `Data/`, `Services/`, `Controllers/AcademicController.cs`, `Controllers/StudentController.cs`, `Views/Academic/Curriculum.cshtml`, `Views/Student/*`
- **Dependencies:** `TASK-P2-02`
- **Priority:** P2
- **Estimated complexity:** L
- **Acceptance criteria:** Locked items return 403/locked state on direct URL/API access until all configured prerequisites are met; switching between `Free` and `Sequential` retains all student progress.
- **Required tests:** Integration tests for single and multi-prerequisite unlocking, direct URL bypass prevention, and mode-toggle progress preservation.

### `TASK-P2-04`: Phase 5A — Student & Faculty Video Progress Dashboards
- **Description:** Update Student Dashboard and Faculty Roster (`Teacher/CourseDetails` + Admin course view) to display course video progress strictly as `Completed video lessons / Total video lessons`, `LastActivityAt`, and per-student lesson-level video drilldown (Admin: all courses; Teacher: own courses; TA: assigned courses; Student: own progress only). Replace hardcoded `"Spring 2026"` on `Views/Student/Dashboard.cshtml` with real current semester data.
- **Why it is needed:** Required by `docs/project.md` Sec. 6.3, 8.3–8.4.
- **Affected files:** `Services/Implementations/CurriculumService.cs`, `Services/Implementations/TeacherService.cs`, `Controllers/StudentController.cs`, `Controllers/TeacherController.cs`, `Views/Student/Dashboard.cshtml`, `Views/Teacher/CourseDetails.cshtml`
- **Dependencies:** `TASK-P2-02`
- **Priority:** P2
- **Estimated complexity:** M
- **Acceptance criteria:** Faculty roster displays student identity, completed video count, progress percentage, last activity, and lesson detail scoped strictly by role/ownership.
- **Required tests:** Scope isolation tests (Teacher A cannot view Teacher B's roster progress; TA cannot view unassigned course progress).

### `TASK-P2-05`: Phase 5B — Lesson Discussions & Moderation
- **Description:** Add `LessonDiscussionThread` and `LessonDiscussionReply` entities (`IsPinned`, `IsAnswered`, `IsDeleted`/moderation fields) and `IDiscussionService`. Render Q&A threads under video lessons (`Views/Student/Lesson.cshtml`) and faculty moderation controls for Teachers and assigned TAs (reply, pin helpful answers, mark answered, delete inappropriate comments).
- **Why it is needed:** Required by `docs/project.md` Sec. 9.
- **Open Product Decision Required:** Owner must confirm whether students may reply to *any* student's thread or only to *their own* threads (plus faculty replies).
- **Affected files:** `Models/`, `Data/`, `Services/`, `Controllers/StudentController.cs`, `Controllers/TeacherController.cs`, `Views/Student/Lesson.cshtml`
- **Dependencies:** Owner peer-reply policy confirmation
- **Priority:** P2
- **Estimated complexity:** L
- **Acceptance criteria:** Enrolled students can post questions; Teachers and assigned TAs can reply, pin, mark answered, and moderate; unauthorized users get 403.
- **Required tests:** Role/ownership authorization tests for posting, pinning, marking answered, and moderation.

### `TASK-P2-06`: Phase 5C — In-App Notification Center & Announcements
- **Description:** Add `Announcement` (`Scope`: Global vs Course, `CourseId?`, `CreatedById`, `PublishedAt`) and `Notification` (`UserId`, `Type`, `Title`, `Message`, `TargetUrl`, `IsRead`, `CreatedAt`) + `INotificationService`. Wire notifications for discussion replies, new lessons, announcements, released grades, and milestones (never playback heartbeats). Replace static empty dropdown in `_Layout.cshtml` with live unread count and feed.
- **Why it is needed:** Required by `docs/project.md` Sec. 10.1–10.2.
- **Affected files:** `Models/`, `Data/`, `Services/`, `Controllers/`, `Views/Shared/_Layout.cshtml`
- **Dependencies:** `TASK-P2-05` (for discussion reply triggers)
- **Priority:** P2
- **Estimated complexity:** L
- **Acceptance criteria:** Admin can publish global announcements; Teacher can publish course-scoped announcements only for owned courses; Students see relevant notifications and mark them read.
- **Required tests:** Authorization tests for announcement publishing/visibility and notification read-state isolation.

### `TASK-P2-07`: Phase 6 — Admin Login History, Real Reports, System Lookups & Final Grades
- **Description:**
  1. Add `LoginHistory` entity and record login attempts (timestamp, IP, user agent, login method, success/failure) in `AccountController` & `Api/AuthController`; expose Admin audit view.
  2. Replace `Views/Admin/Reports.cshtml` placeholder with real enrollment, pass-rate, and video-completion reports + CSV export.
  3. Group `ItemTypes` / `ViewAnswerType` lookups under `Admin/Settings`.
  4. Implement `Grades` (`FinalGrades`) transcript view if required separately from assessment gradebooks (`docs/project.md` Sec. 6.4).
  5. Clarify with owner and either remove or scope placeholder `Admin/Schedule`, `Payments`, `Invoices`, `Payroll` sidebar links and views.
- **Why it is needed:** Completes `docs/project.md` Sec. 6.4 and Sec. 7.
- **Affected files:** `Models/`, `Data/`, `Controllers/AdminController.cs`, `Controllers/AccountController.cs`, `Views/Admin/*.cshtml`, `Views/Shared/_Sidebar.cshtml`
- **Dependencies:** None
- **Priority:** P2
- **Estimated complexity:** L
- **Acceptance criteria:** Admin can audit login history, view real reports/lookups, and no unimplemented financial placeholder pages masquerade as live modules.
- **Required tests:** Login history recording tests (success + failure) and Admin-only access control tests.

---

## Phase 4: P3 — UI/UX Improvements & Performance Optimization

### `TASK-P3-01`: Eliminate N+1 Queries & Over-Fetching in Admin/Enrollment/User Views
- **Description:** Refactor `EnrollmentController.Index` and `CourseStudents` to compute `HasStudentLearningActivity` in a single batched query per student/course instead of 3N queries in a loop. Update `AdminController.Index` and `UserService.GetUsersAsync` to use SQL-side `.Select()` projections (`Count()`) rather than loading full entity graphs into memory.
- **Why it is needed:** Current loops and full `.Include()` graphs degrade linearly with enrollment and user growth.
- **Affected files:** `Controllers/EnrollmentController.cs`, `Services/Implementations/EnrollmentService.cs`, `Controllers/AdminController.cs`, `Services/Implementations/UserService.cs`
- **Dependencies:** None
- **Priority:** P3
- **Estimated complexity:** M
- **Acceptance criteria:** `Enrollment/Index` and `Enrollment/CourseStudents` execute a constant number of SQL queries regardless of student/enrollment count.
- **Required tests:** Query-count / regression verification via existing enrollment and admin dashboard integration suites.

### `TASK-P3-02`: Add Server-Side Pagination & Dark-Mode/Accessibility Polish
- **Description:** Add pagination to `UserController.Index`, `AcademicController.Course`, `AdminController.Files`, and `TeacherController.Grades`. Replace hardcoded `bg-white` / `text-dark` classes in role views with theme-aware CSS variables for dark mode consistency, and add `aria-label`s to icon-only table buttons.
- **Why it is needed:** Large datasets will cause slow page renders without pagination, and dark mode currently clashes with hardcoded `bg-white` cards.
- **Affected files:** `Controllers/`, `Views/`, `wwwroot/css/`
- **Dependencies:** None
- **Priority:** P3
- **Estimated complexity:** M
- **Acceptance criteria:** All major tables support paged queries; icon buttons have accessible labels; dark mode cards maintain >=4.5:1 contrast.
- **Required tests:** Manual/UI responsive + dark-mode check and pagination boundary tests.

---

## Phase 5: P4 — Refactoring & Technical Debt

### `TASK-P4-01`: Extract Dedicated xUnit Test Project & Add HTTP Controller Security Tests
- **Description:** Create a separate `AUBDigitalAcademy.Tests` xUnit project using `WebApplicationFactory<Program>` to test HTTP endpoints (including `AcademicController` cross-teacher 403 IDOR tests, `Api/AuthController`, `AccountController`, `StudentController`, `TeacherController`), wire/migrate the 10 unwired `Data/*TestRunner.cs` suites, and remove test runners from the production web assembly.
- **Why it is needed:** `dotnet test` currently finds 0 tests, production Docker builds ship 19 test runner classes, and HTTP controller/filter pipelines are untested.
- **Affected files:** `AUBDigitalAcademy.csproj`, `Program.cs`, `Data/*TestRunner.cs`, new `tests/AUBDigitalAcademy.Tests/`
- **Dependencies:** None
- **Priority:** P4
- **Estimated complexity:** L
- **Acceptance criteria:** `dotnet test` executes unit + HTTP integration suites cleanly, including negative 403 tests for every role boundary.
- **Required tests:** Full `dotnet test` suite green.

### `TASK-P4-02`: Decompose `AcademicController` & `CurriculumService` and Remove Dead Files
- **Description:**
  1. Split `CurriculumService` (2,666 lines) into focused services (`ICurriculumAuthoringService`, `IQuizService`, `IAssignmentService`, `IStudentLearningService`, `IGradebookService`) and move direct `_context` queries out of `AcademicController` (2,133 lines) and `AdminController`.
  2. Deduplicate `PopulateDropdownsAsync` / `PopulateEditDropdownsAsync` in `AcademicController`.
  3. Add explicit `User` FK/navigation on `CourseProgress.StudentId` in `ApplicationDbContext`.
  4. Remove dead/unused files (`Data/DbInitializer.cs`, `Models/Role.cs`, `GoogleAuthService` empty stubs if unused, unused 1-line layout/partial stubs, and 8 unreferenced JS files + `login.css` in `wwwroot/`).
- **Why it is needed:** Improves maintainability, testability, and eliminates confusion from dead files and duplicate code.
- **Affected files:** `Controllers/AcademicController.cs`, `Services/`, `Data/ApplicationDbContext.cs`, `Models/CourseProgress.cs`, unused `Views/` and `wwwroot/` files
- **Dependencies:** `TASK-P4-01`
- **Priority:** P4
- **Estimated complexity:** L
- **Acceptance criteria:** Every service has single responsibility; build and full test suite pass with no unused/dead stub files remaining.
- **Required tests:** Full regression suite (`dotnet test`).
