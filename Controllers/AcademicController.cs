using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Controllers;

[Authorize(Roles = "Admin,Teacher")]
public class AcademicController : Controller
{
    private readonly ICourseService _courseService;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<AcademicController> _logger;
    private readonly ICurriculumService _curriculumService;
    private readonly IHtmlSanitizationService _htmlSanitizer;
    private readonly IVideoStorageService? _videoStorageService;

    public AcademicController(
        ICourseService courseService,
        ApplicationDbContext context,
        UserManager<User> userManager,
        ILogger<AcademicController> logger,
        ICurriculumService? curriculumService = null,
        IHtmlSanitizationService? htmlSanitizer = null,
        IVideoStorageService? videoStorageService = null)
    {
        _courseService = courseService;
        _context = context;
        _userManager = userManager;
        _logger = logger;
        _htmlSanitizer = htmlSanitizer ?? new AUBDigitalAcademy.Services.Implementations.HtmlSanitizationService();
        _curriculumService = curriculumService ?? new AUBDigitalAcademy.Services.Implementations.CurriculumService(context, new LoggerFactory().CreateLogger<AUBDigitalAcademy.Services.Implementations.CurriculumService>(), null, _htmlSanitizer);
        _videoStorageService = videoStorageService;
    }

    private string? GetCurrentUserId()
    {
        return _userManager?.GetUserId(User)
               ?? User?.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private bool IsAjaxRequest()
    {
        return Request != null &&
               string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal);
    }

    private IActionResult ForbiddenResult(bool forceJson = false)
    {
        if (forceJson || IsAjaxRequest())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                success = false,
                message = "You do not have permission to manage this course."
            });
        }
        return Forbid();
    }

    private async Task<bool> CanManageCourseAsync(int? courseId)
    {
        if (User?.IsInRole("Admin") == true)
        {
            return true;
        }

        if (!courseId.HasValue || courseId.Value <= 0)
        {
            return false;
        }

        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return false;
        }

        return await _context.Courses
            .AsNoTracking()
            .AnyAsync(c => c.Id == courseId.Value && c.TeacherId == currentUserId);
    }

    private Task<int?> ResolveCourseIdFromChapterAsync(int chapterId)
    {
        return chapterId <= 0
            ? Task.FromResult<int?>(null)
            : _context.Chapters.AsNoTracking()
                .Where(ch => ch.Id == chapterId)
                .Select(ch => (int?)ch.CourseId)
                .FirstOrDefaultAsync();
    }

    private Task<int?> ResolveCourseIdFromCourseItemAsync(int id, ItemType? expectedType = null, bool alsoCheckLessonId = false)
    {
        return id <= 0
            ? Task.FromResult<int?>(null)
            : _context.CourseItems.AsNoTracking()
                .Where(ci => (!expectedType.HasValue || ci.Type == expectedType.Value)
                             && (ci.Id == id || (alsoCheckLessonId && ci.Lesson != null && ci.Lesson.Id == id)))
                .Select(ci => (int?)ci.Chapter.CourseId)
                .FirstOrDefaultAsync();
    }

    private Task<int?> ResolveCourseIdFromQuizAsync(int quizOrItemId, bool allowCourseItemId = false)
    {
        return quizOrItemId <= 0
            ? Task.FromResult<int?>(null)
            : _context.Quizzes.AsNoTracking()
                .Where(q => q.Id == quizOrItemId || (allowCourseItemId && q.CourseItemId == quizOrItemId))
                .Select(q => (int?)q.CourseItem.Chapter.CourseId)
                .FirstOrDefaultAsync();
    }

    private Task<int?> ResolveCourseIdFromQuestionAsync(int questionId)
    {
        return questionId <= 0
            ? Task.FromResult<int?>(null)
            : _context.Questions.AsNoTracking()
                .Where(q => q.Id == questionId)
                .Select(q => (int?)q.Quiz.CourseItem.Chapter.CourseId)
                .FirstOrDefaultAsync();
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> Course()
    {
        ViewData["Title"] = "Course Management";
        ViewData["ActivePage"] = "Courses";

        var viewModel = await BuildCourseManagementViewModelAsync();
        return View(viewModel);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> CreateCourse()
    {
        ViewData["Title"] = "Add New Course";
        ViewData["ActivePage"] = "Courses";

        var model = new CourseCreateViewModel();
        await PopulateDropdownsAsync(model);
        return View(model);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCourse(CourseCreateViewModel createModel, string? submitAction = null)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        // Determine status based on action button clicked (Save Draft vs Publish)
        bool isPublish = submitAction?.Split(',', StringSplitOptions.TrimEntries).Any(s => string.Equals(s, "publish", StringComparison.OrdinalIgnoreCase)) == true;
        bool isDraft = submitAction?.Split(',', StringSplitOptions.TrimEntries).Any(s => string.Equals(s, "draft", StringComparison.OrdinalIgnoreCase)) == true;

        if (isDraft)
        {
            createModel.Status = CourseStatus.Draft;
        }
        else if (isPublish)
        {
            createModel.Status = CourseStatus.Published;
        }

        // Validate duplicate course code
        if (!string.IsNullOrWhiteSpace(createModel.Code) && await _courseService.CourseCodeExistsAsync(createModel.Code))
        {
            ModelState.AddModelError("CreateModel.Code", "A course with this course code already exists.");
            ModelState.AddModelError(nameof(createModel.Code), "A course with this course code already exists.");
        }

        // Validate Teacher exists
        if (string.IsNullOrWhiteSpace(createModel.TeacherId))
        {
            ModelState.AddModelError("CreateModel.TeacherId", "Please select a teacher.");
            ModelState.AddModelError(nameof(createModel.TeacherId), "Please select a teacher.");
        }
        else
        {
            var teacherExists = await _context.Users.AnyAsync(u => u.Id == createModel.TeacherId);
            if (!teacherExists)
            {
                ModelState.AddModelError("CreateModel.TeacherId", "The selected teacher does not exist.");
                ModelState.AddModelError(nameof(createModel.TeacherId), "The selected teacher does not exist.");
            }
        }

        // Validate Major exists
        if (createModel.MajorId <= 0)
        {
            ModelState.AddModelError("CreateModel.MajorId", "Please select a major.");
            ModelState.AddModelError(nameof(createModel.MajorId), "Please select a major.");
        }
        else
        {
            var majorExists = await _context.Majors.AnyAsync(m => m.Id == createModel.MajorId);
            if (!majorExists)
            {
                ModelState.AddModelError("CreateModel.MajorId", "The selected major does not exist.");
                ModelState.AddModelError(nameof(createModel.MajorId), "The selected major does not exist.");
            }
        }

        // Validate Semester exists
        if (createModel.SemesterId <= 0)
        {
            ModelState.AddModelError("CreateModel.SemesterId", "Please select a semester.");
            ModelState.AddModelError(nameof(createModel.SemesterId), "Please select a semester.");
        }
        else
        {
            var semesterExists = await _context.Semesters.AnyAsync(s => s.Id == createModel.SemesterId);
            if (!semesterExists)
            {
                ModelState.AddModelError("CreateModel.SemesterId", "The selected semester does not exist.");
                ModelState.AddModelError(nameof(createModel.SemesterId), "The selected semester does not exist.");
            }
        }

        // Validate Category exists if provided
        if (createModel.CategoryId.HasValue && createModel.CategoryId.Value > 0)
        {
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == createModel.CategoryId.Value);
            if (!categoryExists)
            {
                ModelState.AddModelError("CreateModel.CategoryId", "The selected category does not exist.");
                ModelState.AddModelError(nameof(createModel.CategoryId), "The selected category does not exist.");
            }
        }

        // Validate Thumbnail URL/path if provided
        if (!string.IsNullOrWhiteSpace(createModel.Thumbnail) && !_htmlSanitizer.IsSafeResourceUrl(createModel.Thumbnail))
        {
            const string thumbErr = "Invalid cover image URL or path. Only http://, https://, or safe relative paths are permitted.";
            ModelState.AddModelError("CreateModel.Thumbnail", thumbErr);
            ModelState.AddModelError(nameof(createModel.Thumbnail), thumbErr);
        }

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return Json(new { success = false, message = "Please correct the errors in the form.", errors });
            }

            var vm = await BuildCourseManagementViewModelAsync(existingCreateModel: createModel);
            ViewData["Title"] = "Course Management";
            ViewData["ActivePage"] = "Courses";
            ViewBag.OpenCreateModal = true;
            return View("Course", vm);
        }

        try
        {
            var course = new Course
            {
                Code = createModel.Code.Trim().ToUpper(),
                Title = createModel.Title.Trim(),
                Description = createModel.Description?.Trim(),
                Thumbnail = string.IsNullOrWhiteSpace(createModel.Thumbnail) ? null : createModel.Thumbnail.Trim(),
                Status = createModel.Status,
                MajorId = createModel.MajorId,
                SemesterId = createModel.SemesterId,
                TeacherId = createModel.TeacherId
            };

            var createdCourse = await _courseService.CreateCourseAsync(course);

            var successMessage = createdCourse.Status == CourseStatus.Published
                ? "Course published successfully."
                : "Course saved as draft successfully.";

            TempData["SuccessMessage"] = successMessage;

            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = successMessage,
                    courseId = createdCourse.Id,
                    isPublished = createdCourse.Status == CourseStatus.Published,
                    redirectUrl = Url.Action("Course", "Academic")
                });
            }

            return RedirectToAction(nameof(Course));
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("CreateModel.Code", "A course with this course code already exists.");
                ModelState.AddModelError(nameof(createModel.Code), "A course with this course code already exists.");
            }
            ModelState.AddModelError(string.Empty, ex.Message);
            if (isAjax)
            {
                return Json(new { success = false, message = ex.Message });
            }

            var vm = await BuildCourseManagementViewModelAsync(existingCreateModel: createModel);
            ViewBag.OpenCreateModal = true;
            return View("Course", vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create course {Code}", createModel.Code);
            var errorMsg = "An unexpected error occurred while saving the course. Please try again.";

            if (isAjax)
            {
                return Json(new { success = false, message = errorMsg });
            }

            ModelState.AddModelError(string.Empty, errorMsg);
            var vm = await BuildCourseManagementViewModelAsync(existingCreateModel: createModel);
            ViewBag.OpenCreateModal = true;
            return View("Course", vm);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetCourseJson(int id)
    {
        if (id <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid course ID." });
        }

        var course = await _courseService.GetCourseByIdAsync(id);
        if (course == null)
        {
            return NotFound(new { success = false, message = "Course not found." });
        }

        if (!await CanManageCourseAsync(course.Id))
        {
            return ForbiddenResult(forceJson: true);
        }

        return Json(new
        {
            success = true,
            course = new
            {
                id = course.Id,
                title = course.Title,
                code = course.Code,
                description = course.Description ?? "",
                thumbnail = course.Thumbnail ?? "",
                status = (int)course.Status,
                statusName = course.Status.ToString(),
                majorId = course.MajorId,
                semesterId = course.SemesterId,
                teacherId = course.TeacherId,
                categoryId = course.Major?.CategoryId ?? 0
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> EditCourse(int id)
    {
        ViewData["Title"] = "Edit Course";
        ViewData["ActivePage"] = "Courses";

        if (id <= 0)
        {
            TempData["ErrorMessage"] = "Invalid course ID.";
            return RedirectToAction(nameof(Course));
        }

        var course = await _courseService.GetCourseByIdAsync(id);
        if (course == null)
        {
            TempData["ErrorMessage"] = "Course not found.";
            return RedirectToAction(nameof(Course));
        }

        if (!await CanManageCourseAsync(course.Id))
        {
            return Forbid();
        }

        var editModel = new CourseEditViewModel
        {
            Id = course.Id,
            Title = course.Title,
            Code = course.Code,
            Description = course.Description,
            Thumbnail = course.Thumbnail,
            Status = course.Status,
            MajorId = course.MajorId,
            SemesterId = course.SemesterId,
            TeacherId = course.TeacherId,
            CategoryId = course.Major?.CategoryId
        };

        var vm = await BuildCourseManagementViewModelAsync(existingEditModel: editModel);
        ViewBag.OpenEditModal = true;
        return View("Course", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCourse(CourseEditViewModel editModel, string? submitAction = null)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (editModel.Id <= 0)
        {
            ModelState.AddModelError(string.Empty, "Invalid course ID.");
        }
        else
        {
            var existingCourse = await _context.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == editModel.Id);
            if (existingCourse != null)
            {
                if (!await CanManageCourseAsync(existingCourse.Id))
                {
                    return ForbiddenResult();
                }

                // Teachers cannot reassign course ownership to another instructor
                if (!User.IsInRole("Admin"))
                {
                    editModel.TeacherId = existingCourse.TeacherId;
                }
            }
        }

        // Determine status based on button clicked if specified
        bool isPublish = submitAction?.Split(',', StringSplitOptions.TrimEntries).Any(s => string.Equals(s, "publish", StringComparison.OrdinalIgnoreCase)) == true;
        bool isDraft = submitAction?.Split(',', StringSplitOptions.TrimEntries).Any(s => string.Equals(s, "draft", StringComparison.OrdinalIgnoreCase)) == true;

        if (isDraft)
        {
            editModel.Status = CourseStatus.Draft;
        }
        else if (isPublish)
        {
            editModel.Status = CourseStatus.Published;
        }

        // Validate duplicate course code (excluding current course ID)
        if (!string.IsNullOrWhiteSpace(editModel.Code) && await _courseService.CourseCodeExistsAsync(editModel.Code, editModel.Id))
        {
            ModelState.AddModelError("EditModel.Code", "A course with this course code already exists.");
            ModelState.AddModelError(nameof(editModel.Code), "A course with this course code already exists.");
        }

        // Validate Teacher exists
        if (string.IsNullOrWhiteSpace(editModel.TeacherId))
        {
            ModelState.AddModelError("EditModel.TeacherId", "Please select a teacher.");
            ModelState.AddModelError(nameof(editModel.TeacherId), "Please select a teacher.");
        }
        else
        {
            var teacherExists = await _context.Users.AnyAsync(u => u.Id == editModel.TeacherId);
            if (!teacherExists)
            {
                ModelState.AddModelError("EditModel.TeacherId", "The selected teacher does not exist.");
                ModelState.AddModelError(nameof(editModel.TeacherId), "The selected teacher does not exist.");
            }
        }

        // Validate Major exists
        if (editModel.MajorId <= 0)
        {
            ModelState.AddModelError("EditModel.MajorId", "Please select a major.");
            ModelState.AddModelError(nameof(editModel.MajorId), "Please select a major.");
        }
        else
        {
            var majorExists = await _context.Majors.AnyAsync(m => m.Id == editModel.MajorId);
            if (!majorExists)
            {
                ModelState.AddModelError("EditModel.MajorId", "The selected major does not exist.");
                ModelState.AddModelError(nameof(editModel.MajorId), "The selected major does not exist.");
            }
        }

        // Validate Semester exists
        if (editModel.SemesterId <= 0)
        {
            ModelState.AddModelError("EditModel.SemesterId", "Please select a semester.");
            ModelState.AddModelError(nameof(editModel.SemesterId), "Please select a semester.");
        }
        else
        {
            var semesterExists = await _context.Semesters.AnyAsync(s => s.Id == editModel.SemesterId);
            if (!semesterExists)
            {
                ModelState.AddModelError("EditModel.SemesterId", "The selected semester does not exist.");
                ModelState.AddModelError(nameof(editModel.SemesterId), "The selected semester does not exist.");
            }
        }

        // Validate Category exists if provided
        if (editModel.CategoryId.HasValue && editModel.CategoryId.Value > 0)
        {
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == editModel.CategoryId.Value);
            if (!categoryExists)
            {
                ModelState.AddModelError("EditModel.CategoryId", "The selected category does not exist.");
                ModelState.AddModelError(nameof(editModel.CategoryId), "The selected category does not exist.");
            }
        }

        // Validate Thumbnail URL/path if provided
        if (!string.IsNullOrWhiteSpace(editModel.Thumbnail) && !_htmlSanitizer.IsSafeResourceUrl(editModel.Thumbnail))
        {
            const string thumbErr = "Invalid cover image URL or path. Only http://, https://, or safe relative paths are permitted.";
            ModelState.AddModelError("EditModel.Thumbnail", thumbErr);
            ModelState.AddModelError(nameof(editModel.Thumbnail), thumbErr);
        }

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return Json(new { success = false, message = "Please correct the errors in the form.", errors });
            }

            var vm = await BuildCourseManagementViewModelAsync(existingEditModel: editModel);
            ViewData["Title"] = "Course Management";
            ViewData["ActivePage"] = "Courses";
            ViewBag.OpenEditModal = true;
            return View("Course", vm);
        }

        try
        {
            var course = new Course
            {
                Id = editModel.Id,
                Code = editModel.Code.Trim().ToUpper(),
                Title = editModel.Title.Trim(),
                Description = editModel.Description?.Trim(),
                Thumbnail = string.IsNullOrWhiteSpace(editModel.Thumbnail) ? null : editModel.Thumbnail.Trim(),
                Status = editModel.Status,
                MajorId = editModel.MajorId,
                SemesterId = editModel.SemesterId,
                TeacherId = editModel.TeacherId
            };

            var updated = await _courseService.UpdateCourseAsync(course);
            if (!updated)
            {
                var notFoundMsg = "The course could not be found to update.";
                if (isAjax) return Json(new { success = false, message = notFoundMsg });
                TempData["ErrorMessage"] = notFoundMsg;
                return RedirectToAction(nameof(Course));
            }

            var successMessage = "Course updated successfully.";

            TempData["SuccessMessage"] = successMessage;

            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = successMessage,
                    courseId = course.Id,
                    redirectUrl = Url.Action("Course", "Academic")
                });
            }

            return RedirectToAction(nameof(Course));
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("EditModel.Code", "A course with this course code already exists.");
                ModelState.AddModelError(nameof(editModel.Code), "A course with this course code already exists.");
            }
            ModelState.AddModelError(string.Empty, ex.Message);
            if (isAjax)
            {
                return Json(new { success = false, message = ex.Message });
            }

            var vm = await BuildCourseManagementViewModelAsync(existingEditModel: editModel);
            ViewBag.OpenEditModal = true;
            return View("Course", vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update course ID {Id} ({Code})", editModel.Id, editModel.Code);
            var errorMsg = "An unexpected error occurred while updating the course. Please try again.";

            if (isAjax)
            {
                return Json(new { success = false, message = errorMsg });
            }

            ModelState.AddModelError(string.Empty, errorMsg);
            var vm = await BuildCourseManagementViewModelAsync(existingEditModel: editModel);
            ViewBag.OpenEditModal = true;
            return View("Course", vm);
        }
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCourse(int id)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (id <= 0)
        {
            var invalidMsg = "Invalid course ID.";
            if (isAjax)
            {
                return BadRequest(new { success = false, message = invalidMsg });
            }
            TempData["ErrorMessage"] = invalidMsg;
            return RedirectToAction(nameof(Course));
        }

        try
        {
            var result = await _courseService.DeleteCourseAsync(id);

            switch (result.Status)
            {
                case CourseDeleteStatus.Success:
                    var successMsg = string.IsNullOrWhiteSpace(result.Message)
                        ? "Course deleted successfully."
                        : result.Message;

                    TempData["SuccessMessage"] = successMsg;

                    if (isAjax)
                    {
                        return Json(new
                        {
                            success = true,
                            message = successMsg,
                            courseId = id,
                            redirectUrl = Url?.Action("Course", "Academic") ?? "/Academic/Course"
                        });
                    }

                    return RedirectToAction(nameof(Course));

                case CourseDeleteStatus.NotFound:
                    if (isAjax)
                    {
                        return NotFound(new { success = false, message = result.Message });
                    }

                    TempData["ErrorMessage"] = result.Message;
                    return RedirectToAction(nameof(Course));

                case CourseDeleteStatus.BlockedByStudentData:
                    if (isAjax)
                    {
                        return Json(new
                        {
                            success = false,
                            blocked = true,
                            message = result.Message,
                            courseId = id,
                            enrollments = result.EnrollmentCount,
                            progress = result.ProgressCount,
                            quizSubmissions = result.QuizSubmissionCount,
                            assignmentSubmissions = result.AssignmentSubmissionCount
                        });
                    }

                    TempData["ErrorMessage"] = result.Message;
                    return RedirectToAction(nameof(Course));

                case CourseDeleteStatus.Failed:
                default:
                    if (isAjax)
                    {
                        return Json(new { success = false, message = result.Message });
                    }

                    TempData["ErrorMessage"] = result.Message;
                    return RedirectToAction(nameof(Course));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting course ID {CourseId}", id);
            var errorMsg = "An unexpected error occurred while deleting the course. Please try again.";

            if (isAjax)
            {
                return Json(new { success = false, message = errorMsg });
            }

            TempData["ErrorMessage"] = errorMsg;
            return RedirectToAction(nameof(Course));
        }
    }

    // ==========================================
    // MODULE (CHAPTER) MANAGEMENT ACTIONS
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> Curriculum(int id)
    {
        if (id <= 0)
        {
            TempData["ErrorMessage"] = "Invalid course ID.";
            return RedirectToAction(nameof(Course));
        }

        if (_curriculumService == null)
        {
            TempData["ErrorMessage"] = "Curriculum service is currently unavailable.";
            return RedirectToAction(nameof(Course));
        }

        var viewModel = await _curriculumService.GetCurriculumAsync(id);
        if (viewModel == null)
        {
            TempData["ErrorMessage"] = "Course not found.";
            return RedirectToAction(nameof(Course));
        }

        if (!await CanManageCourseAsync(id))
        {
            return Forbid();
        }

        ViewData["Title"] = $"{viewModel.CourseTitle} - Curriculum";
        ViewData["ActivePage"] = "Courses";

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Chapter(int id) => await Curriculum(id);

    [HttpGet]
    public async Task<IActionResult> GetCourseModulesJson(int courseId)
    {
        if (courseId <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid course ID." });
        }

        if (_curriculumService == null)
        {
            return StatusCode(500, new { success = false, message = "Curriculum service is unavailable." });
        }

        if (!await CanManageCourseAsync(courseId))
        {
            return ForbiddenResult(forceJson: true);
        }

        var modules = await _curriculumService.GetModulesByCourseIdAsync(courseId);
        return Json(new { success = true, courseId, modules });
    }

    [HttpGet]
    public async Task<IActionResult> GetModuleJson(int id)
    {
        if (id <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid module ID." });
        }

        if (_curriculumService == null)
        {
            return StatusCode(500, new { success = false, message = "Curriculum service is unavailable." });
        }

        var chapter = await _curriculumService.GetChapterByIdAsync(id);
        if (chapter == null)
        {
            return NotFound(new { success = false, message = "Module not found." });
        }

        if (!await CanManageCourseAsync(chapter.CourseId))
        {
            return ForbiddenResult(forceJson: true);
        }

        return Json(new
        {
            success = true,
            module = new
            {
                id = chapter.Id,
                courseId = chapter.CourseId,
                title = chapter.Title,
                position = chapter.Position
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateModule(ModuleCreateViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (isAjax)
            {
                return Json(new { success = false, message = "Please provide valid module information.", errors });
            }
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid module data.";
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (_curriculumService == null)
        {
            var serviceErr = "Curriculum service is unavailable.";
            if (isAjax) return Json(new { success = false, message = serviceErr });
            TempData["ErrorMessage"] = serviceErr;
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (model.CourseId > 0 && !await CanManageCourseAsync(model.CourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.CreateChapterAsync(model);

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    moduleId = result.EntityId,
                    moduleTitle = result.EntityTitle,
                    courseId = model.CourseId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = model.CourseId })
                });
            }
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (isAjax)
        {
            return Json(new { success = false, message = result.Message, errors = result.Errors });
        }

        TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditModule(ModuleEditViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (isAjax)
            {
                return Json(new { success = false, message = "Please provide valid module information.", errors });
            }
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid module data.";
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (_curriculumService == null)
        {
            var serviceErr = "Curriculum service is unavailable.";
            if (isAjax) return Json(new { success = false, message = serviceErr });
            TempData["ErrorMessage"] = serviceErr;
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        var resolvedCourseId = await ResolveCourseIdFromChapterAsync(model.Id);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.UpdateChapterAsync(model);

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    moduleId = result.EntityId,
                    moduleTitle = result.EntityTitle,
                    courseId = model.CourseId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = model.CourseId })
                });
            }
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (isAjax)
        {
            return Json(new { success = false, message = result.Message, errors = result.Errors });
        }

        TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteModule(int id, int courseId)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (id <= 0)
        {
            var invalidMsg = "Invalid module ID.";
            if (isAjax) return BadRequest(new { success = false, message = invalidMsg });
            TempData["ErrorMessage"] = invalidMsg;
            return RedirectToAction(nameof(Curriculum), new { id = courseId });
        }

        if (_curriculumService == null)
        {
            var serviceErr = "Curriculum service is unavailable.";
            if (isAjax) return Json(new { success = false, message = serviceErr });
            TempData["ErrorMessage"] = serviceErr;
            return RedirectToAction(nameof(Curriculum), new { id = courseId });
        }

        var resolvedCourseId = await ResolveCourseIdFromChapterAsync(id);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.DeleteChapterAsync(id, courseId > 0 ? courseId : null);

        switch (result.Status)
        {
            case CurriculumOperationStatus.Success:
                TempData["SuccessMessage"] = result.Message;
                if (isAjax)
                {
                    return Json(new
                    {
                        success = true,
                        message = result.Message,
                        moduleId = id,
                        courseId = courseId,
                        redirectUrl = Url.Action(nameof(Curriculum), new { id = courseId })
                    });
                }
                return RedirectToAction(nameof(Curriculum), new { id = courseId });

            case CurriculumOperationStatus.BlockedByStudentData:
                if (isAjax)
                {
                    return Json(new
                    {
                        success = false,
                        blocked = true,
                        message = result.Message,
                        moduleId = id,
                        progressCount = result.ProgressCount,
                        submissionCount = result.SubmissionCount
                    });
                }
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction(nameof(Curriculum), new { id = courseId });

            case CurriculumOperationStatus.NotFound:
                if (isAjax) return NotFound(new { success = false, message = result.Message });
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction(nameof(Curriculum), new { id = courseId });

            case CurriculumOperationStatus.ValidationError:
            case CurriculumOperationStatus.Failed:
            default:
                if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction(nameof(Curriculum), new { id = courseId });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetModuleLessonsJson(int id)
    {
        if (id <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid module ID." });
        }

        if (_curriculumService == null)
        {
            return StatusCode(500, new { success = false, message = "Curriculum service is unavailable." });
        }

        var resolvedCourseId = await ResolveCourseIdFromChapterAsync(id);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult(forceJson: true);
        }

        var lessons = await _curriculumService.GetLessonsByChapterIdAsync(id);
        if (lessons == null)
        {
            return NotFound(new { success = false, message = "Module not found." });
        }

        return Json(new { success = true, moduleId = id, lessons });
    }

    [HttpGet]
    public async Task<IActionResult> GetLessonJson(int id, int? courseId = null, int? chapterId = null)
    {
        if (id <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid lesson ID." });
        }

        if (_curriculumService == null)
        {
            return Json(new { success = false, message = "Curriculum service is unavailable." });
        }

        var lesson = await _curriculumService.GetLessonAsync(id, chapterId, courseId);
        if (lesson == null)
        {
            return NotFound(new { success = false, message = "Lesson not found." });
        }

        if (!await CanManageCourseAsync(lesson.CourseId))
        {
            return ForbiddenResult(forceJson: true);
        }

        return Json(new
        {
            success = true,
            lesson = new
            {
                courseItemId = lesson.CourseItemId,
                lessonId = lesson.LessonId,
                chapterId = lesson.ChapterId,
                courseId = lesson.CourseId,
                title = lesson.Title,
                position = lesson.Position,
                videoUrl = lesson.VideoUrl ?? "",
                contentHtml = lesson.ContentHtml ?? "",
                prerequisiteItemIds = lesson.PrerequisiteItemIds
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(536_870_912)]
    [RequestFormLimits(MultipartBodyLengthLimit = 536_870_912)]
    public async Task<IActionResult> CreateLesson(LessonCreateViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (isAjax)
            {
                return Json(new { success = false, message = "Please provide valid lesson information.", errors });
            }
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid lesson data.";
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (_curriculumService == null)
        {
            var serviceErr = "Curriculum service is unavailable.";
            if (isAjax) return Json(new { success = false, message = serviceErr });
            TempData["ErrorMessage"] = serviceErr;
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        var resolvedCourseId = await ResolveCourseIdFromChapterAsync(model.ChapterId) ?? (model.CourseId > 0 ? model.CourseId : null);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        string? uploadedVideoUrl = null;
        if (model.VideoFile != null && model.VideoFile.Length > 0)
        {
            if (_videoStorageService == null)
            {
                const string err = "Video storage service is unavailable.";
                if (isAjax) return Json(new { success = false, message = err });
                TempData["ErrorMessage"] = err;
                return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
            }

            var saveVideo = await _videoStorageService.SaveVideoFileAsync(model.VideoFile);
            if (!saveVideo.Success)
            {
                if (isAjax) return Json(new { success = false, message = saveVideo.ErrorMessage });
                TempData["ErrorMessage"] = saveVideo.ErrorMessage;
                return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
            }

            uploadedVideoUrl = saveVideo.VideoUrl;
            model.VideoUrl = uploadedVideoUrl;
        }

        var result = await _curriculumService.CreateLessonAsync(model);
        if (!result.IsSuccess && uploadedVideoUrl != null && _videoStorageService != null)
        {
            await _videoStorageService.DeleteVideoFileAsync(uploadedVideoUrl);
        }

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    lessonId = result.EntityId,
                    lessonTitle = result.EntityTitle,
                    chapterId = model.ChapterId,
                    courseId = model.CourseId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = model.CourseId })
                });
            }
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (isAjax)
        {
            return Json(new { success = false, message = result.Message, errors = result.Errors });
        }

        TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(536_870_912)]
    [RequestFormLimits(MultipartBodyLengthLimit = 536_870_912)]
    public async Task<IActionResult> EditLesson(LessonEditViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (isAjax)
            {
                return Json(new { success = false, message = "Please provide valid lesson information.", errors });
            }
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid lesson data.";
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (_curriculumService == null)
        {
            var serviceErr = "Curriculum service is unavailable.";
            if (isAjax) return Json(new { success = false, message = serviceErr });
            TempData["ErrorMessage"] = serviceErr;
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        int targetLessonId = model.CourseItemId > 0 ? model.CourseItemId : model.LessonId;
        var resolvedCourseId = await ResolveCourseIdFromCourseItemAsync(targetLessonId, ItemType.Lesson, alsoCheckLessonId: true);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var existingLesson = await _curriculumService.GetLessonAsync(targetLessonId);
        string? oldVideoUrl = existingLesson?.VideoUrl;
        string? newlyUploadedVideoUrl = null;

        if (model.VideoFile != null && model.VideoFile.Length > 0)
        {
            if (_videoStorageService == null)
            {
                const string err = "Video storage service is unavailable.";
                if (isAjax) return Json(new { success = false, message = err });
                TempData["ErrorMessage"] = err;
                return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
            }

            var saveVideo = await _videoStorageService.SaveVideoFileAsync(model.VideoFile);
            if (!saveVideo.Success)
            {
                if (isAjax) return Json(new { success = false, message = saveVideo.ErrorMessage });
                TempData["ErrorMessage"] = saveVideo.ErrorMessage;
                return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
            }

            newlyUploadedVideoUrl = saveVideo.VideoUrl;
            model.VideoUrl = newlyUploadedVideoUrl;
        }
        else if (model.RemoveVideo)
        {
            model.VideoUrl = null;
        }

        var result = await _curriculumService.UpdateLessonAsync(model);
        if (result.IsSuccess)
        {
            if (_videoStorageService != null && !string.IsNullOrWhiteSpace(oldVideoUrl) &&
                (newlyUploadedVideoUrl != null || model.RemoveVideo))
            {
                await _videoStorageService.DeleteVideoFileAsync(oldVideoUrl);
            }
        }
        else if (newlyUploadedVideoUrl != null && _videoStorageService != null)
        {
            await _videoStorageService.DeleteVideoFileAsync(newlyUploadedVideoUrl);
        }

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    lessonId = result.EntityId,
                    lessonTitle = result.EntityTitle,
                    courseId = model.CourseId,
                    chapterId = model.ChapterId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = model.CourseId })
                });
            }
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (isAjax)
        {
            return Json(new { success = false, message = result.Message, errors = result.Errors });
        }

        TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCourseAccessMode(int courseId, CourseAccessMode mode)
    {
        if (courseId <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid course ID." });
        }

        if (!await CanManageCourseAsync(courseId))
        {
            return ForbiddenResult(forceJson: true);
        }

        var currentUserId = GetCurrentUserId() ?? string.Empty;
        var isAdmin = User.IsInRole("Admin");

        var result = await _curriculumService.UpdateCourseAccessModeAsync(courseId, mode, currentUserId, isAdmin);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            return Json(new { success = true, message = result.Message, mode = mode.ToString() });
        }

        return BadRequest(new { success = false, message = result.Message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateItemPrerequisites(int courseItemId, [FromForm] List<int>? prerequisiteItemIds)
    {
        if (courseItemId <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid course item ID." });
        }

        var item = await _context.CourseItems
            .Include(ci => ci.Chapter)
            .AsNoTracking()
            .FirstOrDefaultAsync(ci => ci.Id == courseItemId);

        if (item == null || item.Chapter == null)
        {
            return NotFound(new { success = false, message = "Course item not found." });
        }

        if (!await CanManageCourseAsync(item.Chapter.CourseId))
        {
            return ForbiddenResult(forceJson: true);
        }

        var currentUserId = GetCurrentUserId() ?? string.Empty;
        var isAdmin = User.IsInRole("Admin");

        var result = await _curriculumService.SetItemPrerequisitesAsync(courseItemId, prerequisiteItemIds ?? new List<int>(), currentUserId, isAdmin);
        if (result.IsSuccess)
        {
            return Json(new { success = true, message = result.Message, courseItemId });
        }

        return BadRequest(new { success = false, message = result.Message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLesson(int id, int courseId = 0, int? chapterId = null)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (id <= 0)
        {
            var invalidMsg = "Invalid lesson ID.";
            if (isAjax) return BadRequest(new { success = false, message = invalidMsg });
            TempData["ErrorMessage"] = invalidMsg;
            return courseId > 0
                ? RedirectToAction(nameof(Curriculum), new { id = courseId })
                : RedirectToAction(nameof(Course));
        }

        if (_curriculumService == null)
        {
            var serviceErr = "Curriculum service is unavailable.";
            if (isAjax) return Json(new { success = false, message = serviceErr });
            TempData["ErrorMessage"] = serviceErr;
            return courseId > 0
                ? RedirectToAction(nameof(Curriculum), new { id = courseId })
                : RedirectToAction(nameof(Course));
        }

        var resolvedCourseId = await ResolveCourseIdFromCourseItemAsync(id, ItemType.Lesson, alsoCheckLessonId: true);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.DeleteLessonAsync(
            id,
            chapterId > 0 ? chapterId : null,
            courseId > 0 ? courseId : null);

        switch (result.Status)
        {
            case CurriculumOperationStatus.Success:
                TempData["SuccessMessage"] = result.Message;
                if (isAjax)
                {
                    return Json(new
                    {
                        success = true,
                        message = result.Message,
                        lessonId = id,
                        courseId = courseId,
                        redirectUrl = Url.Action(nameof(Curriculum), new { id = courseId })
                    });
                }
                return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));

            case CurriculumOperationStatus.BlockedByStudentData:
                if (isAjax)
                {
                    return Json(new
                    {
                        success = false,
                        blocked = true,
                        message = result.Message,
                        lessonId = id,
                        progressCount = result.ProgressCount
                    });
                }
                TempData["ErrorMessage"] = result.Message;
                return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));

            case CurriculumOperationStatus.NotFound:
                if (isAjax) return NotFound(new { success = false, message = result.Message });
                TempData["ErrorMessage"] = result.Message;
                return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));

            case CurriculumOperationStatus.ValidationError:
            case CurriculumOperationStatus.Failed:
            default:
                if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
                TempData["ErrorMessage"] = result.Message;
                return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));
        }
    }

    // =========================================================================
    // QUIZ MANAGEMENT ACTIONS
    // =========================================================================

    [HttpGet]
    public async Task<IActionResult> GetQuizJson(int id, int? courseId = null, int? chapterId = null)
    {
        if (id <= 0) return BadRequest(new { success = false, message = "Invalid quiz ID." });
        var quiz = await _curriculumService.GetQuizAsync(id, chapterId, courseId);
        if (quiz == null) return NotFound(new { success = false, message = "Quiz not found." });
        if (!await CanManageCourseAsync(quiz.CourseId)) return ForbiddenResult(forceJson: true);
        return Json(new { success = true, quiz });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateQuiz(QuizCreateViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (isAjax) return Json(new { success = false, message = "Please provide valid quiz information.", errors });
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid quiz data.";
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        var resolvedCourseId = await ResolveCourseIdFromChapterAsync(model.ChapterId) ?? (model.CourseId > 0 ? model.CourseId : null);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.CreateQuizAsync(model);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    courseItemId = result.EntityId,
                    courseId = model.CourseId,
                    chapterId = model.ChapterId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = model.CourseId })
                });
            }
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
        TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditQuiz(QuizEditViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (isAjax) return Json(new { success = false, message = "Please provide valid quiz information.", errors });
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid quiz data.";
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        var resolvedCourseId = await ResolveCourseIdFromCourseItemAsync(model.CourseItemId, ItemType.Quiz);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.UpdateQuizAsync(model);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    courseItemId = model.CourseItemId,
                    courseId = model.CourseId,
                    chapterId = model.ChapterId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = model.CourseId })
                });
            }
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
        TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuiz(int id, int courseId = 0, int? chapterId = null)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        if (id <= 0)
        {
            var invalidMsg = "Invalid quiz ID.";
            if (isAjax) return BadRequest(new { success = false, message = invalidMsg });
            TempData["ErrorMessage"] = invalidMsg;
            return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));
        }

        var resolvedCourseId = await ResolveCourseIdFromCourseItemAsync(id, ItemType.Quiz);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.DeleteQuizAsync(id, chapterId > 0 ? chapterId : null, courseId > 0 ? courseId : null);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    courseItemId = id,
                    courseId = courseId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = courseId })
                });
            }
            return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));
        }

        if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
        TempData["ErrorMessage"] = result.Message;
        return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));
    }

    // ==========================================
    // RESOURCE / BOOK ACTIONS
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> GetBookJson(int id)
    {
        if (id <= 0) return BadRequest(new { success = false, message = "Invalid resource ID." });
        var book = await _curriculumService.GetBookByIdAsync(id);
        if (book == null) return NotFound(new { success = false, message = "Resource not found." });
        if (!await CanManageCourseAsync(book.CourseId)) return ForbiddenResult(forceJson: true);
        return Json(new { success = true, book });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBook(BookCreateViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (isAjax) return Json(new { success = false, message = "Please provide valid resource information.", errors });
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid resource data.";
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        var resolvedCourseId = await ResolveCourseIdFromChapterAsync(model.ChapterId) ?? (model.CourseId > 0 ? model.CourseId : null);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.CreateBookAsync(model);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    courseItemId = result.EntityId,
                    courseId = model.CourseId,
                    chapterId = model.ChapterId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = model.CourseId })
                });
            }
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
        TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBook(BookEditViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (isAjax) return Json(new { success = false, message = "Please provide valid resource information.", errors });
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid resource data.";
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        var resolvedCourseId = await ResolveCourseIdFromCourseItemAsync(model.CourseItemId);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.UpdateBookAsync(model);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    courseItemId = model.CourseItemId,
                    courseId = model.CourseId,
                    chapterId = model.ChapterId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = model.CourseId })
                });
            }
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
        TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBook(int id, int courseId = 0, int? chapterId = null)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        if (id <= 0)
        {
            var invalidMsg = "Invalid resource ID.";
            if (isAjax) return BadRequest(new { success = false, message = invalidMsg });
            TempData["ErrorMessage"] = invalidMsg;
            return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));
        }

        var resolvedCourseId = await ResolveCourseIdFromCourseItemAsync(id);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.DeleteBookAsync(id, chapterId > 0 ? chapterId : null, courseId > 0 ? courseId : null);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    courseItemId = id,
                    courseId = courseId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = courseId })
                });
            }
            return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));
        }

        if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
        TempData["ErrorMessage"] = result.Message;
        return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));
    }

    // ==========================================
    // ASSIGNMENT ACTIONS
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> GetAssignmentJson(int id)
    {
        if (id <= 0) return BadRequest(new { success = false, message = "Invalid assignment ID." });
        var assignment = await _curriculumService.GetAssignmentByIdAsync(id);
        if (assignment == null) return NotFound(new { success = false, message = "Assignment not found." });
        if (!await CanManageCourseAsync(assignment.CourseId)) return ForbiddenResult(forceJson: true);
        return Json(new { success = true, assignment });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAssignment(AssignmentCreateViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (isAjax) return Json(new { success = false, message = "Please provide valid assignment information.", errors });
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid assignment data.";
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        var resolvedCourseId = await ResolveCourseIdFromChapterAsync(model.ChapterId) ?? (model.CourseId > 0 ? model.CourseId : null);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.CreateAssignmentAsync(model);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    courseItemId = result.EntityId,
                    courseId = model.CourseId,
                    chapterId = model.ChapterId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = model.CourseId })
                });
            }
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
        TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAssignment(AssignmentEditViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (isAjax) return Json(new { success = false, message = "Please provide valid assignment information.", errors });
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid assignment data.";
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        var resolvedCourseId = await ResolveCourseIdFromCourseItemAsync(model.CourseItemId);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.UpdateAssignmentAsync(model);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    courseItemId = model.CourseItemId,
                    courseId = model.CourseId,
                    chapterId = model.ChapterId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = model.CourseId })
                });
            }
            return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
        }

        if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
        TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAssignment(int id, int courseId = 0, int? chapterId = null)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        if (id <= 0)
        {
            var invalidMsg = "Invalid assignment ID.";
            if (isAjax) return BadRequest(new { success = false, message = invalidMsg });
            TempData["ErrorMessage"] = invalidMsg;
            return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));
        }

        var resolvedCourseId = await ResolveCourseIdFromCourseItemAsync(id);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult();
        }

        var result = await _curriculumService.DeleteAssignmentAsync(id, chapterId > 0 ? chapterId : null, courseId > 0 ? courseId : null);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    courseItemId = id,
                    courseId = courseId,
                    redirectUrl = Url.Action(nameof(Curriculum), new { id = courseId })
                });
            }
            return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));
        }

        if (isAjax) return Json(new { success = false, message = result.Message, errors = result.Errors });
        TempData["ErrorMessage"] = result.Message;
        return courseId > 0 ? RedirectToAction(nameof(Curriculum), new { id = courseId }) : RedirectToAction(nameof(Course));
    }

    // =========================================================================
    // QUIZ QUESTIONS MANAGEMENT ACTIONS
    // =========================================================================

    [HttpGet]
    public async Task<IActionResult> QuizQuestions(int id)
    {
        if (id <= 0) return NotFound("Invalid Quiz ID.");

        var model = await _curriculumService.GetQuizQuestionsAsync(id);
        if (model == null)
        {
            // Check if id is CourseItemId
            var quiz = await _context.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.CourseItemId == id);
            if (quiz != null)
            {
                model = await _curriculumService.GetQuizQuestionsAsync(quiz.Id);
            }
        }

        if (model == null)
        {
            TempData["ErrorMessage"] = "Quiz not found.";
            return RedirectToAction(nameof(Course));
        }

        if (!await CanManageCourseAsync(model.CourseId))
        {
            return Forbid();
        }

        ViewData["Title"] = $"Manage Questions: {model.QuizTitle}";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> GetQuestionJson(int id)
    {
        if (id <= 0) return BadRequest(new { success = false, message = "Invalid question ID." });
        var resolvedCourseId = await ResolveCourseIdFromQuestionAsync(id);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return ForbiddenResult(forceJson: true);
        }

        var question = await _curriculumService.GetQuestionByIdAsync(id);
        if (question == null) return NotFound(new { success = false, message = "Question not found." });
        return Json(new { success = true, question });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddQuestion(QuestionViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid question data.";
            return RedirectToAction(nameof(QuizQuestions), new { id = model.QuizId });
        }

        var resolvedCourseId = await ResolveCourseIdFromQuizAsync(model.QuizId);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return Forbid();
        }

        var result = await _curriculumService.AddQuestionAsync(model.QuizId, model);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }
        return RedirectToAction(nameof(QuizQuestions), new { id = model.QuizId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditQuestion(QuestionViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            TempData["ErrorMessage"] = errors.FirstOrDefault() ?? "Invalid question data.";
            return RedirectToAction(nameof(QuizQuestions), new { id = model.QuizId });
        }

        var resolvedCourseId = await ResolveCourseIdFromQuestionAsync(model.Id);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return Forbid();
        }

        var quizCourseId = await ResolveCourseIdFromQuizAsync(model.QuizId);
        if (quizCourseId.HasValue && !await CanManageCourseAsync(quizCourseId))
        {
            return Forbid();
        }

        var result = await _curriculumService.UpdateQuestionAsync(model);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }
        return RedirectToAction(nameof(QuizQuestions), new { id = model.QuizId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int id, int quizId)
    {
        if (id <= 0 || quizId <= 0)
        {
            TempData["ErrorMessage"] = "Invalid Question ID or Quiz ID.";
            return RedirectToAction(nameof(QuizQuestions), new { id = quizId });
        }

        var resolvedCourseId = await ResolveCourseIdFromQuestionAsync(id);
        if (resolvedCourseId.HasValue && !await CanManageCourseAsync(resolvedCourseId))
        {
            return Forbid();
        }

        var result = await _curriculumService.DeleteQuestionAsync(id, quizId);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }
        return RedirectToAction(nameof(QuizQuestions), new { id = quizId });
    }

    private async Task<CourseManagementViewModel> BuildCourseManagementViewModelAsync(
        CourseCreateViewModel? existingCreateModel = null,
        CourseEditViewModel? existingEditModel = null)
    {
        var courses = (await _courseService.GetAllCoursesAsync()).ToList();
        var createModel = existingCreateModel ?? new CourseCreateViewModel();
        await PopulateDropdownsAsync(createModel);

        var editModel = existingEditModel ?? new CourseEditViewModel();
        await PopulateEditDropdownsAsync(editModel);

        return new CourseManagementViewModel
        {
            Courses = courses,
            CreateModel = createModel,
            EditModel = editModel
        };
    }

    // Lookup & Dropdown population helper for Create
    private async Task PopulateDropdownsAsync(CourseCreateViewModel model)
    {
        model.MajorOptions = await _context.Majors
            .OrderBy(m => m.Name)
            .Select(m => new SelectListItem
            {
                Value = m.Id.ToString(),
                Text = $"{m.Name} ({m.Code})"
            })
            .ToListAsync();

        model.SemesterOptions = await _context.Semesters
            .OrderByDescending(s => s.StartDate)
            .Select(s => new SelectListItem
            {
                Value = s.Id.ToString(),
                Text = $"{s.Name} ({s.Code})" + (s.IsCurrent ? " [Current]" : "")
            })
            .ToListAsync();

        var teachers = await _userManager.GetUsersInRoleAsync("Teacher");
        if (teachers.Any())
        {
            model.TeacherOptions = teachers
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .Select(u => new SelectListItem
                {
                    Value = u.Id,
                    Text = $"{u.FullName} ({(string.IsNullOrEmpty(u.UniversityId) ? u.Email : u.UniversityId)})"
                })
                .ToList();
        }
        else
        {
            model.TeacherOptions = await _context.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .Select(u => new SelectListItem
                {
                    Value = u.Id,
                    Text = $"{u.FullName} ({(string.IsNullOrEmpty(u.UniversityId) ? u.Email : u.UniversityId)})"
                })
                .ToListAsync();
        }

        model.CategoryOptions = await _context.Categories
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name
            })
            .ToListAsync();
    }

    // Lookup & Dropdown population helper for Edit
    private async Task PopulateEditDropdownsAsync(CourseEditViewModel model)
    {
        model.MajorOptions = await _context.Majors
            .OrderBy(m => m.Name)
            .Select(m => new SelectListItem
            {
                Value = m.Id.ToString(),
                Text = $"{m.Name} ({m.Code})"
            })
            .ToListAsync();

        model.SemesterOptions = await _context.Semesters
            .OrderByDescending(s => s.StartDate)
            .Select(s => new SelectListItem
            {
                Value = s.Id.ToString(),
                Text = $"{s.Name} ({s.Code})" + (s.IsCurrent ? " [Current]" : "")
            })
            .ToListAsync();

        var teachers = await _userManager.GetUsersInRoleAsync("Teacher");
        if (teachers.Any())
        {
            model.TeacherOptions = teachers
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .Select(u => new SelectListItem
                {
                    Value = u.Id,
                    Text = $"{u.FullName} ({(string.IsNullOrEmpty(u.UniversityId) ? u.Email : u.UniversityId)})"
                })
                .ToList();
        }
        else
        {
            model.TeacherOptions = await _context.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .Select(u => new SelectListItem
                {
                    Value = u.Id,
                    Text = $"{u.FullName} ({(string.IsNullOrEmpty(u.UniversityId) ? u.Email : u.UniversityId)})"
                })
                .ToListAsync();
        }

        model.CategoryOptions = await _context.Categories
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name
            })
            .ToListAsync();
    }

    // =========================================================================
    // PHASE 4 MODULE F — TEACHING ASSISTANT MANAGEMENT (ADMIN ONLY)
    // =========================================================================

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetCourseAssistantsJson(int id)
    {
        if (id <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid course ID." });
        }

        var course = await _context.Courses
            .Include(c => c.Teacher)
            .Include(c => c.CourseAssistants)
                .ThenInclude(ca => ca.Assistant)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (course == null)
        {
            return NotFound(new { success = false, message = "Course not found." });
        }

        var assistants = course.CourseAssistants.Select(ca => new
        {
            id = ca.Id,
            assistantId = ca.AssistantId,
            name = ca.Assistant.FullName,
            email = ca.Assistant.Email ?? "",
            universityId = ca.Assistant.UniversityId ?? "N/A",
            canGrade = ca.CanGrade,
            assignedAt = ca.AssignedAt.ToString("MMM dd, yyyy")
        }).ToList();

        var assignedAssistantIds = course.CourseAssistants.Select(ca => ca.AssistantId).ToHashSet();
        var allAssistants = await _userManager.GetUsersInRoleAsync("Assistance");
        var availableCandidates = allAssistants
            .Where(u => u.IsActive && !assignedAssistantIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                id = u.Id,
                name = u.FullName,
                email = u.Email ?? "",
                universityId = u.UniversityId ?? ""
            }).ToList();

        return Json(new
        {
            success = true,
            courseId = course.Id,
            courseCode = course.Code,
            courseTitle = course.Title,
            teacherName = course.Teacher?.FullName ?? "Unassigned",
            teacherEmail = course.Teacher?.Email ?? "",
            assistants,
            availableCandidates
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignCourseAssistant(int courseId, string assistantId)
    {
        if (courseId <= 0 || string.IsNullOrWhiteSpace(assistantId))
        {
            return BadRequest(new { success = false, message = "Invalid course or assistant ID." });
        }

        var courseExists = await _context.Courses.AnyAsync(c => c.Id == courseId);
        if (!courseExists)
        {
            return NotFound(new { success = false, message = "Course not found." });
        }

        var assistantUser = await _userManager.FindByIdAsync(assistantId);
        if (assistantUser == null || !assistantUser.IsActive)
        {
            return NotFound(new { success = false, message = "Teaching Assistant account not found or inactive." });
        }

        var isAssistantRole = await _userManager.IsInRoleAsync(assistantUser, "Assistance");
        if (!isAssistantRole)
        {
            return BadRequest(new { success = false, message = "The selected user does not hold the Assistance role." });
        }

        var alreadyAssigned = await _context.CourseAssistants
            .AnyAsync(ca => ca.CourseId == courseId && ca.AssistantId == assistantId);
        if (alreadyAssigned)
        {
            return BadRequest(new { success = false, message = "This Teaching Assistant is already assigned to this course." });
        }

        var assignment = new CourseAssistant
        {
            CourseId = courseId,
            AssistantId = assistantId,
            CanGrade = false, // Owner-approved rule: newly assigned Assistants default to CanGrade = false
            AssignedAt = DateTime.UtcNow
        };

        _context.CourseAssistants.Add(assignment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin assigned Assistant {AssistantId} to Course {CourseId} (CanGrade=false)", assistantId, courseId);

        return Json(new { success = true, message = "Teaching Assistant assigned successfully." });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignCourseAssistant(int courseId, string assistantId)
    {
        if (courseId <= 0 || string.IsNullOrWhiteSpace(assistantId))
        {
            return BadRequest(new { success = false, message = "Invalid course or assistant ID." });
        }

        var assignment = await _context.CourseAssistants
            .FirstOrDefaultAsync(ca => ca.CourseId == courseId && ca.AssistantId == assistantId);

        if (assignment == null)
        {
            return NotFound(new { success = false, message = "Teaching Assistant assignment not found." });
        }

        _context.CourseAssistants.Remove(assignment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin unassigned Assistant {AssistantId} from Course {CourseId}", assistantId, courseId);

        return Json(new { success = true, message = "Teaching Assistant unassigned successfully." });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAssistantCanGrade(int courseId, string assistantId, bool canGrade)
    {
        if (courseId <= 0 || string.IsNullOrWhiteSpace(assistantId))
        {
            return BadRequest(new { success = false, message = "Invalid course or assistant ID." });
        }

        var assignment = await _context.CourseAssistants
            .FirstOrDefaultAsync(ca => ca.CourseId == courseId && ca.AssistantId == assistantId);

        if (assignment == null)
        {
            return NotFound(new { success = false, message = "Teaching Assistant assignment not found." });
        }

        assignment.CanGrade = canGrade;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin toggled CanGrade={CanGrade} for Assistant {AssistantId} in Course {CourseId}", canGrade, assistantId, courseId);

        return Json(new { success = true, canGrade = assignment.CanGrade, message = $"Grading authority {(canGrade ? "granted" : "revoked")}." });
    }

    // =========================================================================
    // TASK-P2-01 — ACADEMIC LOOKUP CRUD (CATEGORIES, MAJORS, SEMESTERS)
    // =========================================================================

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> Category()
    {
        ViewData["Title"] = "Academic Categories";
        ViewData["ActivePage"] = "Academic";

        var categories = await _context.Categories
            .AsNoTracking()
            .Include(c => c.Majors)
            .OrderBy(c => c.Name)
            .ToListAsync();

        return View(categories);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 100)
        {
            TempData["ErrorMessage"] = "Category name is required and must be 100 characters or fewer.";
            return RedirectToAction(nameof(Category));
        }

        var normalized = trimmed.ToUpperInvariant();
        if (await _context.Categories.AnyAsync(c => c.Name.ToUpper() == normalized))
        {
            TempData["ErrorMessage"] = $"Category '{trimmed}' already exists.";
            return RedirectToAction(nameof(Category));
        }

        _context.Categories.Add(new Category { Name = trimmed });
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Category '{trimmed}' created successfully.";
        return RedirectToAction(nameof(Category));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(int id, string name)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            TempData["ErrorMessage"] = "Category not found.";
            return RedirectToAction(nameof(Category));
        }

        var trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 100)
        {
            TempData["ErrorMessage"] = "Category name is required and must be 100 characters or fewer.";
            return RedirectToAction(nameof(Category));
        }

        var normalized = trimmed.ToUpperInvariant();
        if (await _context.Categories.AnyAsync(c => c.Id != id && c.Name.ToUpper() == normalized))
        {
            TempData["ErrorMessage"] = $"Another category named '{trimmed}' already exists.";
            return RedirectToAction(nameof(Category));
        }

        category.Name = trimmed;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Category '{trimmed}' updated successfully.";
        return RedirectToAction(nameof(Category));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var category = await _context.Categories
            .Include(c => c.Majors)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            TempData["ErrorMessage"] = "Category not found.";
            return RedirectToAction(nameof(Category));
        }

        if (category.Majors.Any())
        {
            TempData["ErrorMessage"] = $"Cannot delete category '{category.Name}' because {category.Majors.Count} major(s) reference it.";
            return RedirectToAction(nameof(Category));
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Category '{category.Name}' deleted.";
        return RedirectToAction(nameof(Category));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> Major()
    {
        ViewData["Title"] = "Academic Majors";
        ViewData["ActivePage"] = "Academic";

        var majors = await _context.Majors
            .AsNoTracking()
            .Include(m => m.Category)
            .Include(m => m.Courses)
            .OrderBy(m => m.Code)
            .ToListAsync();

        ViewBag.Categories = await _context.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();

        return View(majors);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMajor(string name, string code, int categoryId)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        var normalizedCode = (code?.Trim() ?? string.Empty).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 100 ||
            string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length > 20 || categoryId <= 0)
        {
            TempData["ErrorMessage"] = "Valid major name, code (max 20 chars), and category are required.";
            return RedirectToAction(nameof(Major));
        }

        if (!await _context.Categories.AnyAsync(c => c.Id == categoryId))
        {
            TempData["ErrorMessage"] = "Selected category does not exist.";
            return RedirectToAction(nameof(Major));
        }

        if (await _context.Majors.AnyAsync(m => m.Code.ToUpper() == normalizedCode))
        {
            TempData["ErrorMessage"] = $"Major code '{normalizedCode}' already exists.";
            return RedirectToAction(nameof(Major));
        }

        _context.Majors.Add(new Major { Name = trimmedName, Code = normalizedCode, CategoryId = categoryId });
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Major '{normalizedCode} - {trimmedName}' created successfully.";
        return RedirectToAction(nameof(Major));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditMajor(int id, string name, string code, int categoryId)
    {
        var major = await _context.Majors.FindAsync(id);
        if (major == null)
        {
            TempData["ErrorMessage"] = "Major not found.";
            return RedirectToAction(nameof(Major));
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        var normalizedCode = (code?.Trim() ?? string.Empty).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 100 ||
            string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length > 20 || categoryId <= 0)
        {
            TempData["ErrorMessage"] = "Valid major name, code (max 20 chars), and category are required.";
            return RedirectToAction(nameof(Major));
        }

        if (!await _context.Categories.AnyAsync(c => c.Id == categoryId))
        {
            TempData["ErrorMessage"] = "Selected category does not exist.";
            return RedirectToAction(nameof(Major));
        }

        if (await _context.Majors.AnyAsync(m => m.Id != id && m.Code.ToUpper() == normalizedCode))
        {
            TempData["ErrorMessage"] = $"Another major with code '{normalizedCode}' already exists.";
            return RedirectToAction(nameof(Major));
        }

        major.Name = trimmedName;
        major.Code = normalizedCode;
        major.CategoryId = categoryId;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Major '{normalizedCode} - {trimmedName}' updated successfully.";
        return RedirectToAction(nameof(Major));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMajor(int id)
    {
        var major = await _context.Majors
            .Include(m => m.Courses)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (major == null)
        {
            TempData["ErrorMessage"] = "Major not found.";
            return RedirectToAction(nameof(Major));
        }

        if (major.Courses.Any())
        {
            TempData["ErrorMessage"] = $"Cannot delete major '{major.Code}' because {major.Courses.Count} course(s) reference it.";
            return RedirectToAction(nameof(Major));
        }

        _context.Majors.Remove(major);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Major '{major.Code}' deleted.";
        return RedirectToAction(nameof(Major));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> Semester()
    {
        ViewData["Title"] = "Academic Semesters";
        ViewData["ActivePage"] = "Academic";

        var semesters = await _context.Semesters
            .AsNoTracking()
            .Include(s => s.Courses)
            .OrderByDescending(s => s.StartDate)
            .ToListAsync();

        return View(semesters);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSemester(string name, string code, DateTime startDate, DateTime endDate, bool isCurrent = false)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        var normalizedCode = (code?.Trim() ?? string.Empty).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 50 ||
            string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length > 20)
        {
            TempData["ErrorMessage"] = "Valid semester name (max 50 chars) and code (max 20 chars) are required.";
            return RedirectToAction(nameof(Semester));
        }

        if (endDate <= startDate)
        {
            TempData["ErrorMessage"] = "End date must be after start date.";
            return RedirectToAction(nameof(Semester));
        }

        if (await _context.Semesters.AnyAsync(s => s.Code.ToUpper() == normalizedCode))
        {
            TempData["ErrorMessage"] = $"Semester code '{normalizedCode}' already exists.";
            return RedirectToAction(nameof(Semester));
        }

        using var tx = await _context.Database.BeginTransactionAsync();
        if (isCurrent)
        {
            var currentSemesters = await _context.Semesters.Where(s => s.IsCurrent).ToListAsync();
            foreach (var s in currentSemesters) s.IsCurrent = false;
        }

        _context.Semesters.Add(new Semester
        {
            Name = trimmedName,
            Code = normalizedCode,
            StartDate = startDate,
            EndDate = endDate,
            IsCurrent = isCurrent
        });
        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["SuccessMessage"] = $"Semester '{normalizedCode} - {trimmedName}' created successfully.";
        return RedirectToAction(nameof(Semester));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditSemester(int id, string name, string code, DateTime startDate, DateTime endDate, bool isCurrent = false)
    {
        var semester = await _context.Semesters.FindAsync(id);
        if (semester == null)
        {
            TempData["ErrorMessage"] = "Semester not found.";
            return RedirectToAction(nameof(Semester));
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        var normalizedCode = (code?.Trim() ?? string.Empty).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 50 ||
            string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length > 20)
        {
            TempData["ErrorMessage"] = "Valid semester name (max 50 chars) and code (max 20 chars) are required.";
            return RedirectToAction(nameof(Semester));
        }

        if (endDate <= startDate)
        {
            TempData["ErrorMessage"] = "End date must be after start date.";
            return RedirectToAction(nameof(Semester));
        }

        if (await _context.Semesters.AnyAsync(s => s.Id != id && s.Code.ToUpper() == normalizedCode))
        {
            TempData["ErrorMessage"] = $"Another semester with code '{normalizedCode}' already exists.";
            return RedirectToAction(nameof(Semester));
        }

        using var tx = await _context.Database.BeginTransactionAsync();
        if (isCurrent)
        {
            var others = await _context.Semesters.Where(s => s.Id != id && s.IsCurrent).ToListAsync();
            foreach (var s in others) s.IsCurrent = false;
        }

        semester.Name = trimmedName;
        semester.Code = normalizedCode;
        semester.StartDate = startDate;
        semester.EndDate = endDate;
        semester.IsCurrent = isCurrent;
        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["SuccessMessage"] = $"Semester '{normalizedCode} - {trimmedName}' updated successfully.";
        return RedirectToAction(nameof(Semester));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCurrentSemester(int id)
    {
        var target = await _context.Semesters.FindAsync(id);
        if (target == null)
        {
            TempData["ErrorMessage"] = "Semester not found.";
            return RedirectToAction(nameof(Semester));
        }

        using var tx = await _context.Database.BeginTransactionAsync();
        var all = await _context.Semesters.ToListAsync();
        foreach (var s in all) s.IsCurrent = (s.Id == id);
        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["SuccessMessage"] = $"'{target.Name}' is now the active current semester.";
        return RedirectToAction(nameof(Semester));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSemester(int id)
    {
        var semester = await _context.Semesters
            .Include(s => s.Courses)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (semester == null)
        {
            TempData["ErrorMessage"] = "Semester not found.";
            return RedirectToAction(nameof(Semester));
        }

        if (semester.Courses.Any())
        {
            TempData["ErrorMessage"] = $"Cannot delete semester '{semester.Code}' because {semester.Courses.Count} course(s) reference it.";
            return RedirectToAction(nameof(Semester));
        }

        _context.Semesters.Remove(semester);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Semester '{semester.Code}' deleted.";
        return RedirectToAction(nameof(Semester));
    }
}
