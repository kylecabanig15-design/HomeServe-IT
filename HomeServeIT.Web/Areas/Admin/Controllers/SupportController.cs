using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = Roles.Administrator)]
public class SupportController(ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index(string status = "Open", int? ticketId = null)
    {
        status = status is "Open" or "In Progress" or "Resolved" or "All" ? status : "Open";
        ViewBag.Status = status;
        ViewBag.SelectedTicketId = ticketId;
        var query = context.SupportTickets
            .Include(t => t.Customer)
            .ThenInclude(c => c.User)
            .AsQueryable();
        if (status != "All") query = query.Where(t => t.Status == status);
        return View(await query.OrderByDescending(t => t.CreatedAt).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string response, string status = "Resolved")
    {
        var ticket = await context.SupportTickets.FindAsync(id);
        if (ticket == null) return NotFound();
        if (string.IsNullOrWhiteSpace(response))
        {
            TempData["ErrorMessage"] = "Enter a response before resolving the request.";
            return RedirectToAction(nameof(Index), new { status = "All", ticketId = id });
        }
        response = response.Trim();
        if (response.Length > 2000)
        {
            TempData["ErrorMessage"] = "Keep the response under 2,000 characters.";
            return RedirectToAction(nameof(Index), new { status = "All", ticketId = id });
        }

        ticket.AdminResponse = response;
        ticket.Status = status is "Open" or "In Progress" or "Resolved" ? status : "Resolved";
        ticket.RespondedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        TempData["SuccessMessage"] = "The response is now visible to the customer.";
        return RedirectToAction(nameof(Index), new { status = ticket.Status == "Resolved" ? "All" : ticket.Status, ticketId = id });
    }
}
