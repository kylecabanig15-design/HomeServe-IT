using System.Text;
using HomeServeIT.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace HomeServeIT.Web.Services;

public sealed record InvitationViewModel(string Email, string SetupUrl);

public static class InvitationResult
{
    public static async Task<IActionResult> ShowAsync(Controller controller, UserManager<ApplicationUser> users, ApplicationUser user)
    {
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
        var url = controller.Url.Page("/Account/ResetPassword", new { area = "Identity", code, email = user.Email })!;
        controller.Response.Headers.CacheControl = "no-store";
        controller.Response.Headers["Referrer-Policy"] = "no-referrer";
        return controller.View("~/Views/Shared/Invitation.cshtml", new InvitationViewModel(user.Email!, url));
    }
}
