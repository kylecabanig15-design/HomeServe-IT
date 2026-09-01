using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HomeServeIT.Web.Constants;

namespace HomeServeIT.Web.Areas.Technician.Controllers
{
    [Area("Technician")]
    [Authorize(Roles = Roles.Technician)]
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
}
