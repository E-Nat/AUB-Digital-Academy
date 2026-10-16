using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;
using System.Security.Claims;

namespace AUBDigitalAcademy.Controllers;

[Authorize(Roles = "Admin")]
public class UserController : Controller
{
    private readonly IUserService _userService;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<UserController> _logger;

    public UserController(
        IUserService userService,
        UserManager<User> userManager,
        ILogger<UserController> logger)
    {
        _userService = userService;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? role = null, string? search = null, bool? status = null)
    {
        ViewData["Title"] = "User Management";
        ViewData["ActivePage"] = "Users";
        ViewData["CurrentRole"] = role ?? "All";
        ViewData["CurrentSearch"] = search ?? string.Empty;
        ViewData["CurrentStatus"] = status.HasValue ? (status.Value ? "Active" : "Inactive") : "All";

        var users = await _userService.GetUsersAsync(role, search, status);
        ViewBag.Counts = await _userService.GetUserCountsAsync();
        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["ErrorMessage"] = "User ID was not provided.";
            return RedirectToAction(nameof(Index));
        }

        var userDetails = await _userService.GetUserByIdAsync(id);
        if (userDetails == null)
        {
            TempData["ErrorMessage"] = "The requested user account was not found.";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = $"User Details - {userDetails.FullName}";
        ViewData["ActivePage"] = "Users";

        return View(userDetails);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "Create New User";
        ViewData["ActivePage"] = "Users";

        var model = new CreateUserViewModel
        {
            IsActive = true,
            Role = "Student"
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = "Validation failed. Please check form inputs.", errors });
            }
            ViewData["Title"] = "Create New User";
            ViewData["ActivePage"] = "Users";
            return View(model);
        }

        var result = await _userService.CreateUserAsync(model);
        if (result.Succeeded)
        {
            if (isAjax)
            {
                return Json(new { success = true, message = result.Message, redirectUrl = Url.Action(nameof(Index)) });
            }
            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (isAjax)
        {
            return Json(new { success = false, message = result.Message, errors = result.Errors });
        }

        ViewData["Title"] = "Create New User";
        ViewData["ActivePage"] = "Users";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["ErrorMessage"] = "User ID was not provided.";
            return RedirectToAction(nameof(Index));
        }

        var model = await _userService.GetUserForEditAsync(id);
        if (model == null)
        {
            TempData["ErrorMessage"] = "User account not found.";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = $"Edit User - {model.FullName}";
        ViewData["ActivePage"] = "Users";

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = "Validation failed.", errors });
            }
            ViewData["Title"] = $"Edit User - {model.FullName}";
            ViewData["ActivePage"] = "Users";
            return View(model);
        }

        var result = await _userService.UpdateUserAsync(model);
        if (result.Succeeded)
        {
            if (isAjax)
            {
                return Json(new { success = true, message = result.Message, redirectUrl = Url.Action(nameof(Index)) });
            }
            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (isAjax)
        {
            return Json(new { success = false, message = result.Message, errors = result.Errors });
        }

        ViewData["Title"] = $"Edit User - {model.FullName}";
        ViewData["ActivePage"] = "Users";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(string id)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        var currentUserId = _userManager.GetUserId(User) ?? string.Empty;

        var result = await _userService.ToggleUserStatusAsync(id, currentUserId);

        if (isAjax)
        {
            return Json(new { success = result.Succeeded, message = result.Message, errors = result.Errors });
        }

        if (result.Succeeded)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["ErrorMessage"] = "User ID was not provided.";
            return RedirectToAction(nameof(Index));
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User account not found.";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = $"Reset Password - {user.FullName}";
        ViewData["ActivePage"] = "Users";

        var model = new ResetPasswordAdminViewModel
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordAdminViewModel model)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = "Validation failed.", errors });
            }
            ViewData["Title"] = $"Reset Password - {model.FullName}";
            ViewData["ActivePage"] = "Users";
            return View(model);
        }

        var currentUserId = _userManager.GetUserId(User) ?? string.Empty;
        var result = await _userService.ResetPasswordAsync(model.UserId, model.NewPassword, currentUserId);

        if (result.Succeeded)
        {
            if (isAjax)
            {
                return Json(new { success = true, message = result.Message, redirectUrl = Url.Action(nameof(Index)) });
            }
            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (isAjax)
        {
            return Json(new { success = false, message = result.Message, errors = result.Errors });
        }

        ViewData["Title"] = $"Reset Password - {model.FullName}";
        ViewData["ActivePage"] = "Users";
        return View(model);
    }
}
