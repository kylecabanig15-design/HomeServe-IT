using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Areas.Customer.Models;

namespace HomeServeIT.Web.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = Roles.Customer)]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public DashboardController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user!.Id);

        // Auto-create a Customer profile if one doesn't exist yet
        if (customer == null && user != null)
        {
            var nameParts = (user.FullName ?? user.Email ?? "Customer").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            customer = new HomeServeIT.Web.Models.Customer
            {
                UserID = user.Id,
                FirstName = nameParts.Length > 0 ? nameParts[0] : "Customer",
                LastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : "",
                PhoneNumber = (user.PhoneNumber ?? "").Length > 50 ? (user.PhoneNumber ?? "")[..50] : (user.PhoneNumber ?? ""),
                HomeAddress = string.Join(", ", new[] { user.StreetAddress, user.BarangayCity }.Where(s => !string.IsNullOrWhiteSpace(s)))
            };
            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();
        }

        if (customer == null)
            return View(new CustomerDashboardViewModel { FirstName = user?.FullName?.Split(' ').FirstOrDefault() ?? "Customer" });

        var allRequests = await _context.ServiceRequests
            .Include(r => r.Technician)
            .Where(r => r.CustomerID == customer.CustomerID)
            .OrderByDescending(r => r.ScheduledDate)
            .ToListAsync();

        var outstandingInvoice = await _context.Invoices
            .Include(i => i.ServiceRequest)
            .Where(i => i.ServiceRequest.CustomerID == customer.CustomerID
                        && i.PaymentStatus == "Unpaid"
                        && (!i.IsQuotation || i.QuotationStatus == "ApprovedByAdmin" || i.QuotationStatus == "Approved"))
            .FirstOrDefaultAsync();

        var vm = new CustomerDashboardViewModel
        {
            FirstName = customer.FirstName,
            ActiveRequestCount = allRequests.Count(r => !r.IsArchived && r.Status != "Completed" && r.Status != "Cancelled"),
            LatestActiveRequest = allRequests.FirstOrDefault(r => !r.IsArchived && r.Status != "Completed" && r.Status != "Cancelled"),
            OutstandingInvoice = outstandingInvoice,
            AllRequests = allRequests
        };

        return View(vm);
    }
}
