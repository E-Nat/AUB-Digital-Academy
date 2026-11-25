using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AUBDigitalAcademy.Controllers;

[Authorize(Roles = "Admin")]
public class DashboardController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Admin");
    }
}
