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

[Authorize(Roles = "Admin")]
public class AcademicController : Controller
{
    private readonly ICourseService _courseService;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<AcademicController> _logger;
    private readonly ICurriculumService _curriculumService;

    public AcademicController(
        ICourseService courseService,
        ApplicationDbContext context,
        UserManager<User> userManager,
        ILogger<AcademicController> logger,
        ICurriculumService? curriculumService = null)
    {
        _courseService = courseService;
        _context = context;
        _userManager = userManager;
        _logger = logger;
        _curriculumService = curriculumService ?? new AUBDigitalAcademy.Services.Implementations.CurriculumService(context, new LoggerFactory().CreateLogger<AUBDigitalAcademy.Services.Implementations.CurriculumService>());
    }

    [HttpGet]
    public async Task<IActionResult> Course()
    {
        ViewData["Title"] = "Course Management";
        ViewData["ActivePage"] = "Courses";

        var viewModel = await BuildCourseManagementViewModelAsync();
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> CreateCourse()
    {
        ViewData["Title"] = "Add New Course";
        ViewData["ActivePage"] = "Courses";

        var model = new CourseCreateViewModel();
        await PopulateDropdownsAsync(model);
        return View(model);
    }

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
                contentHtml = lesson.ContentHtml ?? ""
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
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

        var result = await _curriculumService.CreateLessonAsync(model);

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

        var result = await _curriculumService.UpdateLessonAsync(model);

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

        ViewData["Title"] = $"Manage Questions: {model.QuizTitle}";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> GetQuestionJson(int id)
    {
        if (id <= 0) return BadRequest(new { success = false, message = "Invalid question ID." });
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
}
