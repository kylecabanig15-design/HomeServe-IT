using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Areas.Admin.Models;

namespace HomeServeIT.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = Roles.Administrator)]
    public class SystemController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly HomeServeIT.Web.Data.ApplicationDbContext _context;
        private readonly HomeServeIT.Web.Services.ApplicationSettingsService _settings;

        public SystemController(UserManager<ApplicationUser> userManager, HomeServeIT.Web.Data.ApplicationDbContext context,
            HomeServeIT.Web.Services.ApplicationSettingsService settings)
        {
            _userManager = userManager;
            _context = context;
            _settings = settings;
        }

        public async Task<IActionResult> UserManagement()
        {
            var users = await _userManager.Users.Where(u => !u.IsArchived).ToListAsync();
            return View(users);
        }

        public async Task<IActionResult> ArchivedUsers()
        {
            var users = await _userManager.Users.Where(u => u.IsArchived).ToListAsync();
            var requests = await _context.ServiceRequests
                .Include(r => r.Customer)
                .Include(r => r.Technician!)
                .ThenInclude(t => t.User)
                .Where(r => r.IsArchived)
                .OrderByDescending(r => r.ScheduledDate)
                .ToListAsync();

            return View(new AdminArchiveViewModel
            {
                Users = users,
                ServiceRequests = requests
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InviteUser(string email, string role, [FromServices] HomeServeIT.Web.Services.AccountProfileService profiles)
        {
            var validRoles = new[] { Roles.Administrator, Roles.Technician, Roles.Customer };
            if (string.IsNullOrWhiteSpace(role) || !validRoles.Contains(role))
            {
                TempData["ErrorMessage"] = "Invalid role selected.";
                return RedirectToAction(nameof(UserManagement));
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Email is required.";
                return RedirectToAction(nameof(UserManagement));
            }

            var user = new ApplicationUser { UserName = email.Trim(), Email = email.Trim(), FullName = email.Split('@')[0] };
            var result = await profiles.CreateAsync(user, role);
            if (result.Succeeded)
            {
                return await HomeServeIT.Web.Services.InvitationResult.ShowAsync(this, _userManager, user);
            }
            else
            {
                TempData["ErrorMessage"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
            return RedirectToAction(nameof(UserManagement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(string userId, string email, string role, string? password,
            [FromServices] HomeServeIT.Web.Services.AccountProfileService profiles)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();
            var model = ProfileViewModel.FromUser(user);
            model.Email = email;
            var result = await profiles.UpdateAsync(user, model, role, password);
            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Succeeded
                ? "Account updated successfully."
                : string.Join(", ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(UserManagement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SuspendUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                
                // If technician, mark as unavailable
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == userId);
                if (tech != null)
                {
                    tech.IsAvailable = false;
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = $"User {user.Email} paused successfully.";
            }
            return RedirectToAction(nameof(UserManagement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnsuspendUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                
                // If technician, mark as available
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == userId);
                if (tech != null)
                {
                    tech.IsAvailable = true;
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = $"User {user.Email} resumed successfully.";
            }
            return RedirectToAction(nameof(UserManagement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.IsArchived = true;
                await _userManager.UpdateAsync(user);
                
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                
                // Mark associated technician unavailable
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == userId);
                if (tech != null)
                {
                    tech.IsAvailable = false;
                    await _context.SaveChangesAsync();
                }
                
                TempData["SuccessMessage"] = $"User {user.Email} archived successfully.";
            }
            return RedirectToAction(nameof(UserManagement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnarchiveUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.IsArchived = false;
                await _userManager.UpdateAsync(user);
                
                await _userManager.SetLockoutEndDateAsync(user, null);
                
                // Mark associated technician available
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == userId);
                if (tech != null)
                {
                    tech.IsAvailable = true;
                    await _context.SaveChangesAsync();
                }
                
                TempData["SuccessMessage"] = $"User {user.Email} restored successfully.";
            }
            return RedirectToAction(nameof(ArchivedUsers));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(ArchivedUsers));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Remove associated technician record if any
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == userId);
                var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == userId);

                if (customer != null)
                {
                    var requestIds = await _context.ServiceRequests
                        .Where(r => r.CustomerID == customer.CustomerID)
                        .Select(r => r.RequestID)
                        .ToListAsync();

                    var invoices = await _context.Invoices
                        .Where(i => requestIds.Contains(i.RequestID))
                        .ToListAsync();
                    if (invoices.Any()) _context.Invoices.RemoveRange(invoices);

                    var deliverables = await _context.JobDeliverables
                        .Where(d => requestIds.Contains(d.RequestID))
                        .ToListAsync();
                    if (deliverables.Any()) _context.JobDeliverables.RemoveRange(deliverables);

                    var messages = await _context.ServiceMessages
                        .Where(m => requestIds.Contains(m.RequestID))
                        .ToListAsync();
                    if (messages.Any()) _context.ServiceMessages.RemoveRange(messages);

                    var requests = await _context.ServiceRequests
                        .Where(r => r.CustomerID == customer.CustomerID)
                        .ToListAsync();
                    if (requests.Any()) _context.ServiceRequests.RemoveRange(requests);

                    var devices = await _context.Devices
                        .Where(d => d.CustomerID == customer.CustomerID)
                        .ToListAsync();
                    if (devices.Any()) _context.Devices.RemoveRange(devices);

                    _context.Customers.Remove(customer);
                }

                if (tech != null)
                {
                    _context.Technicians.Remove(tech);
                }

                await _context.SaveChangesAsync();

                var result = await _userManager.DeleteAsync(user);
                if (result.Succeeded)
                {
                    await transaction.CommitAsync();
                    TempData["SuccessMessage"] = $"User {user.Email} permanently deleted.";
                }
                else
                {
                    TempData["ErrorMessage"] = string.Join(", ", result.Errors.Select(e => e.Description));
                }
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Could not delete the user because of existing related records. Archive the user instead.";
            }

            return RedirectToAction(nameof(ArchivedUsers));
        }

        public async Task<IActionResult> Settings(string? tab = null)
        {
            return View(await _settings.GetPageAsync(tab));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateGeneral([Bind(Prefix = "General")] GeneralSettingsInput input)
        {
            if (!ModelState.IsValid) return await InvalidSettingsAsync("general", model => model.General = input);
            return SettingsResult(await _settings.UpdateGeneralAsync(input, CurrentUserId()), "general");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCompany([Bind(Prefix = "Company")] CompanySettingsInput input)
        {
            if (!ModelState.IsValid) return await InvalidSettingsAsync("company", model => model.Company = input);
            return SettingsResult(await _settings.UpdateCompanyAsync(input, CurrentUserId()), "company");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateNotifications([Bind(Prefix = "Notifications")] NotificationSettingsInput input)
        {
            if (!ModelState.IsValid) return await InvalidSettingsAsync("notifications", model => model.Notifications = input);
            return SettingsResult(await _settings.UpdateNotificationsAsync(input, CurrentUserId()), "notifications");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSecurity([Bind(Prefix = "Security")] SecuritySettingsInput input)
        {
            if (!ModelState.IsValid) return await InvalidSettingsAsync("security", model => model.Security = input);
            return SettingsResult(await _settings.UpdateSecurityAsync(input, CurrentUserId()), "security");
        }

        private string CurrentUserId() => _userManager.GetUserId(User)
            ?? throw new InvalidOperationException("An authenticated administrator is required.");

        private IActionResult SettingsResult(HomeServeIT.Web.Services.SettingsUpdateResult result, string tab)
        {
            if (result.Succeeded)
                TempData["SuccessMessage"] = "Settings saved successfully.";
            else
                TempData["ErrorMessage"] = result.Conflict
                    ? "These settings changed in another session. Review the latest values and try again."
                    : "Settings could not be saved.";
            return RedirectToAction(nameof(Settings), new { tab });
        }

        private async Task<IActionResult> InvalidSettingsAsync(string tab, Action<SystemSettingsViewModel> preserveInput)
        {
            var model = await _settings.GetPageAsync(tab);
            preserveInput(model);
            return View(nameof(Settings), model);
        }
    }
}
