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
public class BillsAndPaymentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public BillsAndPaymentsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user!.Id);

        if (customer == null)
            return View(new CustomerBillsViewModel());

        var invoices = await _context.Invoices
            .Include(i => i.ServiceRequest)
            .Where(i => i.ServiceRequest.CustomerID == customer.CustomerID
                        && (!i.IsQuotation || i.QuotationStatus == "ApprovedByAdmin" || i.QuotationStatus == "Approved"))
            .OrderByDescending(i => i.DateIssued)
            .ToListAsync();

        var vm = new CustomerBillsViewModel
        {
            Invoices = invoices,
            TotalOutstanding = invoices.Where(i => i.PaymentStatus == "Unpaid").Sum(i => i.TotalAmount),
            TotalPaid = invoices.Where(i => i.PaymentStatus == "Paid").Sum(i => i.TotalAmount)
        };

        return View(vm);
    }
}
