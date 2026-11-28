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

### `TASK-P0-02`: Redact Telegram Bot Token from HTTP Client Logs & Harden OTP Logging — **[COMPLETED 2026-11-26]**
- **Description:** Suppress `System.Net.Http.HttpClient` `Information` request-URI logging and replace the default `HttpClient` logging handlers with a custom redacting `IHttpClientAsyncLogger` (`TelegramHttpClientLogger`) so the Telegram Bot token is never written to stdout/logs or logging scopes while preserving method, operation, status code, and latency diagnostics. Sanitize exceptions (`RedactedTelegramException`), HTTP error bodies, and polling command logs (`/start link_`, `/link`) via `TelegramLogSanitizer`, differentiate simulation logging by `IHostEnvironment.IsDevelopment()`, and keep plaintext OTPs suppressed from logs in all environments.
- **Why it is needed:** `AddHttpClient<ITelegramService, TelegramService>()` previously logged the full `https://api.telegram.org/bot<TOKEN>/...` URL every 20 seconds during polling and on every message send, and raw exceptions or `/link` messages could expose tokens.
- **Affected files:** `Program.cs`, `appsettings.json`, `Services/Implementations/TelegramService.cs`, `Services/Implementations/TelegramPollingService.cs`, `Services/Implementations/TelegramLogSanitizer.cs`, `Services/Implementations/TelegramHttpClientLogger.cs`, `Data/TelegramLogRedactionTestRunner.cs`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** S
- **Acceptance criteria:**
  - Startup, message send, error/exception, and polling logs contain zero occurrences of the Telegram Bot token URL segment. *(Verified)*
  - Plaintext OTPs and link secrets are never written to logs in any environment; simulation mode logs `Information` in `Development` and `Warning` in non-Development. *(Verified)*
- **Required tests:** `--test-telegram-log-redaction` (10/10 passed at `LogLevel.Trace`), `--test-api-auth` (12/12 passed), `--test-auth` (12/12 passed), `--test-registration-telegram` (14/14 passed), `--test-telegram-bot` (passed), `--test-telegram-polling` (passed). *(Executed on 2026-11-26.)*

### `TASK-P0-03`: Sanitize HTML Lesson Content & Fix Frontend Stored XSS Vectors — **[COMPLETED 2026-11-26]**
- **Description:** Introduce `IHtmlSanitizationService` / `HtmlSanitizationService` backed by `Ganss.Xss` (`HtmlSanitizer` `11.0.26`) with an explicit allowlist of safe educational HTML elements, attributes, and `http`/`https`/`mailto` schemes. Sanitize `Lesson.ContentHtml` and validate `Lesson.VideoUrl` on write (`CurriculumService.CreateLessonAsync` / `UpdateLessonAsync`) and re-sanitize on read (`GetStudentLessonPlayerAsync`) and render (`Views/Student/Lesson.cshtml`) to protect legacy stored records. Replace unescaped `innerHTML` concatenation in `Views/Academic/Course.cshtml` (TA modal) with `document.createElement` / `textContent` / `replaceChildren`. Validate `http://`/`https://` or safe relative paths on `Book.FileUrl`, `Course.Thumbnail`, `User.Avatar`, and assignment URLs at write/read/render boundaries, and remove unnecessary `@Html.Raw` in `Views/Student/QuizResult.cshtml` and inline token interpolation in `Views/Account/LinkTelegram.cshtml`.
- **Why it is needed:** `@Html.Raw(Model.ContentHtml)`, `innerHTML` string concatenation, and unvalidated `href`/`src` attributes previously exposed students, teachers, and admins to Stored XSS if malicious markup or `javascript:`/`data:` URLs were stored.
- **Affected files:** `AUBDigitalAcademy.csproj`, `Program.cs`, `Services/Interfaces/IHtmlSanitizationService.cs`, `Services/Implementations/HtmlSanitizationService.cs`, `Services/Implementations/CurriculumService.cs`, `Services/Implementations/CourseService.cs`, `Services/Implementations/TeacherService.cs`, `Controllers/AcademicController.cs`, `Controllers/StudentController.cs`, `Controllers/TeacherController.cs`, `Views/Student/Lesson.cshtml`, `Views/Academic/Course.cshtml`, `Views/Student/Book.cshtml`, `Views/Teacher/Books.cshtml`, `Views/Student/Assignment.cshtml`, `Views/Teacher/Submissions.cshtml`, `Views/Home/Index.cshtml`, `Views/Admin/Index.cshtml`, `Views/User/Details.cshtml`, `Views/User/Index.cshtml`, `Views/Student/QuizResult.cshtml`, `Views/Account/LinkTelegram.cshtml`, `Data/StoredXssIntegrationTestRunner.cs`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** M
- **Acceptance criteria:**
  - `<script>`, `onerror=` / `onclick=` event handlers, active elements (`<iframe>`, `<svg>`, `<object>`, `<embed>`), and `javascript:`/`data:`/`vbscript:` payloads in lesson HTML, TA names/emails/IDs, book URLs, course thumbnails, and assignment URLs are stripped or rejected. *(Verified)*
  - Safe educational formatting tags (`<p>`, `<h2>`, `<ul>`, `<strong>`, `<blockquote>`, `<pre><code>`, `<table>`, `<a>` with safe `href` and `rel="noopener noreferrer"`, safe `<img>`) still render properly in `Lesson.cshtml`. *(Verified)*
  - Existing legacy DB rows with unsanitized `ContentHtml` or `Book.FileUrl` are safely sanitized at read and render time. *(Verified)*
- **Required tests:** `--test-stored-xss` (10/10 passed), `--test-telegram-log-redaction` (10/10 passed via `--no-launch-profile`), `--test-api-auth` (12/12 passed), `--test-auth` (12/12 passed), `--test-registration-telegram` (14/14 passed), `--test-course-integration` (16/16 passed), `--test-phase4` (26/26 passed). *(Executed on 2026-11-26.)*

### `TASK-P0-04`: Enforce Telegram 2FA on Google OAuth & Gate `SeedData` Password Resets — **[COMPLETED 2026-11-27]**
- **Description:** Rewrote `AccountController.ExternalLogin` and `ExternalLoginCallback` to delegate to shared `IAuthService.InitiateExternalTwoFactorLoginAsync` (`AuthService`): resolve local accounts strictly via `UserManager.FindByLoginAsync`, reject unassociated Google accounts without auto-creation or silent email linking, enforce `IsActive`, `IsLockedOutAsync`, `CanSignInAsync`, and linked Telegram verification (`IsTelegramVerified && TelegramChatId.HasValue`), prevent callback replay (`AubExternalNonce` / `IssuedUtc` single-use tracking + immediate `IdentityConstants.ExternalScheme` sign-out), set `Options.SaveTokens = false` and `OnRemoteFailure` in `Program.cs`, and require `VerifyTelegram` 2FA completion via `ITimeLimitedDataProtector`-protected `AUB_PendingVerification` cookie before issuing the application Identity cookie. Hardened `SeedData.InitializeAsync` (`CreateSeedUserIfMissingAsync`) so local-only Development seed users are created only when the user store is initially empty in Development/`--test-*` runs, and existing passwords, lockouts, active flags, profile fields, roles, and deleted accounts are never overwritten or resurrected on startup.
- **Why it is needed:** Google OAuth previously bypassed mandatory 2FA and could auto-create or silently link accounts by email, and `SeedData` silently reset Admin/Teacher/TA/Student passwords back to weak defaults (`admin123`, etc.) on every app restart.
- **Affected files:** `Services/Interfaces/IAuthService.cs`, `Services/Implementations/AuthService.cs`, `Controllers/AccountController.cs`, `Views/Account/Login.cshtml`, `Data/SeedData.cs`, `Data/GoogleOAuth2FaIntegrationTestRunner.cs`, `Program.cs`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** M
- **Acceptance criteria:**
  - Google OAuth users must have an associated local account with linked, verified Telegram and complete Telegram 2FA before receiving an authenticated application cookie. *(Verified)*
  - Unassociated Google accounts cannot auto-create accounts or silently link by email, and replayed external callbacks are rejected. *(Verified)*
  - Existing user password hashes, active/lockout states, profile fields, roles, and deletions persist across repeated `SeedData.InitializeAsync` runs. *(Verified)*
- **Required tests:** `--test-google-oauth-2fa` (14/14 passed), `--test-api-auth` (12/12 passed), `--test-auth` (12/12 passed), `--test-registration-telegram` (14/14 passed), `--test-telegram-log-redaction` (10/10 passed via `--no-launch-profile`), `--test-stored-xss` (10/10 passed), `--test-course-integration` (16/16 passed), `--test-phase4` (26/26 passed). *(Executed on 2026-11-27.)*

### `TASK-P0-05`: Registration & Telegram Link Status Security Hardening — **[COMPLETED 2026-11-27]**
- **Description:** Hardened the self-service Registration (`Register` -> `LinkTelegram` -> `VerifyTelegram`) and Telegram account-linking lifecycle across `AccountController`, `AuthService`, `TelegramService`, `TelegramPollingService`, and auth views. `AccountController.Register` creates the pending student (`IsTelegramVerified = false`, `TelegramChatId = null`, Student role only) without dispatching OTPs or issuing an Identity cookie, sets a `TimeLimitedDataProtector`-protected `AUB_PendingVerification` cookie (`HttpOnly`, `SameSite=Lax`, `Secure` on HTTPS, `IsEssential`, revocable `SessionNonce`, `authMethod: "Registration"`), and redirects to `LinkTelegram`. `CheckTelegramLinkStatus` (`[HttpGet]`, `NoStore`) validates the pending session, expiry, `UserId` match, user `IsActive`/`!IsLockedOutAsync` eligibility, ignores client-supplied flags, and returns uniform `401 { linked = false }` without disclosing unrelated accounts. `TelegramService.ProcessLinkTokenAsync` atomically claims tokens and binds `TelegramChatId` inside a DB transaction (`ExecuteUpdateAsync`) without overwriting existing `TelegramChatId` associations, blocks cross-user chat reuse, leaves `IsTelegramVerified = false` until OTP verification passes, and masks `ChatId` in logs; `TelegramPollingService` rejects non-private/bot updates and deduplicates `UpdateId`s; `AuthService` fails closed on missing `TelegramChatId` or Telegram API failure and consumes OTPs atomically; and `CancelVerification` (`[HttpPost]`, `[ValidateAntiForgeryToken]`) revokes pending state and unused challenges.
- **Why it is needed:** Previously, `Register` dispatched OTPs with a `null` `TelegramChatId` (falling back to global `Telegram:ChatId` or simulation logs) and skipped `LinkTelegram`; `CheckTelegramLinkStatus` lacked pending-session ownership validation; `ProcessLinkTokenAsync` marked `IsTelegramVerified = true` prematurely, could overwrite existing `TelegramChatId`s, and lacked concurrency protection; and OTP dispatch did not fail closed on Telegram API errors.
- **Affected files:** `Controllers/AccountController.cs`, `Services/Interfaces/IAuthService.cs`, `Services/Implementations/AuthService.cs`, `Services/Implementations/TelegramService.cs`, `Services/Implementations/TelegramPollingService.cs`, `Services/Implementations/TelegramLogSanitizer.cs`, `Views/Account/Register.cshtml`, `Views/Account/LinkTelegram.cshtml`, `Views/Account/VerifyTelegram.cshtml`, `Data/RegistrationTelegramSecurityTestRunner.cs`, `Program.cs`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** M
- **Acceptance criteria:**
  - New students registering with Student ID + Name + Password are directed to `LinkTelegram`, receive no OTP while `TelegramChatId` is null, keep `IsTelegramVerified = false` until OTP succeeds, and receive no Identity cookie before verification. *(Verified)*
  - `CheckTelegramLinkStatus` rejects missing, expired, tampered, replayed, or mismatched `AUB_PendingVerification` state with `401 { linked = false }` without revealing unrelated accounts. *(Verified)*
  - Telegram linking challenges are single-use, concurrency-safe, private-chat only, cannot overwrite existing `TelegramChatId`s or reuse another user's Telegram account, and fail closed on Telegram API failure. *(Verified)*
- **Required tests:** `--test-registration-telegram-security` (24/24 passed), `--test-google-oauth-2fa` (14/14 passed), `--test-api-auth` (12/12 passed), `--test-auth` (12/12 passed), `--test-registration-telegram` (14/14 passed), `--test-telegram-log-redaction` (10/10 passed via `--no-launch-profile`), `--test-stored-xss` (10/10 passed), `--test-course-integration` (16/16 passed), `--test-phase4` (26/26 passed). *(Executed on 2026-11-27; 138/138 passed.)*

### `TASK-P0-06`: Upgrade NuGet `System.Security.Cryptography.Pkcs` to Resolve NU1902/NU1903 — **[COMPLETED 2026-11-27]**
- **Description:** Traced `System.Security.Cryptography.Pkcs` `9.0.4` through `Microsoft.EntityFrameworkCore.SqlServer` `10.0.5` -> `Microsoft.Data.SqlClient` `6.1.1` and added a minimal explicit `<PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.15" />` in `AUBDigitalAcademy.csproj` (`net10.0`, zero extra transitive dependencies, satisfying `>= 9.0.4`) without upgrading EF Core, ASP.NET Core Identity, `Microsoft.Data.SqlClient`, or suppressing any NuGet audit warnings.
- **Why it is needed:** `dotnet build` previously reported two known DoS vulnerability warnings (`NU1902` Moderate `GHSA-2fmv-rv2c-59x7` and `NU1903` High `GHSA-846f-f764-76mx` on `9.0.4`, affecting `[9.0.0, 9.0.20]` and `[10.0.0, 10.0.12]`).
- **Affected files:** `AUBDigitalAcademy.csproj`
- **Dependencies:** None
- **Priority:** P0
- **Estimated complexity:** S
- **Acceptance criteria:**
  - `dotnet restore` resolves `System.Security.Cryptography.Pkcs` `10.0.15` with no downgrades or dependency conflicts and no changes to EF Core/Identity/AspNetCore/`Microsoft.Data.SqlClient` versions. *(Verified)*
  - `dotnet list package --vulnerable --include-transitive` reports zero vulnerable packages and `dotnet build --no-restore` completes with 0 errors and 0 warnings. *(Verified)*
- **Required tests:** `dotnet restore`, `dotnet list package --vulnerable --include-transitive` (0 vulnerabilities), `dotnet build --no-restore` (0 errors, 0 warnings), and all 9 regression suites (`--test-google-oauth-2fa` 14/14, `--test-api-auth` 12/12, `--test-auth` 12/12, `--test-registration-telegram` 14/14, `--test-registration-telegram-security` 24/24, `--test-telegram-log-redaction` 10/10, `--test-stored-xss` 10/10, `--test-course-integration` 16/16, `--test-phase4` 26/26 — 138/138 passed on 2026-11-27).

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

### `TASK-P1-02`: Implement Secure Google Account Linking, Unlinking & Profile Security — **[COMPLETED 2026-11-27]**
- **Description:** Implemented secure self-service Google account linking and unlinking in `Controllers/SettingsController.cs` (`/Profile`, `/Settings`, `/Settings/Profile`, `/Settings/StartGoogleLink`, `/Settings/LinkGoogle`, `/Settings/LinkGoogleCallback`, `/Settings/StartGoogleUnlink`, `/Settings/UnlinkGoogle`, `/Settings/CancelGoogleSecurityAction`, `/Settings/ResendGoogleStepUpCode`) backed by `IGoogleAuthService` / `GoogleAuthService` and `ITelegramService.SendGoogleSecurityOtpAsync`. Enforced mandatory Telegram OTP step-up verification for BOTH linking (`GLK:`) and unlinking (`GUL:`) delivered strictly to verified `TelegramChatId` (fail-closed on missing bot token or API failure; no simulation mode or fallback chat). Built defense against OAuth replay and account takeover: purpose-bound `AubExternalNonce` (`link:` vs `login:`, isolated in single-use memory cache with 5-minute expiry), `XsrfId` binding to authenticated `UserId`, single Google account per user, rejection of `ProviderKey` collisions without disclosing identity, rejection of unlinking when no local password is set, immediate `SecurityStamp` rotation on link/unlink, safe cookie re-issuance, and `SaveTokens = false`. Built complete `/Profile` UI in `Views/Settings/Profile.cshtml` with `ProfileSecurityViewModel` exposing status flags without sensitive tokens/keys/IDs, and fixed `Admin/Settings.cshtml` `/User/Profile` link to `/Profile`.
- **Why it is needed:** Authenticated users had no secure way to link or unlink Google logins, `/Profile` returned 404, Google linking lacked step-up Telegram 2FA verification, unlinking could lock users out of their accounts if no password existed, and external OAuth callbacks lacked purpose-bound nonce isolation.
- **Affected files:** `Controllers/SettingsController.cs`, `Services/Interfaces/IGoogleAuthService.cs`, `Services/Implementations/GoogleAuthService.cs`, `Services/Interfaces/ITelegramService.cs`, `Services/Implementations/TelegramService.cs`, `Services/Interfaces/IAuthService.cs`, `Services/Implementations/AuthService.cs`, `Models/ViewModels/ProfileSecurityViewModel.cs`, `Views/Settings/Profile.cshtml`, `Views/Admin/Settings.cshtml`, `Views/Account/Login.cshtml`, `Program.cs`, `Data/GoogleAccountLinkingSecurityTestRunner.cs`
- **Dependencies:** `TASK-P0-04`, `TASK-P0-05`
- **Priority:** P1
- **Estimated complexity:** M
- **Acceptance criteria:**
  - Linking and unlinking both require step-up Telegram OTP verification to the user's verified personal `TelegramChatId`. *(Verified)*
  - Nonces are purpose-bound and single-use; OAuth state is tied to `UserId` via `XsrfId`. *(Verified)*
  - No auto-linking by email; `ProviderKey` collisions and multiple Google accounts are rejected. *(Verified)*
  - Unlinking is blocked if the user has no local password; unlinking only removes Google login. *(Verified)*
  - `SecurityStamp` is rotated on link/unlink and all tokens/keys remain unexposed in UI and responses. *(Verified)*
- **Required tests:** `--test-google-linking-security` (42/42 passed), plus full regression suites (199/199 total passed on 2026-11-27).

### `TASK-P1-03`: Implement Self-Service Password Reset via Telegram 2FA — **[COMPLETED 2026-11-27]**
- **Description:** Replaced `AccountController.ForgotPassword` / `ResetPassword` stubs and removed the discarded token / `emailConfigured` enumeration branch from `Api/AuthController.ForgotPassword`. Implemented purpose-isolated Telegram OTP password recovery (`RST:` prefix in `AuthService`, separate `AUB_PendingPasswordReset` TimeLimitedDataProtector cookie with revocable `SessionNonce`, `ITelegramService.SendPasswordResetCodeAsync`, 10-minute OTP expiry, 5-attempt limit, atomic consumption, 60s cross-identifier cooldown, and server-side `UserManager.GeneratePasswordResetTokenAsync` + `ResetPasswordAsync` only after OTP verification). Configured 15-minute `DataProtectionTokenProviderOptions.TokenLifespan`, `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`, `sstamp` JWT SecurityStamp validation across all protected `/api/*` endpoints, `PasswordResetPolicy` rate limiting (`429`), `[ResponseCache(NoStore = true)]`, and redacted logging.
- **Why it is needed:** Previously `AccountController.ResetPassword` POST was a no-op stub that redirected to confirmation without changing the password, `Api/AuthController.ForgotPassword` leaked account/email existence when SMTP was set, and old cookies/JWTs remained valid after password resets.
- **Affected files:** `Program.cs`, `Services/Interfaces/ITelegramService.cs`, `Services/Implementations/TelegramService.cs`, `Services/Interfaces/IAuthService.cs`, `Services/Implementations/AuthService.cs`, `Services/Implementations/UserService.cs`, `Controllers/AccountController.cs`, `Controllers/Api/AuthController.cs`, `Models/ViewModels/ForgotPasswordViewModel.cs`, `Models/ViewModels/ResetPasswordViewModel.cs`, `Views/Account/ForgotPassword.cshtml`, `Views/Account/ForgotPasswordConfirmation.cshtml`, `Views/Account/ResetPassword.cshtml`, `Data/PasswordResetSecurityTestRunner.cs`
- **Dependencies:** `TASK-P0-05`
- **Priority:** P1
- **Estimated complexity:** M
- **Acceptance criteria:**
  - Only active, non-locked users with a verified personal `TelegramChatId` receive reset OTPs and can complete password resets; responses and cookies are enumeration-resistant. *(Verified)*
  - Old Identity cookies and old JWTs across all protected API routes are invalidated immediately upon password reset (`SecurityStamp` rotation). *(Verified)*
  - Login OTPs and Password Reset OTPs are strictly isolated and cannot be cross-reused. *(Verified)*
- **Required tests:** `--test-password-reset-security` (19/19 passed), plus full 9-suite regression (`138/138` passed; **157/157** total passed on 2026-11-27).

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

### ~~`TASK-P1-05`: Wire Edit Question UI & Enforce Server-Side Quiz Time Limit~~ **[COMPLETED — Server-Side Timer Hardened 2026-11-28]**
- **Description:** Add Edit Question modal and button in `Views/Academic/QuizQuestions.cshtml` wired to `AcademicController.GetQuestionJson` and `EditQuestion`. Add server-side quiz attempt start timestamp (`StartedAtUtc` & `DeadlineUtc` persisted in SQL Server `QuizAttempts` + `QuizAttemptAnswers` draft auto-save + `QuizSubmission.StartedAtUtc` / `IsAutoFinalized`) so `TimeLimitMinutes` cannot be bypassed by refreshing the browser, cache eviction, clock manipulation, or concurrent submits. *(Note: `QuestionType.Essay` remains out-of-scope until essay grading is designed.)*
- **Why it is needed:** Edit Question backend endpoints have no UI, and the quiz timer previously relied on client JS or volatile `IMemoryCache`.
- **Affected files:** `Views/Academic/QuizQuestions.cshtml`, `Models/Enums/QuizAttemptStatus.cs`, `Models/QuizAttempt.cs`, `Models/QuizAttemptAnswer.cs`, `Models/QuizSubmission.cs`, `Models/ViewModels/StudentViewModels.cs`, `Data/ApplicationDbContext.cs`, `Data/Migrations/20261128064207_AddServerSideQuizAttemptTiming.cs`, `Services/Interfaces/CurriculumOperationResult.cs`, `Services/Interfaces/ICurriculumService.cs`, `Services/Implementations/CurriculumService.cs`, `Controllers/StudentController.cs`, `Views/Student/Quiz.cshtml`, `Views/Student/QuizResult.cshtml`, `Data/QuizIntegrationTestRunner.cs`
- **Dependencies:** None
- **Priority:** P1
- **Estimated complexity:** M
- **Acceptance criteria:** Faculty can edit existing quiz questions/options in UI; server persists UTC attempt start/deadline, auto-saves pre-deadline draft answers, enforces `DeadlineUtc` (+ 30s grace) inside `SubmitQuizAsync`, prevents concurrent/repeated submission races, and UI disables submission on expiration. *(Verified)*
- **Required tests:** `--test-quiz` (19/19 passed), `--test-phase4` (26/26 passed), `--test-course-access-modes` (17/17 passed), `--test-notifications-announcements` (22/22 passed).

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

### `TASK-P2-05`: Phase 5B — Lesson Discussions & Moderation — **[COMPLETED 2026-11-27]**
- **Description:** Added `LessonDiscussionThread` and `LessonDiscussionReply` entities (`IsPinned`, `IsAnswered`, `IsInstructorAnswer`, `IsDeleted`, `DeletedById`, `ModerationReason`, timestamps) with EF Core migration `AddLessonDiscussionsAndModeration` (with Restrict on user FKs to satisfy SQL Server multiple cascade path rules). Implemented `IDiscussionService` / `DiscussionService` and `DiscussionController` with anti-forgery protection, HTML sanitization against Stored XSS, and Open Peer Discussion policy (enrolled students can post and reply to peer threads in their course). Integrated responsive discussion Q&A feed under video lessons (`Views/Student/Lesson.cshtml`) and faculty moderation controls (Teachers, assigned TAs, and Admins can reply as instructors, pin/unpin helpful answers or questions, mark answered/unanswered, and moderate/soft-delete inappropriate content with reasons). Added direct "Discussions" action links in faculty curriculum structure (`Views/Teacher/CourseDetails.cshtml`).
- **Why it is needed:** Required by `docs/project.md` Sec. 9.
- **Product Decision:** Owner confirmed Open Peer Discussion policy (enrolled students can reply to any peer's thread within the enrolled course).
- **Affected files:** `Models/LessonDiscussionThread.cs`, `Models/LessonDiscussionReply.cs`, `Models/CourseItem.cs`, `Models/User.cs`, `Data/ApplicationDbContext.cs`, `Data/Migrations/20261127135413_AddLessonDiscussionsAndModeration.cs`, `Models/ViewModels/DiscussionViewModels.cs`, `Models/ViewModels/StudentViewModels.cs`, `Services/Interfaces/IDiscussionService.cs`, `Services/Implementations/DiscussionService.cs`, `Controllers/DiscussionController.cs`, `Controllers/StudentController.cs`, `Views/Student/Lesson.cshtml`, `Views/Teacher/CourseDetails.cshtml`, `Program.cs`, `Data/LessonDiscussionsIntegrationTestRunner.cs`
- **Dependencies:** None
- **Priority:** P2
- **Estimated complexity:** L
- **Acceptance criteria:** Enrolled students can post questions and reply to peer questions; Teachers and assigned TAs can reply with faculty badge, pin helpful answers, mark answered, and moderate inappropriate comments; unassigned users get 403. *(Verified)*
- **Required tests:** `--test-lesson-discussions` (22/22 passed), `--test-course-access-modes` (17/17 passed), `--test-video-tracking` (8/8 passed). *(Executed on 2026-11-27).*

### `TASK-P2-06`: Phase 5C — In-App Notification Center & Announcements — **[COMPLETED 2026-11-28]**
- **Description:** Added `Announcement` entity (`Scope`: Global vs Course, `CourseId?`, `CreatedById`, `Title`, `Content`, `IsPinned`, `PublishedAt`, `IsArchived`) and `Notification` entity (`UserId`, `Type`: DiscussionReply, NewLesson, Announcement, GradeReleased, CourseMilestone, System, `Title`, `Message`, `TargetUrl`, `IsRead`, `CreatedAt`, `ReadAt`) with EF Core migration `AddNotificationsAndAnnouncements` (using restricted delete on User foreign keys to avoid SQL Server multiple cascade path error 1785). Implemented `INotificationService` / `NotificationService` and `NotificationController` with CSRF protection, real-time unread badge calculation, read marking, and role-based announcement permissions (Admins post Global and Course announcements; Teachers post Course announcements for owned courses only; Students/TAs forbidden). Wired automated notification triggers across LMS events: discussion replies notify thread authors (excluding self-replies); new lessons notify enrolled students; announcements notify enrolled students (or all users if Global); graded assignments and submitted quizzes notify students with score/pass status; and 100% course video completion notifies students with an idempotent milestone notification. Replaced the empty static navbar bell dropdown in `Views/Shared/_Layout.cshtml` with live unread counter badge, dynamic dropdown items, mark-all-read action, and link to Notification Center (`Views/Notification/Index.cshtml`) and Announcements feed (`Views/Notification/Announcements.cshtml`). Added "Post Announcement" modal in `Views/Teacher/CourseDetails.cshtml`.
- **Why it is needed:** Required by `docs/project.md` Sec. 10.1–10.2.
- **Affected files:** `Models/Enums/AnnouncementScope.cs`, `Models/Enums/NotificationType.cs`, `Models/Announcement.cs`, `Models/Notification.cs`, `Models/Course.cs`, `Models/User.cs`, `Data/ApplicationDbContext.cs`, `Data/Migrations/20261128054626_AddNotificationsAndAnnouncements.cs`, `Models/ViewModels/NotificationViewModels.cs`, `Services/Interfaces/INotificationService.cs`, `Services/Implementations/NotificationService.cs`, `Services/Implementations/DiscussionService.cs`, `Services/Implementations/CurriculumService.cs`, `Services/Implementations/TeacherService.cs`, `Controllers/NotificationController.cs`, `Views/Shared/_Layout.cshtml`, `wwwroot/js/site.js`, `Views/Notification/Index.cshtml`, `Views/Notification/Announcements.cshtml`, `Views/Teacher/CourseDetails.cshtml`, `Data/NotificationAnnouncementIntegrationTestRunner.cs`, `Program.cs`
- **Dependencies:** `TASK-P2-05` (for discussion reply triggers)
- **Priority:** P2
- **Estimated complexity:** L
- **Acceptance criteria:** Admin can publish global and course announcements; Teacher can publish course announcements only for owned courses; Students and TAs are forbidden from publishing announcements; Students see global announcements and course announcements for courses they are enrolled in; Automated notification dispatch functions for discussion replies, new lessons, announcements, graded submissions/quizzes, and 100% video milestone; Cross-user notification read-state isolation is enforced; Unread badge count accurately updates. *(Verified)*
- **Required tests:** `--test-notifications-announcements` (22/22 passed), `--test-lesson-discussions` (22/22 passed), `--test-course-access-modes` (17/17 passed), `--test-video-tracking` (8/8 passed). *(Executed on 2026-11-28).*

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
