using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Controllers;

[Authorize]
public class NotificationController : Controller
{
    private readonly INotificationService _notificationService;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(
        INotificationService notificationService,
        UserManager<User> userManager,
        ILogger<NotificationController> logger)
    {
        _notificationService = notificationService;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    [Route("api/Notification/UnreadCount")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Json(new { success = false, unreadCount = 0 });

        var count = await _notificationService.GetUnreadCountAsync(userId);
        return Json(new { success = true, unreadCount = count });
    }

    [HttpGet]
    [Route("api/Notification/List")]
    public async Task<IActionResult> GetNotifications(int take = 15, bool unreadOnly = false, NotificationType? type = null)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Json(new { success = false, data = new NotificationFeedViewModel() });

        var feed = await _notificationService.GetUserNotificationsAsync(userId, take, unreadOnly, type);
        return Json(new { success = true, data = feed });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("api/Notification/MarkAsRead")]
    public async Task<IActionResult> MarkAsRead([FromForm] int id)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var success = await _notificationService.MarkAsReadAsync(id, userId);
        return Json(new { success });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("api/Notification/MarkAllAsRead")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var updatedCount = await _notificationService.MarkAllAsReadAsync(userId);
        return Json(new { success = true, updatedCount });
    }

    [HttpGet]
    [Route("Notifications")]
    public async Task<IActionResult> Index(string? tab = "all")
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        ViewData["Title"] = "Notification Center";
        ViewData["ActivePage"] = "Notifications";
        ViewBag.ActiveTab = (tab ?? "all").ToLowerInvariant();

        var notifications = await _notificationService.GetUserNotificationsAsync(userId, take: 50);
        var announcements = await _notificationService.GetAnnouncementsForUserAsync(userId, take: 50);

        ViewBag.Announcements = announcements;
        return View(notifications);
    }

    [HttpGet]
    [Route("Announcements")]
    public async Task<IActionResult> Announcements(int? courseId = null)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        ViewData["Title"] = "Announcements";
        ViewData["ActivePage"] = "Announcements";

        var announcements = await _notificationService.GetAnnouncementsForUserAsync(userId, take: 50, courseId: courseId);
        return View(announcements);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Announcement/Create")]
    public async Task<IActionResult> CreateAnnouncement([FromForm] CreateAnnouncementInputModel model)
    {
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Invalid input.";
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return BadRequest(new { success = false, message = firstError });
            }
            TempData["ErrorMessage"] = firstError;
            return Redirect(Request.Headers["Referer"].ToString() ?? "/");
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var result = await _notificationService.CreateAnnouncementAsync(
            userId, model.Title, model.Content, model.Scope, model.CourseId, model.IsPinned);

        if (!result.Success)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = result.Message });
            }
            TempData["ErrorMessage"] = result.Message;
            return Redirect(Request.Headers["Referer"].ToString() ?? "/");
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
    [Route("Announcement/Delete")]
    public async Task<IActionResult> DeleteAnnouncement([FromForm] int id)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var result = await _notificationService.DeleteAnnouncementAsync(id, userId);
        if (!result.Success)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = result.Message });
            }
            TempData["ErrorMessage"] = result.Message;
            return Redirect(Request.Headers["Referer"].ToString() ?? "/");
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return Ok(result);
        }

        TempData["SuccessMessage"] = result.Message;
        return Redirect(Request.Headers["Referer"].ToString() ?? "/");
    }
}
