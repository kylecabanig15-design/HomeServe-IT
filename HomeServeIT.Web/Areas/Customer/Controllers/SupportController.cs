using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Areas.Customer.Controllers;

[Area("Customer"), Authorize(Roles = Roles.Customer)]
public class SupportController(ApplicationDbContext context, UserManager<ApplicationUser> userManager) : Controller
{
    public async Task<IActionResult> Index(int? ticketId)
    {
        var userId = userManager.GetUserId(User);
        var customer = await context.Customers.FirstOrDefaultAsync(c => c.UserID == userId);
        var tickets = customer == null ? [] : await context.SupportTickets.Where(t => t.CustomerID == customer.CustomerID).OrderByDescending(t => t.CreatedAt).ToListAsync();
        ViewBag.SelectedTicketId = ticketId;
        return View(tickets);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string subject, string message)
    {
        var userId = userManager.GetUserId(User);
        var customer = await context.Customers.FirstOrDefaultAsync(c => c.UserID == userId);
        if (customer == null) return Forbid();
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(message))
        {
            TempData["ErrorMessage"] = "Add a subject and message so the support team can help.";
            return RedirectToAction(nameof(Index));
        }
        subject = subject.Trim();
        message = message.Trim();
        if (subject.Length > 120 || message.Length > 2000)
        {
            TempData["ErrorMessage"] = "Keep the subject under 120 characters and the message under 2,000 characters.";
            return RedirectToAction(nameof(Index));
        }

        var ticket = new SupportTicket { CustomerID = customer.CustomerID, Subject = subject, Message = message };
        context.SupportTickets.Add(ticket);
        await context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Your support request was sent to the admin team.";
        return RedirectToAction(nameof(Index), new { ticketId = ticket.SupportTicketID });
    }
}
