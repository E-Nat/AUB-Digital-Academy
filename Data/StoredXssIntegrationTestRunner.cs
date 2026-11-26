using System.Security.Claims;
using System.Text.Json;
using AUBDigitalAcademy.Controllers;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Implementations;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AUBDigitalAcademy.Data;

public static class StoredXssIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("======================================================================");
        Console.WriteLine("  TASK-P0-03: STORED XSS SECURITY HARDENING INTEGRATION TESTS");
        Console.WriteLine("======================================================================");

        int passed = 0;
        int failed = 0;
        var runTag = Guid.NewGuid().ToString("N")[..8];

        int? tempCourseId = null;
        int? tempChapterId = null;
        string? tempTeacherId = null;
        string? tempStudentId = null;
        string? tempTaId = null;
        string? tempAdminId = null;
        int? tempMajorId = null;
        int? tempSemesterId = null;
        int? tempCategoryId = null;

        void Assert(bool condition, string testName, string detail)
        {
            if (condition)
            {
                passed++;
                Console.WriteLine($"[PASS] {testName}: {detail}");
            }
            else
            {
                failed++;
                Console.WriteLine($"[FAIL] {testName}: {detail}");
            }
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var sp = scope.ServiceProvider;
            var sanitizer = sp.GetRequiredService<IHtmlSanitizationService>();
            var context = sp.GetRequiredService<ApplicationDbContext>();
            var curriculumService = sp.GetRequiredService<ICurriculumService>();
            var courseService = sp.GetRequiredService<ICourseService>();
            var userManager = sp.GetRequiredService<UserManager<User>>();
            var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
            var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

            // -----------------------------------------------------------------
            // TEST 1: <script>alert(1)</script> stripped completely
            // -----------------------------------------------------------------
            {
                var input = "<p>Intro notes</p><script>alert(1)</script><SCRIPT SRC='https://evil.com/x.js'></SCRIPT>";
                var output = sanitizer.SanitizeLessonHtml(input) ?? "";
                bool ok = output.Contains("<p>Intro notes</p>", StringComparison.Ordinal)
                          && !output.Contains("<script", StringComparison.OrdinalIgnoreCase)
                          && !output.Contains("alert(1)", StringComparison.OrdinalIgnoreCase);
                Assert(ok, "Test 1: Script Tag Removal", $"Sanitized output: '{output}'");
            }

            // -----------------------------------------------------------------
            // TEST 2: <img src=x onerror=alert(1)> and event handlers stripped
            // -----------------------------------------------------------------
            {
                var input = "<img src=\"/images/diagram.png\" onerror=\"alert(1)\" alt=\"Diagram\">" +
                            "<p onclick=\"alert(1)\" onmouseover=\"steal()\">Click me</p>" +
                            "<svg onload=\"alert(1)\"><circle r=\"10\"></circle></svg>" +
                            "<iframe src=\"https://evil.com\"></iframe>";
                var output = sanitizer.SanitizeLessonHtml(input) ?? "";
                bool ok = output.Contains("<img", StringComparison.OrdinalIgnoreCase)
                          && output.Contains("/images/diagram.png", StringComparison.Ordinal)
                          && output.Contains("Click me", StringComparison.Ordinal)
                          && !output.Contains("onerror", StringComparison.OrdinalIgnoreCase)
                          && !output.Contains("onclick", StringComparison.OrdinalIgnoreCase)
                          && !output.Contains("onmouseover", StringComparison.OrdinalIgnoreCase)
                          && !output.Contains("onload", StringComparison.OrdinalIgnoreCase)
                          && !output.Contains("<svg", StringComparison.OrdinalIgnoreCase)
                          && !output.Contains("<iframe", StringComparison.OrdinalIgnoreCase)
                          && !output.Contains("alert(1)", StringComparison.OrdinalIgnoreCase);
                Assert(ok, "Test 2: Event Handler & Active Element Stripping", $"Sanitized output: '{output}'");
            }

            // -----------------------------------------------------------------
            // TEST 3: Dangerous URL schemes rejected (javascript:, data:, vbscript:)
            // -----------------------------------------------------------------
            {
                var htmlInput = "<a href=\"javascript:alert(1)\">Bad1</a>" +
                                "<a href=\"  java&#x09;script:alert(1)\">Bad2</a>" +
                                "<a href=\"data:text/html,<script>alert(1)</script>\">Bad3</a>" +
                                "<a href=\"vbscript:msgbox(1)\">Bad4</a>" +
                                "<img src=\"data:image/svg+xml,<svg onload=alert(1)>\">" +
                                "<a href=\"https://aub.edu.kh\" target=\"_blank\">Good</a>";
                var output = sanitizer.SanitizeLessonHtml(htmlInput) ?? "";
                bool ok = !output.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
                          && !output.Contains("data:", StringComparison.OrdinalIgnoreCase)
                          && !output.Contains("vbscript:", StringComparison.OrdinalIgnoreCase)
                          && output.Contains("https://aub.edu.kh", StringComparison.Ordinal)
                          && output.Contains("noopener", StringComparison.OrdinalIgnoreCase)
                          && output.Contains("noreferrer", StringComparison.OrdinalIgnoreCase)
                          && sanitizer.SanitizeResourceUrl("javascript:alert(1)") == null
                          && sanitizer.SanitizeResourceUrl("  java&#x09;script:alert(1)") == null
                          && sanitizer.SanitizeResourceUrl("data:text/html,<script>alert(1)</script>") == null
                          && sanitizer.SanitizeResourceUrl("//evil.com/payload.js") == null
                          && sanitizer.SanitizeHttpUrl("javascript:alert(1)") == null;
                Assert(ok, "Test 3: Dangerous URL Scheme Rejection", $"Sanitized output: '{output}'");
            }

            // -----------------------------------------------------------------
            // TEST 4: Legitimate educational HTML formatting preserved
            // -----------------------------------------------------------------
            {
                var legitHtml = "<h2>Module Overview</h2>" +
                                "<p>Read <strong>Chapter 1</strong> and <em>practice</em> <u>carefully</u>.</p>" +
                                "<blockquote>Knowledge is structured data.</blockquote>" +
                                "<ul><li>Item A</li><li>Item B</li></ul>" +
                                "<pre><code>Console.WriteLine(\"Hello\");</code></pre>" +
                                "<table><thead><tr><th>Topic</th><th>Hours</th></tr></thead><tbody><tr><td>XSS</td><td>2</td></tr></tbody></table>" +
                                "<p><a href=\"https://aub.edu.kh/syllabus\">Syllabus</a></p>" +
                                "<img src=\"/images/course_webdev.jpg\" alt=\"Architecture\">";
                var output = sanitizer.SanitizeLessonHtml(legitHtml) ?? "";
                bool ok = output.Contains("<h2>Module Overview</h2>", StringComparison.Ordinal)
                          && output.Contains("<strong>Chapter 1</strong>", StringComparison.Ordinal)
                          && output.Contains("<em>practice</em>", StringComparison.Ordinal)
                          && output.Contains("<u>carefully</u>", StringComparison.Ordinal)
                          && output.Contains("<blockquote>Knowledge is structured data.</blockquote>", StringComparison.Ordinal)
                          && output.Contains("<ul><li>Item A</li><li>Item B</li></ul>", StringComparison.Ordinal)
                          && output.Contains("<pre><code>", StringComparison.Ordinal)
                          && output.Contains("<table>", StringComparison.Ordinal)
                          && output.Contains("href=\"https://aub.edu.kh/syllabus\"", StringComparison.Ordinal)
                          && output.Contains("src=\"/images/course_webdev.jpg\"", StringComparison.Ordinal);
                Assert(ok, "Test 4: Legitimate Educational Formatting Preserved", "All safe tags, tables, code blocks, links, and images preserved.");
            }

            // Setup isolated SQL Server fixtures for end-to-end persistence & rendering checks
            var category = new Category { Name = $"XSS_Cat_{runTag}" };
            context.Categories.Add(category);
            await context.SaveChangesAsync();
            tempCategoryId = category.Id;

            var major = new Major { Code = $"X{runTag[..4]}".ToUpper(), Name = $"XSS Major {runTag}", CategoryId = category.Id };
            context.Majors.Add(major);
            await context.SaveChangesAsync();
            tempMajorId = major.Id;

            var semester = new Semester
            {
                Code = $"XS{runTag[..4]}".ToUpper(),
                Name = $"XSS Semester {runTag}",
                StartDate = DateTime.UtcNow.AddDays(-10),
                EndDate = DateTime.UtcNow.AddDays(60),
                IsCurrent = false
            };
            context.Semesters.Add(semester);
            await context.SaveChangesAsync();
            tempSemesterId = semester.Id;

            async Task<User> CreateTempUserAsync(string prefix, string fullName, string email, string uniId, string role)
            {
                var u = new User
                {
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    UniversityId = uniId,
                    IsActive = true,
                    IsTelegramVerified = true,
                    EmailConfirmed = true
                };
                var res = await userManager.CreateAsync(u, "Test@12345");
                if (!res.Succeeded)
                {
                    throw new InvalidOperationException($"Failed creating test user {prefix}: {string.Join(", ", res.Errors.Select(e => e.Description))}");
                }
                await userManager.AddToRoleAsync(u, role);
                return u;
            }

            var teacher = await CreateTempUserAsync("teacher", "Dr. XSS Test Teacher", $"xss_teacher_{runTag}@test.local", $"XT{runTag[..5]}", "Teacher");
            tempTeacherId = teacher.Id;

            var student = await CreateTempUserAsync("student", "XSS Test Student", $"xss_student_{runTag}@test.local", $"XS{runTag[..5]}", "Student");
            tempStudentId = student.Id;

            var admin = await CreateTempUserAsync("admin", "XSS Test Admin", $"xss_admin_{runTag}@test.local", $"XA{runTag[..5]}", "Admin");
            tempAdminId = admin.Id;

            // Malicious payload inside TA's name, email, and universityId (<=20 chars) to test TA modal JSON & safe DOM rendering
            var evilTaName = "TA <img src=x onerror=alert('XSS_TA_NAME')> Smith";
            var evilTaEmail = $"ta_xss_{runTag}@test.local";
            var evilTaUniId = $"<img/src=x>{runTag[..4]}";
            var taUser = await CreateTempUserAsync("ta", evilTaName, evilTaEmail, evilTaUniId, "Assistance");
            tempTaId = taUser.Id;

            var course = await courseService.CreateCourseAsync(new Course
            {
                Code = $"XSS{runTag[..5]}".ToUpper(),
                Title = "XSS Security Verification Course",
                Description = "Course for verifying Stored XSS hardening.",
                Status = CourseStatus.Published,
                MajorId = major.Id,
                SemesterId = semester.Id,
                TeacherId = teacher.Id,
                Thumbnail = "assets/images/course_webdev.jpg"
            });
            tempCourseId = course.Id;

            context.Enrollments.Add(new Enrollment
            {
                CourseId = course.Id,
                StudentId = student.Id,
                EnrolledAt = DateTime.UtcNow
            });
            context.CourseAssistants.Add(new CourseAssistant
            {
                CourseId = course.Id,
                AssistantId = taUser.Id,
                CanGrade = false,
                AssignedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var chapterRes = await curriculumService.CreateChapterAsync(course.Id, "Module 1: Sanitization", 1);
            tempChapterId = chapterRes.EntityId;
            if (!chapterRes.IsSuccess || !tempChapterId.HasValue)
            {
                throw new InvalidOperationException("Failed to create test module.");
            }

            // -----------------------------------------------------------------
            // TEST 5: Malicious HTML in lesson create/update sanitized at write boundary
            // -----------------------------------------------------------------
            {
                var maliciousCreateHtml = "<p>Welcome</p><script>alert('create')</script><img src=x onerror=alert(1)>";
                var createLessonRes = await curriculumService.CreateLessonAsync(
                    tempChapterId.Value,
                    "Lesson 1: Sanitized Write",
                    maliciousCreateHtml,
                    "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                    1,
                    course.Id);

                var createdItemId = createLessonRes.EntityId!.Value;
                var dbLessonAfterCreate = await context.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.CourseItemId == createdItemId);

                var badVideoUpdate = await curriculumService.UpdateLessonAsync(
                    createdItemId,
                    "Lesson 1: Sanitized Write",
                    "<p>Updated</p><a href=\"javascript:alert(1)\" onclick=\"alert(2)\">BadLink</a>",
                    "javascript:alert(1)",
                    1,
                    tempChapterId.Value,
                    course.Id);

                var goodUpdate = await curriculumService.UpdateLessonAsync(
                    createdItemId,
                    "Lesson 1: Sanitized Write",
                    "<p>Updated <strong>content</strong></p><a href=\"javascript:alert(1)\" onclick=\"alert(2)\">BadLink</a>",
                    "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                    1,
                    tempChapterId.Value,
                    course.Id);

                var dbLessonAfterUpdate = await context.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.CourseItemId == createdItemId);

                bool ok = createLessonRes.IsSuccess
                          && dbLessonAfterCreate?.ContentHtml != null
                          && !dbLessonAfterCreate.ContentHtml.Contains("<script", StringComparison.OrdinalIgnoreCase)
                          && !dbLessonAfterCreate.ContentHtml.Contains("onerror", StringComparison.OrdinalIgnoreCase)
                          && !badVideoUpdate.IsSuccess
                          && goodUpdate.IsSuccess
                          && dbLessonAfterUpdate?.ContentHtml != null
                          && dbLessonAfterUpdate.ContentHtml.Contains("<strong>content</strong>", StringComparison.Ordinal)
                          && !dbLessonAfterUpdate.ContentHtml.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
                          && !dbLessonAfterUpdate.ContentHtml.Contains("onclick", StringComparison.OrdinalIgnoreCase);

                Assert(ok, "Test 5: Lesson Write Boundary Sanitization", $"Create & Update sanitized DB HTML; rejected bad VideoUrl='{badVideoUpdate.Message}'.");
            }

            // -----------------------------------------------------------------
            // TEST 6: Legacy unsanitized DB lesson row safely handled at read & render time
            // -----------------------------------------------------------------
            {
                var legacyCourseItem = new CourseItem
                {
                    ChapterId = tempChapterId.Value,
                    Title = "Lesson 2: Legacy Unsanitized Row",
                    Position = 2,
                    Type = ItemType.Lesson
                };
                context.CourseItems.Add(legacyCourseItem);
                await context.SaveChangesAsync();

                var legacyLesson = new Lesson
                {
                    CourseItemId = legacyCourseItem.Id,
                    ContentHtml = "<p>Legacy safe paragraph</p><script>alert('legacy')</script><img src=x onerror=alert(1)><a href=\"javascript:alert(1)\">click</a>",
                    VideoUrl = "javascript:alert('video')"
                };
                context.Lessons.Add(legacyLesson);
                await context.SaveChangesAsync();

                var playerVm = await curriculumService.GetStudentLessonPlayerAsync(course.Id, legacyCourseItem.Id, student.Id);
                var renderedFromDb = playerVm != null
                    ? await RenderViewAsync(sp, viewEngine, tempDataProvider, "~/Views/Student/Lesson.cshtml", playerVm)
                    : string.Empty;

                // Defense-in-depth check: even if a caller passes unsanitized ContentHtml directly to Lesson.cshtml
                var rawBypassVm = new StudentLessonPlayerViewModel
                {
                    CourseId = course.Id,
                    CourseCode = course.Code,
                    CourseTitle = course.Title,
                    ModuleId = tempChapterId.Value,
                    ModuleTitle = "Module 1",
                    ModulePosition = 1,
                    CourseItemId = legacyCourseItem.Id,
                    LessonId = legacyLesson.Id,
                    LessonTitle = "Direct View Test",
                    LessonPosition = 2,
                    ContentHtml = "<p>Direct</p><script>alert('direct')</script><img src=x onerror=alert(99)>",
                    VideoUrl = "javascript:alert('direct_video')"
                };
                var renderedFromRawVm = await RenderViewAsync(sp, viewEngine, tempDataProvider, "~/Views/Student/Lesson.cshtml", rawBypassVm);

                bool ok = playerVm != null
                          && playerVm.VideoUrl == null
                          && (playerVm.ContentHtml ?? "").Contains("<p>Legacy safe paragraph</p>", StringComparison.Ordinal)
                          && !(playerVm.ContentHtml ?? "").Contains("<script", StringComparison.OrdinalIgnoreCase)
                          && !(playerVm.ContentHtml ?? "").Contains("onerror", StringComparison.OrdinalIgnoreCase)
                          && !(playerVm.ContentHtml ?? "").Contains("javascript:", StringComparison.OrdinalIgnoreCase)
                          && renderedFromDb.Contains("<p>Legacy safe paragraph</p>", StringComparison.Ordinal)
                          && !renderedFromDb.Contains("alert('legacy')", StringComparison.OrdinalIgnoreCase)
                          && !renderedFromDb.Contains("onerror=alert(1)", StringComparison.OrdinalIgnoreCase)
                          && !renderedFromDb.Contains("javascript:alert", StringComparison.OrdinalIgnoreCase)
                          && renderedFromRawVm.Contains("<p>Direct</p>", StringComparison.Ordinal)
                          && !renderedFromRawVm.Contains("alert('direct')", StringComparison.OrdinalIgnoreCase)
                          && !renderedFromRawVm.Contains("onerror=alert(99)", StringComparison.OrdinalIgnoreCase)
                          && !renderedFromRawVm.Contains("javascript:alert('direct_video')", StringComparison.OrdinalIgnoreCase);

                Assert(ok, "Test 6: Legacy Lesson DB & View Defense-in-Depth", "Legacy DB payload and direct raw ViewModel were sanitized before rendering.");
            }

            // -----------------------------------------------------------------
            // TEST 7: Book.FileUrl & Course.Thumbnail URL protocol validation + legacy Book rendering
            // -----------------------------------------------------------------
            {
                var badBookCreate = await curriculumService.CreateBookAsync(new BookCreateViewModel
                {
                    ChapterId = tempChapterId.Value,
                    CourseId = course.Id,
                    Title = "Evil Book",
                    FileUrl = "javascript:alert(1)",
                    FileType = "PDF"
                });

                bool badThumbRejected = false;
                try
                {
                    await courseService.CreateCourseAsync(new Course
                    {
                        Code = $"BAD{runTag[..4]}".ToUpper(),
                        Title = "Evil Thumb Course",
                        MajorId = major.Id,
                        SemesterId = semester.Id,
                        TeacherId = teacher.Id,
                        Thumbnail = "javascript:alert(1)"
                    });
                }
                catch (ArgumentException)
                {
                    badThumbRejected = true;
                }

                // Legacy Book DB row with dangerous FileUrl
                var legacyBookItem = new CourseItem
                {
                    ChapterId = tempChapterId.Value,
                    Title = "Legacy Unsafe Book",
                    Position = 3,
                    Type = ItemType.Book
                };
                context.CourseItems.Add(legacyBookItem);
                await context.SaveChangesAsync();
                context.Books.Add(new Book
                {
                    CourseItemId = legacyBookItem.Id,
                    FileUrl = "javascript:alert('book')",
                    FileType = "PDF"
                });
                await context.SaveChangesAsync();

                var bookVm = await curriculumService.GetStudentBookAsync(course.Id, legacyBookItem.Id, student.Id);
                var renderedBookHtml = bookVm != null
                    ? await RenderViewAsync(sp, viewEngine, tempDataProvider, "~/Views/Student/Book.cshtml", bookVm)
                    : string.Empty;

                bool ok = !badBookCreate.IsSuccess
                          && badThumbRejected
                          && bookVm != null
                          && string.IsNullOrEmpty(bookVm.FileUrl)
                          && !renderedBookHtml.Contains("javascript:alert", StringComparison.OrdinalIgnoreCase);

                Assert(ok, "Test 7: Book & Course Thumbnail URL Protection", "Rejected javascript: URLs on write and stripped legacy Book.FileUrl on read/render.");
            }

            // -----------------------------------------------------------------
            // TEST 8: Assignment ExternalUrl protocol validation
            // -----------------------------------------------------------------
            {
                var assignRes = await curriculumService.CreateAssignmentAsync(new AssignmentCreateViewModel
                {
                    ChapterId = tempChapterId.Value,
                    CourseId = course.Id,
                    Title = "XSS Assignment Check",
                    Instruction = "Submit a valid repo link.",
                    MaxScore = 100,
                    DueDate = DateTime.UtcNow.AddDays(5)
                });

                var assignItemId = assignRes.EntityId!.Value;
                var badSubmit = await curriculumService.SubmitAssignmentAsync(course.Id, assignItemId, student.Id, null, "javascript:alert(1)", null);
                var badDataSubmit = await curriculumService.SubmitAssignmentAsync(course.Id, assignItemId, student.Id, null, "data:text/html,<script>alert(1)</script>", null);
                var goodSubmit = await curriculumService.SubmitAssignmentAsync(course.Id, assignItemId, student.Id, null, "https://github.com/aub/project", null);

                bool ok = assignRes.IsSuccess
                          && !badSubmit.Result.IsSuccess
                          && !badDataSubmit.Result.IsSuccess
                          && goodSubmit.Result.IsSuccess;
                Assert(ok, "Test 8: Assignment ExternalUrl Validation", "Rejected javascript:/data: URLs and accepted valid https:// URL.");
            }

            // -----------------------------------------------------------------
            // TEST 9: TA-generated content & Academic/Course.cshtml safe DOM APIs
            // -----------------------------------------------------------------
            {
                var academicLogger = sp.GetRequiredService<ILogger<AcademicController>>();
                var academicController = new AcademicController(courseService, context, userManager, academicLogger, curriculumService, sanitizer);
                var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, admin.Id),
                    new Claim(ClaimTypes.Name, admin.UserName!),
                    new Claim(ClaimTypes.Role, "Admin")
                ], "TestAuth"));
                academicController.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = adminPrincipal, RequestServices = sp }
                };

                var jsonResult = await academicController.GetCourseAssistantsJson(course.Id) as JsonResult;
                var serializedJson = jsonResult != null ? JsonSerializer.Serialize(jsonResult.Value) : string.Empty;

                // Inspect Views/Academic/Course.cshtml to verify TA rendering uses safe DOM APIs (textContent / createTextNode / replaceChildren) instead of innerHTML concatenation
                var courseViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Academic", "Course.cshtml");
                var courseViewSource = await File.ReadAllTextAsync(courseViewPath);

                bool usesSafeDomApis = courseViewSource.Contains("function createAssistantRow(courseId, asst)", StringComparison.Ordinal)
                                       && courseViewSource.Contains("nameDiv.textContent = asst.name", StringComparison.Ordinal)
                                       && courseViewSource.Contains("emailDiv.textContent = asst.email", StringComparison.Ordinal)
                                       && courseViewSource.Contains("uniBadge.textContent = asst.universityId", StringComparison.Ordinal)
                                       && courseViewSource.Contains("tableBody.replaceChildren(fragment)", StringComparison.Ordinal)
                                       && !courseViewSource.Contains("tableBody.innerHTML = html", StringComparison.Ordinal)
                                       && !courseViewSource.Contains("'<div class=\"fw-bold text-dark\">' + asst.name", StringComparison.Ordinal);

                // Also verify Course.cshtml view rendering handles malicious Thumbnail / Title safely
                var courseVm = new CourseManagementViewModel
                {
                    Courses =
                    [
                        new Course
                        {
                            Id = course.Id,
                            Code = course.Code,
                            Title = "Course <script>alert('title')</script>",
                            Thumbnail = "javascript:alert('thumb')",
                            Status = CourseStatus.Published,
                            Teacher = teacher,
                            Major = major,
                            Semester = semester
                        }
                    ]
                };
                var renderedCourseHtml = await RenderViewAsync(sp, viewEngine, tempDataProvider, "~/Views/Academic/Course.cshtml", courseVm);

                bool ok = serializedJson.Contains("XSS_TA_NAME", StringComparison.Ordinal)
                          && usesSafeDomApis
                          && !renderedCourseHtml.Contains("javascript:alert('thumb')", StringComparison.OrdinalIgnoreCase)
                          && !renderedCourseHtml.Contains("<script>alert('title')</script>", StringComparison.Ordinal);

                Assert(ok, "Test 9: TA Output & Course.cshtml Safe DOM Rendering", "TA table uses createElement + textContent + replaceChildren; malicious course title encoded and unsafe thumbnail dropped.");
            }

            // -----------------------------------------------------------------
            // TEST 10: Normal lesson & course rendering preserves legitimate functionality
            // -----------------------------------------------------------------
            {
                var normalLessonRes = await curriculumService.CreateLessonAsync(
                    tempChapterId.Value,
                    "Lesson 3: Normal Educational Content",
                    "<h3>Clean Architecture</h3><p>Separation of concerns with <strong>ASP.NET Core</strong>.</p>",
                    "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                    4,
                    course.Id);

                var normalVm = await curriculumService.GetStudentLessonPlayerAsync(course.Id, normalLessonRes.EntityId!.Value, student.Id);
                var renderedNormal = normalVm != null
                    ? await RenderViewAsync(sp, viewEngine, tempDataProvider, "~/Views/Student/Lesson.cshtml", normalVm)
                    : string.Empty;

                bool ok = normalLessonRes.IsSuccess
                          && normalVm != null
                          && renderedNormal.Contains("<h3>Clean Architecture</h3>", StringComparison.Ordinal)
                          && renderedNormal.Contains("<strong>ASP.NET Core</strong>", StringComparison.Ordinal)
                          && renderedNormal.Contains("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", StringComparison.Ordinal);

                Assert(ok, "Test 10: Normal Lesson & Course Rendering", "Legitimate lesson HTML and YouTube embed rendered intact.");
            }
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"[FAIL] Unhandled exception in StoredXssIntegrationTestRunner: {ex}");
        }
        finally
        {
            // Cleanup all temporary records in reverse dependency order without touching real user data
            try
            {
                using var cleanupScope = serviceProvider.CreateScope();
                var db = cleanupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                if (tempCourseId.HasValue)
                {
                    var cid = tempCourseId.Value;

                    var attachments = await db.AssignmentAttachments
                        .Where(a => a.Submission.Assignment.CourseItem.Chapter.CourseId == cid)
                        .ToListAsync();
                    if (attachments.Any()) db.AssignmentAttachments.RemoveRange(attachments);

                    var assignSubs = await db.AssignmentSubmissions
                        .Where(s => s.Assignment.CourseItem.Chapter.CourseId == cid)
                        .ToListAsync();
                    if (assignSubs.Any()) db.AssignmentSubmissions.RemoveRange(assignSubs);

                    var progresses = await db.CourseProgresses
                        .Where(p => p.CourseItem.Chapter.CourseId == cid)
                        .ToListAsync();
                    if (progresses.Any()) db.CourseProgresses.RemoveRange(progresses);

                    var enrollments = await db.Enrollments.Where(e => e.CourseId == cid).ToListAsync();
                    if (enrollments.Any()) db.Enrollments.RemoveRange(enrollments);

                    var assistants = await db.CourseAssistants.Where(ca => ca.CourseId == cid).ToListAsync();
                    if (assistants.Any()) db.CourseAssistants.RemoveRange(assistants);

                    var books = await db.Books.Where(b => b.CourseItem.Chapter.CourseId == cid).ToListAsync();
                    if (books.Any()) db.Books.RemoveRange(books);

                    var lessons = await db.Lessons.Where(l => l.CourseItem.Chapter.CourseId == cid).ToListAsync();
                    if (lessons.Any()) db.Lessons.RemoveRange(lessons);

                    var assignments = await db.Assignments.Where(a => a.CourseItem.Chapter.CourseId == cid).ToListAsync();
                    if (assignments.Any()) db.Assignments.RemoveRange(assignments);

                    var items = await db.CourseItems.Where(ci => ci.Chapter.CourseId == cid).ToListAsync();
                    if (items.Any()) db.CourseItems.RemoveRange(items);

                    var chapters = await db.Chapters.Where(ch => ch.CourseId == cid).ToListAsync();
                    if (chapters.Any()) db.Chapters.RemoveRange(chapters);

                    var course = await db.Courses.FindAsync(cid);
                    if (course != null) db.Courses.Remove(course);

                    await db.SaveChangesAsync();
                }

                foreach (var uid in new[] { tempTaId, tempStudentId, tempTeacherId, tempAdminId })
                {
                    if (!string.IsNullOrEmpty(uid))
                    {
                        var user = await db.Users.FindAsync(uid);
                        if (user != null) db.Users.Remove(user);
                    }
                }
                await db.SaveChangesAsync();

                if (tempMajorId.HasValue)
                {
                    var m = await db.Majors.FindAsync(tempMajorId.Value);
                    if (m != null) db.Majors.Remove(m);
                }
                if (tempSemesterId.HasValue)
                {
                    var s = await db.Semesters.FindAsync(tempSemesterId.Value);
                    if (s != null) db.Semesters.Remove(s);
                }
                if (tempCategoryId.HasValue)
                {
                    var c = await db.Categories.FindAsync(tempCategoryId.Value);
                    if (c != null) db.Categories.Remove(c);
                }
                await db.SaveChangesAsync();
            }
            catch (Exception cleanupEx)
            {
                Console.WriteLine($"[WARN] Cleanup error in StoredXssIntegrationTestRunner: {cleanupEx.Message}");
            }
        }

        Console.WriteLine("----------------------------------------------------------------------");
        Console.WriteLine($"  SUMMARY: {passed} PASSED, {failed} FAILED (Total {passed + failed})");
        Console.WriteLine("======================================================================");

        if (failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }

    private sealed class NoopRouter : IRouter
    {
        public VirtualPathData? GetVirtualPath(VirtualPathContext context) => new VirtualPathData(this, "/");
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;
    }

    private static async Task<string> RenderViewAsync<TModel>(
        IServiceProvider serviceProvider,
        ICompositeViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        string viewPath,
        TModel model)
    {
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        var routeData = new RouteData();
        routeData.Routers.Add(new NoopRouter());
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var viewResult = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        if (!viewResult.Success)
        {
            throw new InvalidOperationException($"Could not locate Razor view '{viewPath}'.");
        }

        await using var sw = new StringWriter();
        var viewData = new ViewDataDictionary<TModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            tempData,
            sw,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return sw.ToString();
    }
}
