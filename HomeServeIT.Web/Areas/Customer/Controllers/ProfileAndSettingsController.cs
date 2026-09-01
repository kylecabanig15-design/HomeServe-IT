using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeServeIT.Web.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = HomeServeIT.Web.Constants.Roles.Customer)]
public class ProfileAndSettingsController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateProfile(string fullName, string email, string mobile, string address, string city)
    {
        TempData["SuccessMessage"] = "Profile updated successfully.";
        return RedirectToAction(nameof(Index));
    }
}
