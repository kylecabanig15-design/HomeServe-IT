using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeServeIT.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LockoutModel : PageModel
{
    public bool IsArchived { get; private set; }

    public void OnGet(bool archived = false)
    {
        IsArchived = archived;
    }
}
