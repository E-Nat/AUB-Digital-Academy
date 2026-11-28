using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Controllers;

[Authorize(Roles = "Student,Teacher,Assistance,Admin")]
public class DiscussionController : Controller
{
    private readonly IDiscussionService _discussionService;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<DiscussionController> _logger;

    public DiscussionController(
        IDiscussionService discussionService,
        UserManager<User> userManager,
        ILogger<DiscussionController> logger)
    {
        _discussionService = discussionService;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    [Route("Discussion/Lesson/{courseItemId:int}")]
    public async Task<IActionResult> GetLessonDiscussions(int courseItemId)
    {
        var userId = _userManager.GetUserId(User);
        var viewModel = await _discussionService.GetLessonDiscussionsAsync(courseItemId, userId);
        return Json(new { success = true, data = viewModel });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Discussion/CreateThread")]
    public async Task<IActionResult> CreateThread([FromForm] CreateThreadInputModel model)
    {
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Invalid input.";
            return BadRequest(new { success = false, message = firstError });
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var result = await _discussionService.CreateThreadAsync(model.CourseItemId, userId, model.Title, model.Content);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = result.Message });
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return Ok(result);
        }

        TempData["SuccessMessage"] = result.Message;
        return Redirect(Request.Headers["Referer"].ToString() ?? "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Discussion/CreateReply")]
    public async Task<IActionResult> CreateReply([FromForm] CreateReplyInputModel model)
    {
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Invalid input.";
            return BadRequest(new { success = false, message = firstError });
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var result = await _discussionService.CreateReplyAsync(model.ThreadId, userId, model.Content);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = result.Message });
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return Ok(result);
        }

        TempData["SuccessMessage"] = result.Message;
        return Redirect(Request.Headers["Referer"].ToString() ?? "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Discussion/TogglePinThread")]
    public async Task<IActionResult> TogglePinThread([FromForm] int threadId)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var result = await _discussionService.TogglePinThreadAsync(threadId, userId);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = result.Message });
        }

        return Ok(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Discussion/ToggleAnsweredThread")]
    public async Task<IActionResult> ToggleAnsweredThread([FromForm] int threadId)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var result = await _discussionService.ToggleAnsweredThreadAsync(threadId, userId);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = result.Message });
        }

        return Ok(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Discussion/TogglePinReply")]
    public async Task<IActionResult> TogglePinReply([FromForm] int replyId)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var result = await _discussionService.TogglePinReplyAsync(replyId, userId);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = result.Message });
        }

        return Ok(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Discussion/DeleteThread")]
    public async Task<IActionResult> DeleteThread([FromForm] ModerateDiscussionInputModel model)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var result = await _discussionService.DeleteThreadAsync(model.Id, userId, model.Reason);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = result.Message });
        }

        return Ok(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Discussion/DeleteReply")]
    public async Task<IActionResult> DeleteReply([FromForm] ModerateDiscussionInputModel model)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var result = await _discussionService.DeleteReplyAsync(model.Id, userId, model.Reason);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = result.Message });
        }

        return Ok(result);
    }
}
