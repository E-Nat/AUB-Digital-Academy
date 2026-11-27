using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Controllers;

[Authorize(Roles = "Student,Admin")]
public class StudentController : Controller
{
    private readonly IEnrollmentService _enrollmentService;
    private readonly ICourseService _courseService;
    private readonly ICurriculumService _curriculumService;
    private readonly IAssignmentStorageService _assignmentStorageService;
    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<StudentController> _logger;
    private readonly IHtmlSanitizationService _htmlSanitizer;
    private readonly IMemoryCache? _memoryCache;
    private readonly IVideoStorageService? _videoStorageService;

    public StudentController(
        IEnrollmentService enrollmentService,
        ICourseService courseService,
        UserManager<User> userManager,
        ApplicationDbContext context,
        ILogger<StudentController> logger)
        : this(
            enrollmentService,
            courseService,
            new AUBDigitalAcademy.Services.Implementations.CurriculumService(context, new LoggerFactory().CreateLogger<AUBDigitalAcademy.Services.Implementations.CurriculumService>()),
            null!,
            userManager,
            context,
            logger)
    {
    }

    public StudentController(
        IEnrollmentService enrollmentService,
        ICourseService courseService,
        ICurriculumService curriculumService,
        UserManager<User> userManager,
        ApplicationDbContext context,
        ILogger<StudentController> logger)
        : this(
            enrollmentService,
            courseService,
            curriculumService,
            null!,
            userManager,
            context,
            logger)
    {
    }

    [ActivatorUtilitiesConstructor]
    public StudentController(
        IEnrollmentService enrollmentService,
        ICourseService courseService,
        ICurriculumService curriculumService,
        IAssignmentStorageService assignmentStorageService,
        UserManager<User> userManager,
        ApplicationDbContext context,
        ILogger<StudentController> logger,
        IHtmlSanitizationService? htmlSanitizer = null,
        IMemoryCache? memoryCache = null,
        IVideoStorageService? videoStorageService = null)
    {
        _enrollmentService = enrollmentService;
        _courseService = courseService;
        _curriculumService = curriculumService;
        _assignmentStorageService = assignmentStorageService;
        _userManager = userManager;
        _context = context;
        _logger = logger;
        _htmlSanitizer = htmlSanitizer ?? new AUBDigitalAcademy.Services.Implementations.HtmlSanitizationService();
        _memoryCache = memoryCache;
        _videoStorageService = videoStorageService;
    }

    /// <summary>
    /// Student Dashboard: Overview of academic enrollment, term stats, and quick links to enrolled courses.
    /// </summary>
    [HttpGet]
    [Route("Student")]
    [Route("Student/Dashboard")]
    [Route("Student/Index")]
    public async Task<IActionResult> Dashboard()
    {
        ViewData["Title"] = "Student Dashboard";
        ViewData["ActivePage"] = "Dashboard";

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var student = await _userManager.FindByIdAsync(currentUserId);
        if (student == null)
        {
            return NotFound("Student profile not found.");
        }

        var enrollments = await _enrollmentService.GetStudentEnrollmentsAsync(currentUserId);
        var courseIds = enrollments.Select(e => e.CourseId).ToList();
        var progressMap = await _curriculumService.GetStudentCoursesProgressAsync(currentUserId, courseIds);
        var totalCompletedLessons = await _curriculumService.GetStudentCompletedLessonsCountAsync(currentUserId);

        var enrolledList = enrollments.Select(e =>
        {
            var (total, completed) = progressMap.GetValueOrDefault(e.CourseId, (0, 0));
            return new StudentEnrollmentItemViewModel
            {
                EnrollmentId = e.Id,
                CourseId = e.CourseId,
                CourseCode = e.Course.Code,
                CourseTitle = e.Course.Title,
                Description = e.Course.Description,
                Thumbnail = e.Course.Thumbnail,
                Status = e.Course.Status,
                TeacherName = e.Course.Teacher?.FullName ?? "Instructor",
                MajorName = e.Course.Major?.Name ?? "General",
                SemesterName = e.Course.Semester?.Name ?? "Current Term",
                EnrolledAt = e.EnrolledAt,
                TotalLessons = total,
                CompletedLessons = completed,
                HasLearningActivity = completed > 0
            };
        }).ToList();

        var viewModel = new StudentDashboardViewModel
        {
            StudentId = student.Id,
            StudentName = student.FullName,
            UniversityId = student.UniversityId,
            StudentEmail = student.Email ?? string.Empty,
            Avatar = student.Avatar,
            CompletedLessonsCount = totalCompletedLessons,
            EnrolledCourses = enrolledList
        };

        return View(viewModel);
    }

    /// <summary>
    /// My Courses: Lists all courses the authenticated student is actively enrolled in.
    /// Strictly filters by authenticated Student ID and displays real course progress.
    /// </summary>
    [HttpGet]
    [Route("Student/Courses")]
    [Route("Student/MyCourses")]
    public async Task<IActionResult> Courses()
    {
        ViewData["Title"] = "My Enrolled Courses";
        ViewData["ActivePage"] = "Courses";

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var student = await _userManager.FindByIdAsync(currentUserId);
        if (student == null)
        {
            return NotFound("Student profile not found.");
        }

        var enrollments = await _enrollmentService.GetStudentEnrollmentsAsync(currentUserId);
        var courseIds = enrollments.Select(e => e.CourseId).ToList();
        var progressMap = await _curriculumService.GetStudentCoursesProgressAsync(currentUserId, courseIds);

        var courseItems = enrollments.Select(e =>
        {
            var (total, completed) = progressMap.GetValueOrDefault(e.CourseId, (0, 0));
            return new StudentEnrollmentItemViewModel
            {
                EnrollmentId = e.Id,
                CourseId = e.CourseId,
                CourseCode = e.Course.Code,
                CourseTitle = e.Course.Title,
                Description = e.Course.Description,
                Thumbnail = e.Course.Thumbnail,
                Status = e.Course.Status,
                TeacherName = e.Course.Teacher?.FullName ?? "Instructor",
                MajorName = e.Course.Major?.Name ?? "General",
                SemesterName = e.Course.Semester?.Name ?? "Current Term",
                EnrolledAt = e.EnrolledAt,
                TotalLessons = total,
                CompletedLessons = completed,
                HasLearningActivity = completed > 0
            };
        }).ToList();

        var viewModel = new StudentMyCoursesViewModel
        {
            StudentId = student.Id,
            StudentName = student.FullName,
            UniversityId = student.UniversityId,
            Courses = courseItems
        };

        return View("MyCourses", viewModel);
    }

    /// <summary>
    /// Alias for Courses action.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> MyCourses()
    {
        return await Courses();
    }

    /// <summary>
    /// Open Course / Course Overview:
    /// Server-side access security: Strictly verifies that the authenticated student is enrolled in the course.
    /// If not enrolled and not admin: Denies access (HTTP 403 / Forbid).
    /// Displays real lesson completion checkmarks and overall course progress percentage.
    /// </summary>
    [HttpGet]
    [Route("Student/Course/{id:int}")]
    [Route("Student/CourseOverview/{id:int}")]
    public async Task<IActionResult> Course(int id)
    {
        if (id <= 0)
        {
            return NotFound("Invalid course ID.");
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");

        // 1. Server-side Enrollment Security Check
        var isEnrolled = await _enrollmentService.IsStudentEnrolledAsync(currentUserId, id);
        if (!isEnrolled && !isAdmin)
        {
            _logger.LogWarning("Course access denied: Student '{StudentId}' is not enrolled in Course ID {CourseId}.", currentUserId, id);
            return Forbid();
        }

        // 2. Load Course and Curriculum Overview from SQL Server
        var course = await _context.Courses
            .AsNoTracking()
            .Include(c => c.Teacher)
            .Include(c => c.Major)
            .Include(c => c.Semester)
            .Include(c => c.Chapters.OrderBy(ch => ch.Position))
                .ThenInclude(ch => ch.CourseItems.OrderBy(ci => ci.Position))
                    .ThenInclude(ci => ci.Lesson)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Quiz)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Assignment)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Book)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (course == null)
        {
            return NotFound("Course not found.");
        }

        // Get enrollment timestamp
        var enrollment = await _enrollmentService.GetEnrollmentAsync(currentUserId, id);
        var enrolledAt = enrollment?.EnrolledAt ?? DateTime.UtcNow;

        // Query real student completion data for this course
        var completedItemIds = await _context.CourseProgresses
            .AsNoTracking()
            .Where(cp => cp.StudentId == currentUserId && cp.IsCompleted && cp.CourseItem.Chapter.CourseId == id)
            .Select(cp => cp.CourseItemId)
            .ToListAsync();
        var completedSet = new HashSet<int>(completedItemIds);

        // Query student quiz submissions for this course
        var quizSubmissions = await _context.QuizSubmissions
            .AsNoTracking()
            .Where(qs => qs.StudentId == currentUserId && qs.Quiz.CourseItem.Chapter.CourseId == id)
            .ToListAsync();
        var quizSubGrouped = quizSubmissions
            .GroupBy(qs => qs.QuizId)
            .ToDictionary(g => g.Key, g => new { HasPassed = g.Any(s => s.IsPassed), BestScore = g.Max(s => s.Score) });

        // Query student assignment submissions for this course
        var assignmentSubmissions = await _context.AssignmentSubmissions
            .AsNoTracking()
            .Where(s => s.StudentId == currentUserId && s.Assignment.CourseItem.Chapter.CourseId == id)
            .ToListAsync();
        var assignmentCompletedMap = assignmentSubmissions
            .GroupBy(s => s.AssignmentId)
            .ToDictionary(g => g.Key, g => g.Any(s => s.Status == SubmissionStatus.Graded || s.Status == SubmissionStatus.Submitted || s.Status == SubmissionStatus.Late));

        // Find first item for "Start Learning" (exclude unpublished quizzes for non-admin students)
        var firstLesson = course.Chapters
            .OrderBy(ch => ch.Position)
            .ThenBy(ch => ch.Id)
            .SelectMany(ch => ch.CourseItems
                .Where(ci => isAdmin || ci.Type != ItemType.Quiz || (ci.Quiz != null && ci.Quiz.IsPublished))
                .OrderBy(ci => ci.Position)
                .ThenBy(ci => ci.Id))
            .FirstOrDefault();

        var modules = course.Chapters
            .OrderBy(ch => ch.Position)
            .ThenBy(ch => ch.Id)
            .Select(ch => new StudentCourseModuleOverviewViewModel
            {
                ModuleId = ch.Id,
                Title = ch.Title,
                Position = ch.Position,
                Lessons = ch.CourseItems
                    .Where(ci => isAdmin || ci.Type != ItemType.Quiz || (ci.Quiz != null && ci.Quiz.IsPublished))
                    .OrderBy(ci => ci.Position)
                    .ThenBy(ci => ci.Id)
                    .Select(ci =>
                    {
                        bool isPassed = false;
                        double? bestScore = null;
                        if (ci.Quiz != null && quizSubGrouped.TryGetValue(ci.Quiz.Id, out var qStat))
                        {
                            isPassed = qStat.HasPassed;
                            bestScore = qStat.BestScore;
                        }

                        bool isCompleted = completedSet.Contains(ci.Id) || isPassed;
                        if (ci.Type == ItemType.Assignment && ci.Assignment != null && assignmentCompletedMap.TryGetValue(ci.Assignment.Id, out var aComp) && aComp)
                        {
                            isCompleted = true;
                        }

                        return new StudentCourseLessonOverviewViewModel
                        {
                            CourseItemId = ci.Id,
                            LessonId = ci.Lesson?.Id ?? 0,
                            QuizId = ci.Quiz?.Id,
                            AssignmentId = ci.Assignment?.Id,
                            BookId = ci.Book?.Id,
                            Type = ci.Type,
                            Title = ci.Title,
                            Position = ci.Position,
                            HasVideo = !string.IsNullOrWhiteSpace(ci.Lesson?.VideoUrl),
                            HasContent = !string.IsNullOrWhiteSpace(ci.Lesson?.ContentHtml),
                            IsCompleted = isCompleted,
                            HasPassedQuiz = isPassed,
                            BestQuizScore = bestScore
                        };
                    }).ToList()
            }).ToList();

        var viewModel = new StudentCourseOverviewViewModel
        {
            CourseId = course.Id,
            Code = course.Code,
            Title = course.Title,
            Description = course.Description,
            Thumbnail = course.Thumbnail,
            TeacherName = course.Teacher?.FullName ?? "Instructor",
            TeacherEmail = course.Teacher?.Email ?? string.Empty,
            MajorName = course.Major?.Name ?? "General",
            SemesterName = course.Semester?.Name ?? "Current Term",
            EnrolledAt = enrolledAt,
            Modules = modules,
            FirstCourseItemId = firstLesson?.Id,
            FirstLessonTitle = firstLesson?.Title
        };

        ViewData["Title"] = course.Title;
        ViewData["ActivePage"] = "Courses";
        return View("CourseOverview", viewModel);
    }

    /// <summary>
    /// Alias for Course action.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CourseOverview(int id)
    {
        return await Course(id);
    }

    /// <summary>
    /// Start Learning Entry Point:
    /// Locates the first available lesson in curriculum sequence and routes to the Lesson Player.
    /// If no lesson exists, notifies the student with a friendly warning.
    /// </summary>
    [HttpGet]
    [Route("Student/Course/{courseId:int}/Learn")]
    public async Task<IActionResult> Learn(int courseId)
    {
        if (courseId <= 0)
        {
            return NotFound("Invalid course ID.");
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");
        var isEnrolled = await _enrollmentService.IsStudentEnrolledAsync(currentUserId, courseId);
        if (!isEnrolled && !isAdmin)
        {
            _logger.LogWarning("Course learn access denied: Student '{StudentId}' is not enrolled in Course ID {CourseId}.", currentUserId, courseId);
            return Forbid();
        }

        var firstLessonId = await _curriculumService.GetFirstLessonIdAsync(courseId);
        if (!firstLessonId.HasValue)
        {
            TempData["WarningMessage"] = "This course does not have learning content yet.";
            return RedirectToAction(nameof(Course), new { id = courseId });
        }

        return RedirectToAction(nameof(Lesson), new { courseId = courseId, lessonId = firstLessonId.Value });
    }

    /// <summary>
    /// Student Lesson Player:
    /// Opens a specific lesson within an enrolled course.
    /// Enforces strict server-side enrollment and lesson-course ownership verification.
    /// Loads student completion status and course progress from SQL Server.
    /// </summary>
    [HttpGet]
    [Route("Student/Course/{courseId:int}/Lesson/{lessonId:int}")]
    [Route("Student/Lesson/{lessonId:int}")]
    public async Task<IActionResult> Lesson(int? courseId, int lessonId)
    {
        if (lessonId <= 0)
        {
            return NotFound("Invalid lesson ID.");
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");

        // If courseId not provided in route (e.g. /Student/Lesson/{lessonId}), resolve it from SQL Server
        if (!courseId.HasValue || courseId.Value <= 0)
        {
            var item = await _context.CourseItems
                .Include(ci => ci.Chapter)
                .Include(ci => ci.Lesson)
                .AsNoTracking()
                .FirstOrDefaultAsync(ci => (ci.Id == lessonId || (ci.Lesson != null && ci.Lesson.Id == lessonId)) && ci.Type == ItemType.Lesson);

            if (item == null || item.Chapter == null)
            {
                return NotFound("Lesson not found.");
            }

            courseId = item.Chapter.CourseId;
        }

        // 1. Strict Server-Side Enrollment Security Check
        var isEnrolled = await _enrollmentService.IsStudentEnrolledAsync(currentUserId, courseId.Value);
        if (!isEnrolled && !isAdmin)
        {
            _logger.LogWarning("Lesson player access denied: Student '{StudentId}' is not enrolled in Course ID {CourseId}.", currentUserId, courseId.Value);
            return Forbid();
        }

        // 2. Load complete Lesson Player ViewModel with completion tracking & ownership validation
        var playerModel = await _curriculumService.GetStudentLessonPlayerAsync(courseId.Value, lessonId, currentUserId, allowAdminPreview: isAdmin);
        if (playerModel == null)
        {
            _logger.LogWarning("Lesson ID {LessonId} not found or does not belong to Course ID {CourseId}.", lessonId, courseId.Value);
            return NotFound("Lesson not found in this course.");
        }

        ViewData["Title"] = $"{playerModel.LessonTitle} - {playerModel.CourseTitle}";
        ViewData["ActivePage"] = "Courses";

        return View("Lesson", playerModel);
    }

    [HttpGet]
    [Authorize(Roles = "Student,Teacher,Assistance,Admin")]
    [Route("Student/Video/{fileName}")]
    public async Task<IActionResult> StreamLessonVideo(string fileName)
    {
        if (_videoStorageService == null || string.IsNullOrWhiteSpace(fileName))
        {
            return NotFound();
        }

        var safeFileName = Path.GetFileName(fileName);
        var expectedUrl = $"/Student/Video/{safeFileName}";

        var lessonCourseId = await _context.Lessons
            .AsNoTracking()
            .Where(l => l.VideoUrl == expectedUrl)
            .Select(l => (int?)l.CourseItem.Chapter.CourseId)
            .FirstOrDefaultAsync();

        if (!lessonCourseId.HasValue)
        {
            return NotFound();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId)) return Challenge();

        if (!User.IsInRole("Admin"))
        {
            int cId = lessonCourseId.Value;
            bool authorized = await _enrollmentService.IsStudentEnrolledAsync(currentUserId, cId) ||
                              await _context.Courses.AsNoTracking().AnyAsync(c => c.Id == cId && c.TeacherId == currentUserId) ||
                              await _context.CourseAssistants.AsNoTracking().AnyAsync(ca => ca.CourseId == cId && ca.AssistantId == currentUserId);
            if (!authorized) return Forbid();
        }

        var videoInfo = _videoStorageService.GetVideoPhysicalPath(safeFileName);
        if (videoInfo == null) return NotFound();

        return PhysicalFile(videoInfo.Value.FullPath, videoInfo.Value.ContentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Mark Lesson as Complete:
    /// Server-side action allowing an enrolled student to mark a lesson as completed.
    /// Strictly verifies user authentication, student role, course enrollment, and lesson-course ownership.
    /// Prevents duplicate completions idempotently and persists to SQL Server.
    /// Supports both progressive AJAX JSON response and standard POST form redirection.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Student/Course/{courseId:int}/Lesson/{courseItemId:int}/Complete")]
    [Route("Student/CompleteLesson")]
    public async Task<IActionResult> CompleteLesson(int courseId, int courseItemId)
    {
        if (courseId <= 0 || courseItemId <= 0)
        {
            return NotFound("Invalid course ID or lesson ID.");
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");

        // 1. Strict Server-Side Enrollment Security Check
        var isEnrolled = await _enrollmentService.IsStudentEnrolledAsync(currentUserId, courseId);
        if (!isEnrolled && !isAdmin)
        {
            _logger.LogWarning("CompleteLesson access denied: Student '{StudentId}' is not enrolled in Course ID {CourseId}.", currentUserId, courseId);
            return Forbid();
        }

        // 2. Mark lesson complete via service layer (idempotent, SQL Server unique-constraint guarded)
        var result = await _curriculumService.MarkLessonCompleteAsync(currentUserId, courseId, courseItemId);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("CompleteLesson failed for Student '{StudentId}', Course {CourseId}, Item {ItemId}: {Message}",
                currentUserId, courseId, courseItemId, result.Message);

            if (result.Status == CurriculumOperationStatus.NotFound)
            {
                return NotFound(result.Message);
            }

            return BadRequest(result.Message);
        }

        // 3. Compute updated course progress
        var progressDict = await _curriculumService.GetStudentCoursesProgressAsync(currentUserId, new[] { courseId });
        var (total, completed) = progressDict.GetValueOrDefault(courseId, (0, 0));
        var progressPercentage = total > 0 ? (int)Math.Round((double)completed / total * 100) : 0;

        // 4. Return AJAX JSON or Redirect
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                     Request.Headers["Accept"].ToString().Contains("application/json");

        if (isAjax)
        {
            return Json(new
            {
                success = true,
                isCompleted = true,
                courseItemId = courseItemId,
                totalLessons = total,
                completedLessons = completed,
                progressPercentage = progressPercentage,
                message = result.Message
            });
        }

        TempData["SuccessMessage"] = "Lesson marked as complete!";
        return RedirectToAction(nameof(Lesson), new { courseId = courseId, lessonId = courseItemId });
    }

    // =========================================================================
    // STEP 4.3 — STUDENT QUIZ ACTIONS
    // =========================================================================

    /// <summary>
    /// Student Quiz Player:
    /// Renders the interactive quiz interface with curriculum sidebar.
    /// Strictly verifies user authentication, student role, course enrollment, and quiz-course ownership.
    /// </summary>
    [HttpGet]
    [Route("Student/Course/{courseId:int}/Quiz/{courseItemId:int}")]
    [Route("Student/Quiz/{courseItemId:int}")]
    public async Task<IActionResult> Quiz(int? courseId, int courseItemId)
    {
        if (courseItemId <= 0)
        {
            return NotFound("Invalid quiz item ID.");
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");

        // Resolve courseId if not in route
        if (!courseId.HasValue || courseId.Value <= 0)
        {
            var item = await _context.CourseItems
                .Include(ci => ci.Chapter)
                .Include(ci => ci.Quiz)
                .AsNoTracking()
                .FirstOrDefaultAsync(ci => (ci.Id == courseItemId || (ci.Quiz != null && ci.Quiz.Id == courseItemId)) && ci.Type == ItemType.Quiz);

            if (item == null || item.Chapter == null)
            {
                return NotFound("Quiz not found.");
            }

            courseId = item.Chapter.CourseId;
            courseItemId = item.Id;
        }

        // Strict Server-Side Enrollment Security Check
        var isEnrolled = await _enrollmentService.IsStudentEnrolledAsync(currentUserId, courseId.Value);
        if (!isEnrolled && !isAdmin)
        {
            _logger.LogWarning("Quiz access denied: Student '{StudentId}' is not enrolled in Course ID {CourseId}.", currentUserId, courseId.Value);
            return Forbid();
        }

        var playerModel = await _curriculumService.GetStudentQuizPlayerAsync(courseId.Value, courseItemId, currentUserId, allowAdminPreview: isAdmin);
        if (playerModel == null)
        {
            _logger.LogWarning("Quiz CourseItem ID {CourseItemId} not found or does not belong to Course ID {CourseId}.", courseItemId, courseId.Value);
            return NotFound("Quiz not found in this course.");
        }

        // Server-side quiz attempt timer: persist StartedAtUtc across browser refreshes
        if (!isAdmin && playerModel.CanAttempt && playerModel.TimeLimitMinutes > 0 && _memoryCache != null)
        {
            var timerKey = $"quiz-timer:{currentUserId}:{courseItemId}:{playerModel.CurrentAttemptNumber}";
            var startedAtUtc = _memoryCache.GetOrCreate(timerKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(playerModel.TimeLimitMinutes + 5);
                return DateTime.UtcNow;
            });
            var remaining = (int)Math.Floor((startedAtUtc.AddMinutes(playerModel.TimeLimitMinutes) - DateTime.UtcNow).TotalSeconds);
            playerModel.RemainingSeconds = Math.Max(0, remaining);
        }

        ViewData["Title"] = $"{playerModel.QuizTitle} - {playerModel.CourseTitle}";
        ViewData["ActivePage"] = "Courses";

        return View("Quiz", playerModel);
    }

    /// <summary>
    /// Student Quiz Submission:
    /// Server evaluates answers against QuestionOption.IsCorrect in SQL Server.
    /// Client cannot manipulate score.
    /// Saves QuizSubmission record and redirects to results breakdown.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Student/Course/{courseId:int}/Quiz/{courseItemId:int}/Submit")]
    [Route("Student/SubmitQuiz")]
    public async Task<IActionResult> SubmitQuiz(int courseId, int courseItemId, Dictionary<int, int>? answers)
    {
        if (courseId <= 0 || courseItemId <= 0)
        {
            return NotFound("Invalid course ID or quiz item ID.");
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");

        // Strict Server-Side Enrollment Security Check
        var isEnrolled = await _enrollmentService.IsStudentEnrolledAsync(currentUserId, courseId);
        if (!isEnrolled && !isAdmin)
        {
            _logger.LogWarning("SubmitQuiz access denied: Student '{StudentId}' is not enrolled in Course ID {CourseId}.", currentUserId, courseId);
            return Forbid();
        }

        // Server-side quiz timer enforcement (30-second grace period for auto-submit latency)
        string? activeTimerKey = null;
        if (!isAdmin && _memoryCache != null)
        {
            var quizItem = await _context.CourseItems
                .AsNoTracking()
                .Include(ci => ci.Quiz)
                .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Quiz);
            if (quizItem?.Quiz != null && quizItem.Quiz.TimeLimitMinutes > 0)
            {
                var attemptCount = await _context.QuizSubmissions
                    .CountAsync(qs => qs.QuizId == quizItem.Quiz.Id && qs.StudentId == currentUserId);
                activeTimerKey = $"quiz-timer:{currentUserId}:{courseItemId}:{attemptCount + 1}";
                if (_memoryCache.TryGetValue(activeTimerKey, out DateTime startedAtUtc))
                {
                    var deadline = startedAtUtc.AddMinutes(quizItem.Quiz.TimeLimitMinutes).AddSeconds(30);
                    if (DateTime.UtcNow > deadline)
                    {
                        _memoryCache.Remove(activeTimerKey);
                        _logger.LogWarning("SubmitQuiz expired: Student '{StudentId}', CourseItem {ItemId}, StartedAt {StartedAt}.",
                            currentUserId, courseItemId, startedAtUtc);
                        TempData["ErrorMessage"] = "Quiz time limit has expired. Your submission was rejected.";
                        return RedirectToAction(nameof(Quiz), new { courseId, courseItemId });
                    }
                }
            }
        }

        var (result, outcome) = await _curriculumService.SubmitQuizAsync(currentUserId, courseId, courseItemId, answers ?? new());
        if (!result.IsSuccess)
        {
            _logger.LogWarning("SubmitQuiz failed for Student '{StudentId}', Course {CourseId}, Item {ItemId}: {Message}",
                currentUserId, courseId, courseItemId, result.Message);

            if (result.Status == CurriculumOperationStatus.Forbidden)
            {
                return Forbid();
            }
            if (result.Status == CurriculumOperationStatus.NotFound)
            {
                return NotFound(result.Message);
            }
            return BadRequest(result.Message);
        }

        if (activeTimerKey != null && _memoryCache != null)
        {
            _memoryCache.Remove(activeTimerKey);
        }

        TempData["SuccessMessage"] = result.Message;
        return RedirectToAction(nameof(QuizResult), new
        {
            courseId = courseId,
            courseItemId = courseItemId,
            submissionId = outcome!.SubmissionId
        });
    }

    /// <summary>
    /// Student Quiz Result:
    /// Displays score, pass/fail status, points earned, and question-by-question breakdown.
    /// Strictly verifies student identity and isolates student submissions.
    /// </summary>
    [HttpGet]
    [Route("Student/Course/{courseId:int}/Quiz/{courseItemId:int}/Result/{submissionId:int}")]
    [Route("Student/QuizResult")]
    public async Task<IActionResult> QuizResult(int courseId, int courseItemId, int submissionId)
    {
        if (courseId <= 0 || courseItemId <= 0 || submissionId <= 0)
        {
            return NotFound("Invalid course, quiz, or submission ID.");
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");

        // Strict Server-Side Enrollment Security Check
        var isEnrolled = await _enrollmentService.IsStudentEnrolledAsync(currentUserId, courseId);
        if (!isEnrolled && !isAdmin)
        {
            _logger.LogWarning("QuizResult access denied: Student '{StudentId}' is not enrolled in Course ID {CourseId}.", currentUserId, courseId);
            return Forbid();
        }

        // Student isolation and ownership check
        var submission = await _context.QuizSubmissions
            .Include(qs => qs.Quiz)
                .ThenInclude(q => q.CourseItem)
                    .ThenInclude(ci => ci.Chapter)
            .AsNoTracking()
            .FirstOrDefaultAsync(qs => qs.Id == submissionId);

        if (submission == null)
        {
            return NotFound("Quiz submission not found.");
        }

        if (submission.StudentId != currentUserId && !isAdmin)
        {
            _logger.LogWarning("QuizResult access denied: Student '{StudentId}' attempted to access Submission {SubmissionId} belonging to '{OwnerId}'.",
                currentUserId, submissionId, submission.StudentId);
            return Forbid();
        }

        if (submission.Quiz.CourseItemId != courseItemId || submission.Quiz.CourseItem.Chapter.CourseId != courseId)
        {
            return NotFound("Quiz submission does not belong to this course or quiz item.");
        }

        var resultModel = await _curriculumService.GetStudentQuizResultAsync(courseId, courseItemId, submissionId, currentUserId, allowAdminPreview: isAdmin);
        if (resultModel == null)
        {
            return NotFound("Quiz result details not found.");
        }

        ViewData["Title"] = $"Result: {resultModel.QuizTitle} - {resultModel.CourseTitle}";
        ViewData["ActivePage"] = "Courses";

        return View("QuizResult", resultModel);
    }

    // =========================================================================
    // PHASE 4 — ASSIGNMENT PLAYER, SUBMISSION & DOWNLOAD
    // =========================================================================

    [HttpGet]
    [Route("Student/Course/{courseId:int}/Assignment/{courseItemId:int}")]
    [Route("Student/Assignment/{courseItemId:int}")]
    public async Task<IActionResult> Assignment(int? courseId, int courseItemId)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId)) return Challenge();

        var isAdmin = User.IsInRole("Admin");

        if (!courseId.HasValue || courseId.Value <= 0)
        {
            var item = await _context.CourseItems
                .Include(ci => ci.Chapter)
                .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Assignment);

            if (item?.Chapter != null)
            {
                courseId = item.Chapter.CourseId;
            }
            else
            {
                return NotFound("Assignment item not found.");
            }
        }

        var isEnrolled = await _enrollmentService.IsStudentEnrolledAsync(currentUserId, courseId.Value);
        if (!isEnrolled && !isAdmin)
        {
            _logger.LogWarning("Assignment access denied: Student '{StudentId}' is not enrolled in Course ID {CourseId}.", currentUserId, courseId.Value);
            return Forbid();
        }

        var model = await _curriculumService.GetStudentAssignmentAsync(courseId.Value, courseItemId, currentUserId, allowAdminPreview: isAdmin);
        if (model == null)
        {
            return NotFound("Assignment not found or you are not enrolled in this course.");
        }

        ViewData["Title"] = $"{model.Title} - {model.CourseTitle}";
        ViewData["ActivePage"] = "Courses";
        return View("Assignment", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Student/Course/{courseId:int}/Assignment/{courseItemId:int}/Submit")]
    [Route("Student/SubmitAssignment")]
    public async Task<IActionResult> SubmitAssignment(
        int courseId,
        int courseItemId,
        IFormFile? submissionFile,
        List<IFormFile>? files,
        string? externalUrl,
        string? textResponse)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId)) return Challenge();

        var (result, submission) = await _curriculumService.SubmitAssignmentAsync(
            courseId,
            courseItemId,
            currentUserId,
            submissionFile,
            externalUrl,
            textResponse,
            files);

        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Message ?? "Failed to submit assignment.";
        }
        else
        {
            TempData["SuccessMessage"] = result.Message ?? "Assignment submitted successfully!";
        }

        return RedirectToAction(nameof(Assignment), new { courseId, courseItemId });
    }

    [HttpGet]
    [Route("Student/Assignment/DownloadAttachment/{attachmentId:int}")]
    public async Task<IActionResult> DownloadAttachment(int attachmentId)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId)) return Challenge();

        var attachment = await _context.AssignmentAttachments
            .Include(a => a.Submission)
            .FirstOrDefaultAsync(a => a.Id == attachmentId);

        if (attachment == null || attachment.IsDeleted) return NotFound("Attachment not found or has been removed.");

        if (attachment.Submission.StudentId != currentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var fileResult = await _assignmentStorageService.GetAssignmentFileAsync(attachment.StoragePath, attachment.OriginalFileName);
        if (fileResult == null || fileResult.Value.FileBytes == null)
        {
            return NotFound("Stored file not found.");
        }

        return File(fileResult.Value.FileBytes, fileResult.Value.ContentType, fileResult.Value.DownloadFileName);
    }

    [HttpGet]
    [Route("Student/Assignment/Download/{submissionId:int}")]
    public async Task<IActionResult> DownloadMySubmissionFile(int submissionId)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId)) return Challenge();

        var submission = await _context.AssignmentSubmissions
            .Include(s => s.Attachments)
            .Include(s => s.Assignment)
                .ThenInclude(a => a.CourseItem)
                    .ThenInclude(ci => ci.Chapter)
            .FirstOrDefaultAsync(s => s.Id == submissionId);

        if (submission == null) return NotFound("Submission not found.");

        if (submission.StudentId != currentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(submission.FileUrl))
        {
            return BadRequest("No file attached to this submission.");
        }

        bool isSoftDeleted = submission.Attachments.Any(a =>
            a.IsDeleted && string.Equals(a.StoragePath, submission.FileUrl, StringComparison.OrdinalIgnoreCase)) ||
            (submission.Attachments.Any() && submission.Attachments.All(a => a.IsDeleted));
        if (isSoftDeleted)
        {
            return NotFound("Attachment not found or has been removed.");
        }

        if (submission.FileUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            submission.FileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var safeUrl = _htmlSanitizer.SanitizeHttpUrl(submission.FileUrl);
            if (safeUrl == null)
            {
                return BadRequest("Invalid submission URL.");
            }
            return Redirect(safeUrl);
        }

        var fileResult = await _assignmentStorageService.GetAssignmentFileAsync(submission.FileUrl, submission.OriginalFileName);
        if (fileResult == null || fileResult.Value.FileBytes == null)
        {
            return NotFound("Stored file not found.");
        }

        return File(fileResult.Value.FileBytes, fileResult.Value.ContentType, fileResult.Value.DownloadFileName);
    }

    // =========================================================================
    // PHASE 4 — BOOK READER
    // =========================================================================

    [HttpGet]
    [Route("Student/Course/{courseId:int}/Book/{courseItemId:int}")]
    [Route("Student/Book/{courseItemId:int}")]
    public async Task<IActionResult> Book(int? courseId, int courseItemId)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId)) return Challenge();

        var isAdmin = User.IsInRole("Admin");

        if (!courseId.HasValue || courseId.Value <= 0)
        {
            var item = await _context.CourseItems
                .Include(ci => ci.Chapter)
                .FirstOrDefaultAsync(ci => ci.Id == courseItemId && ci.Type == ItemType.Book);

            if (item?.Chapter != null)
            {
                courseId = item.Chapter.CourseId;
            }
            else
            {
                return NotFound("Book item not found.");
            }
        }

        var isEnrolled = await _enrollmentService.IsStudentEnrolledAsync(currentUserId, courseId.Value);
        if (!isEnrolled && !isAdmin)
        {
            _logger.LogWarning("Book access denied: Student '{StudentId}' is not enrolled in Course ID {CourseId}.", currentUserId, courseId.Value);
            return Forbid();
        }

        var model = await _curriculumService.GetStudentBookAsync(courseId.Value, courseItemId, currentUserId, allowAdminPreview: isAdmin);
        if (model == null)
        {
            return NotFound("Book not found or you are not enrolled in this course.");
        }

        ViewData["Title"] = $"{model.Title} - {model.CourseTitle}";
        ViewData["ActivePage"] = "Courses";
        return View("Book", model);
    }

    // =========================================================================
    // PHASE 4 — STUDENT GRADEBOOK
    // =========================================================================

    [HttpGet]
    [Route("Student/Grades")]
    public async Task<IActionResult> Grades(int? courseId = null)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId)) return Challenge();

        var model = await _curriculumService.GetStudentGradesAsync(currentUserId, courseId);

        ViewData["Title"] = "My Grades & Academic Records";
        ViewData["ActivePage"] = "Grades";
        return View("Grades", model);
    }
}
