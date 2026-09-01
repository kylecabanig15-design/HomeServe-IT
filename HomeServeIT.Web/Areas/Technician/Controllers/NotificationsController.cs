using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;

namespace HomeServeIT.Web.Areas.Technician.Controllers
{
    [Area("Technician")]
    [Authorize(Roles = Roles.Technician)]
    public class NotificationsController : Controller
    {
        private readonly NotificationService _notifications;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationsController(NotificationService notifications, UserManager<ApplicationUser> userManager)
        {
            _notifications = notifications;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(string? category)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            return View(await _notifications.GetFeedAsync(user, category));
        }
    }
}
