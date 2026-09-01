using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;

namespace HomeServeIT.Web.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = Roles.Customer)]
public class QuotationsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public QuotationsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user!.Id);

        if (customer == null)
            return View(new List<Invoice>());

        var invoices = await _context.Invoices
            .Include(i => i.ServiceRequest)
            .Where(i => i.ServiceRequest.CustomerID == customer.CustomerID
                        && i.IsQuotation
                        && (i.QuotationStatus == "ApprovedByAdmin" || i.QuotationStatus == "Approved"))
            .OrderByDescending(i => i.DateIssued)
            .ToListAsync();

        return View(invoices);
    }

    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user!.Id);

        var quotation = await _context.Invoices
            .Include(i => i.ServiceRequest)
            .FirstOrDefaultAsync(i => i.InvoiceID == id 
                                      && i.ServiceRequest.CustomerID == customer!.CustomerID 
                                      && i.IsQuotation);

        if (quotation == null) return NotFound();

        return View(quotation);
    }

    public async Task<IActionResult> Checkout(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user!.Id);

        var quotation = await _context.Invoices
            .Include(i => i.ServiceRequest)
            .FirstOrDefaultAsync(i => i.InvoiceID == id 
                                      && i.ServiceRequest.CustomerID == customer!.CustomerID 
                                      && i.IsQuotation
                                      && i.PaymentStatus == "Unpaid");

        if (quotation == null) return NotFound();

        return View(quotation);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessPayment(int id, string paymentMethod)
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user!.Id);

        var quotation = await _context.Invoices
            .Include(i => i.ServiceRequest)
            .FirstOrDefaultAsync(i => i.InvoiceID == id 
                                      && i.ServiceRequest.CustomerID == customer!.CustomerID 
                                      && i.IsQuotation
                                      && i.PaymentStatus == "Unpaid");

        if (quotation == null) return NotFound();

        // Simulate payment processing
        quotation.PaymentStatus = "Paid";
        quotation.QuotationStatus = "Approved";
        
        // Return to Pending so admin/technician can proceed
        quotation.ServiceRequest.Status = "Pending"; 
        
        await _context.SaveChangesAsync();
        
        TempData["SuccessMessage"] = $"Payment for Quotation #{quotation.InvoiceID:D4} was successful!";
        return RedirectToAction(nameof(Index));
    }
}
