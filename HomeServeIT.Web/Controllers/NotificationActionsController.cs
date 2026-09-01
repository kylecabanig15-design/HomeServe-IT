using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HomeServeIT.Web.Controllers;

[Authorize(Roles = $"{Roles.Administrator},{Roles.Customer},{Roles.Technician}")]
[Route("notifications")]
public class NotificationActionsController : Controller
{
    private readonly NotificationService _notifications;
    private readonly UserManager<ApplicationUser> _userManager;

    public NotificationActionsController(NotificationService notifications, UserManager<ApplicationUser> userManager)
    {
        _notifications = notifications;
        _userManager = userManager;
    }

    [HttpPost("{notificationId:int}/read")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRead(int notificationId, bool isRead, string? returnUrl)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _notifications.SetReadStateAsync(user, notificationId, isRead);
        return RedirectBack(returnUrl);
    }

    [HttpPost("read-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(string? returnUrl)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _notifications.MarkAllReadAsync(user);
        return RedirectBack(returnUrl);
    }

    [HttpPost("{notificationId:int}/open")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(int notificationId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var target = await _notifications.MarkReadAndGetTargetAsync(user, notificationId);
        return target == null ? RedirectToRoleHome() : LocalRedirect(target);
    }

    private IActionResult RedirectBack(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToRoleHome();

    private IActionResult RedirectToRoleHome()
    {
        if (User.IsInRole(Roles.Administrator))
            return RedirectToAction("Index", "Notifications", new { area = "Admin" });
        if (User.IsInRole(Roles.Technician))
            return RedirectToAction("Index", "Notifications", new { area = "Technician" });
        return RedirectToAction("Index", "Notifications", new { area = "Customer" });
    }
}
