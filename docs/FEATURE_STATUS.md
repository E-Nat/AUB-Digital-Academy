# AUB Digital Academy — Feature Status Matrix

> **Verified Against Source Code:** 2026-11-26  
> **Reference Specification:** [`docs/project.md`](./project.md)  
> **Status Legend:**
> - **Implemented** — Functional end-to-end (UI + Controller + Service + DB)
> - **Partial** — Data/backend exists, or core works with bugs/missing sub-parts
> - **Broken Workflow** — Exists in code/UI but fails or skips critical logic
> - **UI-Backend Disconnect** — Implemented in backend or UI, not wired together
> - **Placeholder** — Static Razor view / stub class with no real backend logic
> - **Missing** — Not implemented yet

---

## 1. Public Portal & Shared Authentication

| Feature | Status | Evidence & Notes |
|---|---|---|
| Public Landing & Course Catalog (`/` / `Home/Index`) | **Implemented** | `HomeController.Index`, `Views/Home/Index.cshtml` displays real Categories, Majors, and Published Courses. |
| Cookie Login via University ID / Email (`Account/Login`) | **Implemented** | `AccountController.Login`, lockout + `IsActive` + mandatory Telegram 2FA. |
| "Activate Your Account" Link on Login Page | **Broken Workflow** | `Views/Account/Login.cshtml:164` links to `/pages/authentication/activate-account.html` (404) instead of `/Account/Register`. |
| Student Self-Registration (`Account/Register`) | **Broken Workflow** | `AccountController.Register` creates Student user, but calls `SendVerificationCodeAsync` with `null` `TelegramChatId` (falling back to global `Telegram:ChatId`/logs) and redirects to `VerifyTelegram` instead of `LinkTelegram`. |
| Telegram Account Linking (`@AUBLinkBot`, `/start link_`, `/link`) | **Implemented** | `TelegramService`, `TelegramPollingService`, `LinkTelegram.cshtml`. (`CheckTelegramLinkStatus` lacks pending-session cookie check.) |
| API JWT Login (`POST /api/Auth/login`) | **Broken Workflow** | `Controllers/Api/AuthController.cs` bypasses mandatory Telegram 2FA & lockout and uses a hardcoded fallback JWT secret. |
| Google OAuth SSO (`ExternalLoginCallback`) | **Broken Workflow** | `AccountController.cs:612-678` bypasses mandatory Telegram 2FA and sets `IsTelegramVerified = true` on new accounts; `GoogleAuthService` stub is empty. |
| Forgot / Reset Password (`Account/ForgotPassword`, `ResetPassword`) | **Broken Workflow** | `AccountController.ResetPassword` POST (`L573-587`) is a no-op stub; `ForgotPassword` only logs; `Api/AuthController.ForgotPassword` discards generated token. |
| User Profile & Change Password (`/Profile`, `SettingsController`) | **Placeholder / Broken** | `SettingsController.cs` is empty; `Views/Settings/{Profile,ChangePassword}.cshtml` are 1-line stubs; `_Layout.cshtml` `/Profile` and `Admin/Settings.cshtml` `/User/Profile` 404. |
| Topbar `Ctrl+K` Global Search & Notification Bell | **UI-Backend Disconnect / Placeholder** | `wwwroot/js/site.js:124` sends all roles to `/Admin/Users?search=...` (403 for non-Admins); notification bell in `_Layout.cshtml` is static empty state. |

---

## 2. Admin Features

| Feature | Status | Evidence & Notes |
|---|---|---|
| Executive Dashboard (`Admin/Index`) | **Implemented** | Real KPIs, Major/Semester charts, recent enrollments, quizzes, and activity feed (`AdminController.Index`). |
| User Management CRUD, Roles, Status Toggle, Password Reset | **Implemented** | `UserController.cs`, `UserService.cs`, `Views/User/*`. |
| Course Management (Create, Edit, Safe Delete) | **Implemented** | `AcademicController.cs`, `CourseService.cs`, `Views/Academic/Course.cshtml`. (Note: TA modal has unescaped `innerHTML` XSS risk.) |
| TA Assignment & `CanGrade` Toggle (`CourseAssistants`) | **Implemented** | `AcademicController.cs:1973-2132` (`[Authorize(Roles = "Admin")]`), `CanGrade` defaults to `false`. |
| Curriculum, Module, Lesson, Book, Quiz, Assignment CRUD | **Implemented** | `AcademicController.cs`, `CurriculumService.cs`, `Views/Academic/Curriculum.cshtml`. |
| Quiz Question Add / Delete | **Implemented** | `AcademicController.cs` + `Views/Academic/QuizQuestions.cshtml`. |
| Quiz Question Edit | **UI-Backend Disconnect** | `AcademicController.GetQuestionJson` & `EditQuestion` exist; `QuizQuestions.cshtml` has no Edit UI button/modal. |
| Categories, Majors, Semesters CRUD | **Partial (Data Only)** | Seeded in `SeedData.cs`; no controller CRUD actions; `Views/Academic/{Category,Major,Semester,CourseItem}.cshtml` are 1-line stubs. |
| Enrollment Management (Admin View & Course Roster) | **Implemented** | `EnrollmentController.cs`, `EnrollmentService.cs`, safe-unenroll blocking when learning activity exists. (Has N+1 activity queries.) |
| File Management (Audit, Download, Soft-Delete, Restore) | **Implemented** | `AdminController.Files`, `SoftDeleteFile`, `RestoreFile`, `DownloadAttachment` (`Views/Admin/Files.cshtml`). |
| Login History Audit (`LoginHistory`) | **Missing** | No entity, login hook, or Admin view exists. |
| Reports & Analytics (`Admin/Reports`) | **Placeholder** | `Views/Admin/Reports.cshtml` is a static card. |
| System Settings & Lookups (`ItemTypes`, `ViewAnswerType`) | **Placeholder** | `Views/Admin/Settings.cshtml` is a static card with a dead `/User/Profile` link; no lookup management UI exists. |
| Schedule, Payments, Invoices, Payroll (`Admin/*`) | **Placeholder** | Static Razor cards in `Views/Admin/{Schedule,Payments,Invoices,Payroll}.cshtml` with no models/services. |
| Global Announcements & Cross-Course Progress (Phase 5) | **Missing** | No `Announcement` or Phase 5 video progress entities/views exist. |

---

## 3. Teacher Features

| Feature | Status | Evidence & Notes |
|---|---|---|
| Shared Faculty Dashboard (`Teacher/Dashboard`) | **Partial (Broken Card Links)** | `TeacherController.Dashboard` & `TeacherService` work, but `ManageUrl` sets `/Teacher/Courses/Details/{id}` (404) and `PendingReviewsCount` omits `Late` submissions. |
| My Courses & Course Details / Roster (`Teacher/Courses`, `CourseDetails`) | **Implemented** | `TeacherController.Courses`, `CourseDetails` (`/Teacher/CourseDetails/{id}`) scoped by `TeacherId`. |
| Curriculum & Assessment Authoring Access | **UI-Backend Disconnect** | `AcademicController` allows Teachers to manage owned courses (`CanManageCourseAsync`), but `Teacher/CourseDetails.cshtml` has no link to `/Academic/Curriculum/{id}`, and `Curriculum.cshtml` "Back to Courses" links to Admin-only `/Academic/Course` (403). |
| Assignments, Quizzes & Books Overview | **Implemented (Missing Sidebar Links)** | `TeacherController.{Assignments,Quizzes,Books}` work, but `_Layout.cshtml` sidebar links to broken `/Teacher/Submissions` instead of `/Teacher/Assignments`, and omits `Quizzes`/`Books`. |
| Review & Grade Assignment Submissions (`Teacher/Submissions`) | **Implemented** | `TeacherService.GradeSubmissionAsync` enforces ownership, `IsLatest == true`, 0..MaxScore, and feedback. (`DownloadSubmission` legacy path skips `IsDeleted` check.) |
| Configure Quiz `MaxAttempts` | **Implemented** | Editable in `AcademicController.EditQuiz`; future-only rule enforced via `QuizSubmission.MaxAttemptsAllowed`. |
| Teacher Gradebook (`Teacher/Grades`) | **Partial (Logic Bugs)** | `TeacherService.GetTeacherGradesAsync` omits `IsLatest` filter on graded assignments and lists all quiz attempts instead of best score. |
| Phase 5 Video Progress Monitoring, Discussions & Course Announcements | **Missing** | Not implemented yet. |

---

## 4. Teaching Assistant (`Assistance`) Features

| Feature | Status | Evidence & Notes |
|---|---|---|
| Shared Faculty Dashboard & Assigned Course Scoping | **Implemented** | `TeacherService` filters strictly to `CourseAssistants` (`CanGrade` defaults `false`; `CS301` seeded `CanGrade=true` only in Dev/Test). |
| View Assigned Course Roster, Books, Quizzes, Assignments | **Implemented** | `TeacherController` + `EnrollmentController.CourseStudents` enforce `CourseAssistants` membership; unassigned courses return 403/`null`. |
| Grade Submissions (`CanGrade == true` only) | **Implemented** | `TeacherService.GradeSubmissionAsync` rejects `CanGrade == false`; UI (`Views/Teacher/Submissions.cshtml`) shows "View Only" badge. |
| Faculty Sidebar / Breadcrumb Navigation | **Broken Workflow** | `_Layout.cshtml:199` generates `/Assistance/Dashboard` (404) in breadcrumb; sidebar `/Teacher/Submissions` link fails without `assignmentId`; `/Profile` 404s. |
| Phase 5 Video Progress Monitoring & Discussion Moderation/Replies | **Missing** | Not implemented yet. |

---

## 5. Student Features

| Feature | Status | Evidence & Notes |
|---|---|---|
| Student Dashboard (`Student/Dashboard`) | **Partial** | Shows enrolled courses & completed counts (`StudentController.Dashboard`), but hardcodes `"Spring 2026"` for Current Term (`Views/Student/Dashboard.cshtml:70`). |
| My Enrolled Courses & Course Overview | **Implemented** | `StudentController.Courses`, `Course`, `EnrollmentController.MyCourses` enforce enrollment. |
| Lesson Player & Manual Completion | **Partial (Security/Phase 5 Gap)** | `StudentController.Lesson` renders YouTube/HTML5/link + `@Html.Raw(ContentHtml)` (XSS risk). `CompleteLesson` allows blind manual completion without video watch verification. |
| Book Reader (`Student/Book`) | **Partial (Progress Bug)** | Opens PDF/URL resources, but `GetStudentBookAsync` logs `CourseProgress` rows that inflate lesson completion percentage (`>100%`). |
| Quiz Taking & Result Breakdown | **Partial (Draft & Timer Bugs)** | `SubmitQuizAsync` enforces attempt limits, snapshots `MaxAttemptsAllowed`, computes best score, and reveals answers only when exhausted (`AreAttemptsExhausted`). However, `IsPublished` is not checked, `TimeLimitMinutes` is client-side only, and `Essay` type is unsupported. |
| Assignment Submission & Resubmission | **Implemented** | Up to 5 files (<=10 MB, PDF/ZIP/DOCX magic-byte validated, stored outside `wwwroot`), HTTP/HTTPS URL, text response, `Late` flagging without penalty, full attempt history preserved. |
| Student Gradebook (`Student/Grades`) | **Implemented** | Displays assignments and best quiz scores with 50% Pass/Fail status and course filter. |
| Final Course Grades / Transcripts (`Grades`/`tblGrades`) | **Missing** | No separate final course grade table or transcript view exists. |
| Phase 5 Video Watch Tracking (>=90% + Resume) | **Missing** | No `YT.Player` / `<video>` playback tracking or heartbeat API. |
| Phase 5 Sequential Access & Multi-Prerequisites | **Missing** | No `CourseAccessMode` or `LessonPrerequisite` enforcement. |
| Phase 5 Discussions, In-App Notifications & Announcements | **Missing** | Not implemented yet. |
