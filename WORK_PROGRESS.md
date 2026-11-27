# AUB Digital Academy — Codebase Audit & Work Progress

> **Last Verified:** 2026-11-27  
> **Reference Specification:** [`docs/project.md`](./docs/project.md)  
> **Audit Deliverables:** [`docs/FULL_PROJECT_AUDIT.md`](./docs/FULL_PROJECT_AUDIT.md) | [`docs/DEVELOPMENT_ROADMAP.md`](./docs/DEVELOPMENT_ROADMAP.md) | [`docs/FEATURE_STATUS.md`](./docs/FEATURE_STATUS.md)

---

## 1. Verified Project Health & Build/Test Status

- **Build (`dotnet build --no-restore`):** **0 Errors, 0 Warnings** (`NU1902`/`NU1903` resolved by pinning `System.Security.Cryptography.Pkcs` `10.0.15`; `dotnet list package --vulnerable --include-transitive` reports zero vulnerable packages).
- **EF Core Migrations:** All 4 SQL Server migrations (`InitialSqlServerCreate`, `AddCourseAssistantJoinTable`, `Phase4_StudentLearningAssessmentWorkflow`, `Phase4_FinalRelationalAttachmentsAndQuizSecurity`) are applied cleanly against local `aub_sqlserver`.
- **Test Suites:**
  - `dotnet test`: **0 test projects** (no xUnit/NUnit/MSTest project exists yet).
  - **Security, Authentication & Curriculum Suites Executed After `TASK-P1-02` (2026-11-27): 199/199 PASSED:**
    - `--test-google-linking-security` (`GoogleAccountLinkingSecurityTestRunner`): 42/42 passed
    - `--test-password-reset-security` (`PasswordResetSecurityTestRunner`): 19/19 passed
    - `--test-registration-telegram-security` (`RegistrationTelegramSecurityTestRunner`): 24/24 passed
    - `--test-google-oauth-2fa` (`GoogleOAuth2FaIntegrationTestRunner`): 14/14 passed
    - `--test-api-auth` (`ApiAuth2FaIntegrationTestRunner`): 12/12 passed
    - `--test-auth` (`AuthIntegrationTestRunner`): 12/12 passed
    - `--test-registration-telegram` (`RegistrationTelegramIntegrationTestRunner`): 14/14 passed
    - `--test-telegram-log-redaction` (`TelegramLogRedactionTestRunner`, run with `--no-launch-profile`): 10/10 passed
    - `--test-stored-xss` (`StoredXssIntegrationTestRunner`): 10/10 passed
    - `--test-course-integration` (`CourseIntegrationTestRunner`): 16/16 passed
    - `--test-phase4` (`StudentLearningAssessmentTestRunner`): 26/26 passed
  - **Other Wired CLI Integration Runners (executed during 2026-11-26 baseline audit, unchanged modules): 57/57 PASSED:**
    - `--test-assistance-role`: 14/14 passed
    - `--test-teacher-portal`: 14/14 passed
    - `--test-user-management`: 14/14 passed
    - `--test-enrollment-ui`: 13/13 passed
    - `--test-quiz`: 16/16 passed
    - `--test-telegram-bot`: connected to `@AUBLinkBot` with zero token exposure in logs/stdout (verified under `TASK-P0-02`)
  - **Unwired CLI Runners (10 files in `Data/`):** Not wired into `Program.cs` (`CourseCreationTestRunner`, `CourseDeleteTestRunner`, `CurriculumEndToEndIntegrationTestRunner`, `EnrollmentBackendTestRunner`, `LessonCrudTestRunner`, `ModuleCrudTestRunner`, `ModuleUiIntegrationTestRunner`, `StudentFlowIntegrationTestRunner`, `StudentPlayerIntegrationTestRunner`, `StudentProgressIntegrationTestRunner`).
  - **Coverage Gap:** No automated HTTP/Controller integration tests exist (e.g. testing `[Authorize]` filters or `AcademicController` 403 IDOR rejections).

---

## 2. Verified Completed Foundation (Phases 1–4)

- **Identity, Web Login & API 2FA Auth (`TASK-P0-01` Completed 2026-11-26):** 4 roles (`Admin`, `Teacher`, `Assistance`, `Student`), University ID / Email login, shared credential/lockout/OTP engine (`IAuthService` / `AuthService`), mandatory Telegram 2FA (`@AUBLinkBot`, `TelegramPollingService`, 6-digit single-use codes, SHA-256 `TelegramLinkTokens`) enforced on both Web Login (`/Account/Login` -> `/Account/VerifyTelegram`) and API Login (`POST /api/Auth/login` -> `POST /api/Auth/verify-2fa`) with time-limited DataProtection `verificationId`, strict HS256 JWT validation, and no hardcoded fallback JWT secret.
- **Recent Security Fixes Verified in Source:**
  - `AcademicController`: `[Authorize(Roles = "Admin")]` enforced on `CreateCourse`, `DeleteCourse`, and all TA assignment endpoints (`GetCourseAssistantsJson`, `AssignCourseAssistant`, `UnassignCourseAssistant`, `ToggleAssistantCanGrade`); `CanManageCourseAsync` / `ResolveCourseIdFrom*Async` enforced across course/module/lesson/quiz/book/assignment/question endpoints.
  - `SeedData` / `Program.cs`: `CS301` TA `CanGrade = true` seeding is gated to Development or `--test-*` CLI runners.
- **Core LMS & Assessment:** Course/Chapter/Item CRUD, Student Enrollments (unique index + safe unenroll guard), Lesson/Book players, Quiz engine (default `MaxAttempts = 3`, future-only `MaxAttemptsAllowed`, best score, 50% pass threshold, exhaustion-gated answer reveal), Assignment submission/resubmission (<=5 files, <=10 MB, PDF/ZIP/DOCX magic-byte validation stored outside `wwwroot` + external URLs + text), Faculty grading (`IsLatest`-only + `CanGrade` enforcement), and Admin attachment soft-delete/restore.

---

## 3. Critical Open Gaps & Verified Bugs (Prioritized)

### P0 — Critical Security & Integrity
1. ~~**API Login (`POST /api/Auth/login`) 2FA Bypass & Hardcoded JWT Secret (`TASK-P0-01`):**~~ **RESOLVED (2026-11-26).** Shared `AuthService` now enforces lockout, active status, linked Telegram verification, OTP dispatch + single-use verification (`POST /api/Auth/verify-2fa`), and configured `Jwt:Secret`/`Jwt:Key`/`JWT_SECRET` (>=32 bytes, fail-closed `500 ConfigurationError` when unconfigured). Set `JWT_SECRET` in local `.env` or `dotnet user-secrets` before using API endpoints.
2. ~~**Telegram Bot Token & Simulation OTP Logging (`TASK-P0-02`):**~~ **RESOLVED (2026-11-26).** `Program.cs` now removes default `HttpClient` URI loggers (`.RemoveAllLoggers()`) and attaches `TelegramHttpClientLogger` (`IHttpClientAsyncLogger`) to log redacted Telegram endpoints (`bot[REDACTED]/<operation>`), HTTP methods, status codes, and latency without raw tokens or payloads. `TelegramLogSanitizer` + `RedactedTelegramException` sanitize Telegram API URLs, bot tokens, JWTs, sensitive query/key-value secrets, error response bodies, and exception chains across `TelegramService` and `TelegramPollingService` (including redacting `/start link_` and `/link` command arguments); simulation mode differentiates `IHostEnvironment.IsDevelopment()` (`Information` vs `Warning`) and never logs plaintext OTPs in any environment. Verified via `--test-telegram-log-redaction` (10/10 passed at `LogLevel.Trace`).
3. ~~**Stored XSS Vectors (`TASK-P0-03`):**~~ **RESOLVED (2026-11-26).** Added `Ganss.Xss` (`HtmlSanitizer` `11.0.26`) via `IHtmlSanitizationService` / `HtmlSanitizationService` with an explicit educational HTML tag/attribute/scheme allowlist (`SanitizeLessonHtml`) and strict URL validation (`SanitizeResourceUrl`, `SanitizeHttpUrl`). Lesson HTML and `VideoUrl` are sanitized at write boundaries (`CurriculumService.CreateLessonAsync` / `UpdateLessonAsync`) and re-sanitized on read (`GetStudentLessonPlayerAsync`) and render (`Views/Student/Lesson.cshtml`) to protect legacy DB rows. `Views/Academic/Course.cshtml` TA modal rendering was rewritten to use `document.createElement`, `textContent`, and `replaceChildren` instead of `innerHTML` concatenation. `Book.FileUrl`, `Course.Thumbnail`, `User.Avatar`, and assignment external URLs are validated on write and/or sanitized before rendering across `Student/Book.cshtml`, `Teacher/Books.cshtml`, `Student/Assignment.cshtml`, `Teacher/Submissions.cshtml`, `Home/Index.cshtml`, `Admin/Index.cshtml`, `User/Details.cshtml`, and `User/Index.cshtml`. Verified via `--test-stored-xss` (10/10 passed).
4. ~~**Google OAuth 2FA Bypass & Unconditional `SeedData` Password Reset (`TASK-P0-04`):**~~ **RESOLVED (2026-11-27).** `AccountController.ExternalLoginCallback` no longer calls `ExternalLoginSignInAsync`, `SignInAsync` prior to 2FA, auto-creates accounts, or silently links Google logins by email; instead it resolves local users strictly via `UserManager.FindByLoginAsync`, enforces `IsActive`, `IsLockedOutAsync`, `CanSignInAsync`, and linked Telegram verification (`IsTelegramVerified && TelegramChatId.HasValue`), prevents external login replay via single-use `AubExternalNonce` / `IssuedUtc` tracking and immediate `IdentityConstants.ExternalScheme` sign-out, dispatches OTP via shared `IAuthService.InitiateExternalTwoFactorLoginAsync`, stores a time-limited DataProtection `AUB_PendingVerification` cookie, and issues the application Identity cookie only after `VerifyTelegram` succeeds. `SeedData.InitializeAsync` now seeds local-only Development accounts (`admin@aub.com`, `teacher@aub.com`, `ta@aub.com`, `student@aub.com`) strictly when `!hasUsers && isDevOrTest` and never overwrites existing passwords, lockouts, active flags, profile fields, or roles, nor recreates deleted accounts. Verified via `--test-google-oauth-2fa` (14/14 passed).
5. ~~**Registration Flow & Link Status Check (`TASK-P0-05`):**~~ **RESOLVED (2026-11-27).** `AccountController.Register` no longer dispatches OTPs to a `null` `TelegramChatId` or redirects to `VerifyTelegram`; instead it creates the student with `IsTelegramVerified = false` and `TelegramChatId = null`, issues a protected `AUB_PendingVerification` cookie (`authMethod: "Registration"`, single-use `SessionNonce`), and redirects to `LinkTelegram`. `TelegramService.ProcessLinkTokenAsync` uses a serializable database transaction + atomic `ExecuteUpdateAsync` to claim single-use tokens, prevents overwriting existing `TelegramChatId` values, prevents cross-account Telegram reuse, keeps `IsTelegramVerified = false` until OTP verification succeeds, and masks `ChatId` in logs (`TelegramLogSanitizer.MaskChatId`). `TelegramPollingService` rejects group/non-private chats and bot senders and deduplicates `UpdateId`s. `CheckTelegramLinkStatus` enforces a valid non-expired/non-replayed `AUB_PendingVerification` cookie matching `userId`, verifies `IsActive` and `!IsLockedOutAsync`, ignores client-supplied flags, and returns `401` uniformly without revealing unrelated accounts. `AuthService` fails closed on missing `TelegramChatId` or Telegram API failure and consumes OTPs atomically. Verified via `--test-registration-telegram-security` (24/24 passed).
6. ~~**NuGet Security Warnings (`TASK-P0-06`):**~~ **RESOLVED (2026-11-27).** Transitive dependency `Microsoft.EntityFrameworkCore.SqlServer` `10.0.5` -> `Microsoft.Data.SqlClient` `6.1.1` previously resolved `System.Security.Cryptography.Pkcs` `9.0.4` (`NU1902` / `GHSA-2fmv-rv2c-59x7`, `NU1903` / `GHSA-846f-f764-76mx`). Added an explicit `<PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.15" />` in `AUBDigitalAcademy.csproj` (`net10.0`), leaving EF Core, ASP.NET Core, Identity, and `Microsoft.Data.SqlClient` unchanged. Verified `dotnet list package --vulnerable --include-transitive` (0 vulnerabilities), `dotnet build --no-restore` (0 errors, 0 warnings), and all 9 regression suites (138/138 passed).

### P1 — Broken Workflows & Core Logic Bugs
1. ~~**Broken Password Reset (`TASK-P1-03`):**~~ **RESOLVED (2026-11-27).** Replaced the `AccountController.ForgotPassword` / `ResetPassword` stubs and `Api/AuthController.ForgotPassword` discarded-token/enumeration behavior with purpose-specific Telegram OTP password recovery (`RST:` prefix isolation in `TelegramVerificationRequests`, separate `AUB_PendingPasswordReset` TimeLimitedDataProtector cookie, `SendPasswordResetCodeAsync`, 10-minute OTP expiry, 5-attempt limit, 60s cross-identifier cooldown, server-side `UserManager.GeneratePasswordResetTokenAsync` + `ResetPasswordAsync` with 15-minute `TokenLifespan`, `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`, `sstamp` JWT invalidation across all `/api/*` endpoints, `PasswordResetPolicy` rate limiting, `[ResponseCache(NoStore = true)]`, and redacted logging). Verified via `--test-password-reset-security` (19/19 passed).
2. ~~**Secure Google Account Linking, Unlinking & Profile Security (`TASK-P1-02`):**~~ **RESOLVED (2026-11-27).** Implemented secure self-service Google account linking (`POST /Settings/StartGoogleLink`, `POST /Settings/LinkGoogle`, `GET /Settings/LinkGoogleCallback`) and unlinking (`POST /Settings/StartGoogleUnlink`, `POST /Settings/UnlinkGoogle`, `POST /Settings/CancelGoogleSecurityAction`, `POST /Settings/ResendGoogleStepUpCode`) via `SettingsController` and `IGoogleAuthService` / `GoogleAuthService`. Mandated step-up Telegram OTP verification for BOTH linking (`GLK:`) and unlinking (`GUL:`) delivered strictly to verified `TelegramChatId` (fail-closed on missing token/API error, zero simulation mode / fallback chat), protected by `TelegramStepUpPolicy` rate limiter (10 req/min/IP), 5 max attempts, 10-minute expiry, and 60-second cooldown. Enforced strict anti-takeover protections: purpose-bound `AubExternalNonce` (`link:` vs `login:`), rejection of client-provided provider keys, rejection of linking another user's Google account or linking a second Google account to the same user, rejection of unlinking when no local password exists, immediate `SecurityStamp` rotation on link/unlink, safe session re-issuance without exposing tokens/keys (`SaveTokens = false`), and authorized `/Profile` / `/Settings` UI with `NoStore` caching. Verified via `--test-google-linking-security` (42/42 passed).
3. **Broken Navigation Links (404 / 403):**
   - `Views/Account/Login.cshtml` "Activate Your Account" -> `/pages/authentication/activate-account.html` (404).
   - `TeacherService` `ManageUrl` -> `/Teacher/Courses/Details/{id}` (404; controller uses `/Teacher/CourseDetails/{id}`).
   - `_Layout.cshtml` `/Profile` (empty `SettingsController`), `/Teacher/Submissions` (fails without `assignmentId`), `/Assistance/Dashboard` (404 TA breadcrumb), `site.js` `Ctrl+K` search -> `/Admin/Users` (403 for non-Admins).
   - `Admin/Settings.cshtml` -> `/User/Profile` (404).
   - `Teacher/CourseDetails.cshtml` has no link to `/Academic/Curriculum/{id}`, and `Curriculum.cshtml` "Back to Courses" sends Teachers to Admin-only `/Academic/Course` (403).
3. **Progress Inflation & Assessment Bugs:** `GetStudentBookAsync` marks Books in `CourseProgresses`, inflating lesson completion >100%; `GetStudentQuizPlayerAsync` / `SubmitQuizAsync` ignore `Quiz.IsPublished`; quiz timer is client-side only; `TeacherService` excludes `Late` submissions from `PendingReviewsCount` and omits `IsLatest` in `GetTeacherGradesAsync`; `QuizQuestions.cshtml` lacks Edit Question UI; `Student/Dashboard.cshtml` hardcodes `"Spring 2026"`.

### P2 — Missing Features (Deferred & Phase 5–6)
- Categories, Majors, Semesters CRUD (`Views/Academic/{Category,Major,Semester,CourseItem}.cshtml` are 1-line stubs).
- `SettingsController` Profile & Change Password.
- **Phase 5:** Video watch tracking (>=90% anti-seek + resume; replace blind `CompleteLesson`), Sequential Access & Multi-Prerequisites, Student/Faculty Video Progress Dashboards, Lesson Discussions, In-App Notification Center, Global & Course Announcements.
- **Phase 6:** `LoginHistory`, real `Admin/Reports`, System Lookups (`ItemTypes`, `ViewAnswerType`), Final Grades/Transcripts (`Grades`), and resolution of `Admin/{Schedule,Payments,Invoices,Payroll}` placeholders.

---

## 4. Open Owner Decisions Before Phase 5 Build

Per [`docs/project.md`](./docs/project.md):
1. **Sequential Access & Non-Video Items (Sec. 8.2):** Confirm what counts as "completed" for `Book`, `Quiz`, and `Assignment` items when used as prerequisites.
2. **Lesson Discussion Peer Replies (Sec. 9):** Confirm whether students can reply to *any* student's thread or only to *their own* threads (plus faculty replies).

---

## 5. Next Recommended Action

All **Phase 1 (P0)** items (`TASK-P0-01` through `TASK-P0-06`) are complete. Proceed to **Phase 2 (P1)** in [`docs/DEVELOPMENT_ROADMAP.md`](./docs/DEVELOPMENT_ROADMAP.md) starting with **`TASK-P1-01`** (Fix broken navigation links and dead routes across portals).
