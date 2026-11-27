using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AUBDigitalAcademy.Controllers;

[Authorize]
public class SettingsController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        if (User.IsInRole("Admin"))
        {
            return RedirectToAction("Settings", "Admin");
        }
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Profile()
    {
        if (User.IsInRole("Admin"))
        {
            return RedirectToAction("Settings", "Admin");
        }
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ChangePassword()
    {
        return RedirectToAction("ForgotPassword", "Account");
    }
}
