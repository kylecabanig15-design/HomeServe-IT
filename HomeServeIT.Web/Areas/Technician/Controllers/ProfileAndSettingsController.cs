using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HomeServeIT.Web.Areas.Technician.Controllers;

[Area("Technician")]
[Authorize(Roles = Roles.Technician)]
public class ProfileAndSettingsController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    AccountProfileService profiles) : Controller
{
    public async Task<IActionResult> Index()
    {
        var user = await users.GetUserAsync(User);
        return user == null ? Challenge() : View(ProfileViewModel.FromUser(user));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(ProfileViewModel model)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!ModelState.IsValid) return View("Index", model);
        var result = await profiles.UpdateAsync(user, model);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return View("Index", model);
        }
        await signIn.RefreshSignInAsync(user);
        TempData["SuccessMessage"] = "Profile updated successfully.";
        return RedirectToAction(nameof(Index));
    }
}
