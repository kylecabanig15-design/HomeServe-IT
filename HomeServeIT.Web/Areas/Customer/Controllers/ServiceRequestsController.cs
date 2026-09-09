using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Areas.Customer.Models;
using HomeServeIT.Web.Services;

namespace HomeServeIT.Web.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = Roles.Customer)]
public class ServiceRequestsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly JobInventoryService _jobInventoryService;

    public ServiceRequestsController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        JobInventoryService jobInventoryService)
    {
        _context = context;
        _userManager = userManager;
        _env = env;
        _jobInventoryService = jobInventoryService;
    }

    public async Task<IActionResult> Index(int? jobId, string? tab, string? open, string? filter)
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

        var vm = new CustomerServiceRequestsViewModel
        {
            AvailableTechnicians = await _context.Technicians
                .Where(t => t.IsAvailable)
                .ToListAsync()
        };

        if (customer != null)
        {
            vm.Requests = await _context.ServiceRequests
                .Include(r => r.Technician)
                    .ThenInclude(t => t!.User)
                .Where(r => r.CustomerID == customer.CustomerID)
                .OrderByDescending(r => r.ScheduledDate)
                .ToListAsync();
        }

        ViewBag.OpenJobId = jobId;
        ViewBag.OpenTab = tab is "chat" or "progress" ? tab : "details";
        ViewBag.OpenCreate = string.Equals(open, "create", StringComparison.OrdinalIgnoreCase);
        ViewBag.InitialFilter = filter == "Cancelled" ? "Cancelled" : "All";

        return View(vm);
    }

    public IActionResult Cancelled(int? jobId) =>
        RedirectToAction(nameof(Index), new { jobId, filter = "Cancelled" });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(PrivateUploadService.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = PrivateUploadService.MaxRequestBytes)]
        public async Task<IActionResult> Create(
        string issueDescription,
        DateTime scheduledDate,
        string? serviceCategory,
        string? priority,
        IFormFile? image, [FromServices] PrivateUploadService uploads)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login", "Account", new { area = "" });

        if (string.IsNullOrWhiteSpace(issueDescription))
        {
            TempData["ErrorMessage"] = "Please provide a description of the issue.";
            return RedirectToAction(nameof(Index));
        }

        if (scheduledDate <= DateTime.Now.AddMinutes(1))
        {
            TempData["ErrorMessage"] = "Scheduled date must be in the future.";
            return RedirectToAction(nameof(Index));
        }

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user.Id);

        // Auto-create a Customer profile if one doesn't exist yet (e.g. accounts created before CRM integration)
        if (customer == null)
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

        if (!string.IsNullOrWhiteSpace(issueDescription))
        {
            StoredUpload? stored;
            try { stored = await uploads.StoreAsync(image, "service-requests", HttpContext.RequestAborted); }
            catch (UploadValidationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
            await using var upload = stored;
            var imagePath = upload?.Url;

            var request = new ServiceRequest
            {
                CustomerID = customer.CustomerID,
                IssueDescription = issueDescription,
                ScheduledDate = scheduledDate,
                Status = "Pending",
                ServiceCategory = serviceCategory ?? "Other",
                Priority = priority ?? "Normal",
                ImagePath = imagePath
            };

            _context.ServiceRequests.Add(request);
            await _context.SaveChangesAsync();
            upload?.Complete();
            TempData["SuccessMessage"] = "Your service request has been successfully submitted.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> GetChatHistory(int requestId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user.Id);
        if (customer == null) return Forbid();

        var ownsRequest = await _context.ServiceRequests
            .AnyAsync(r => r.RequestID == requestId && r.CustomerID == customer.CustomerID);
        if (!ownsRequest) return Forbid();

        var messages = await _context.ServiceMessages
            .Include(m => m.Sender)
            .Where(m => m.RequestID == requestId)
            .OrderBy(m => m.Timestamp)
            .Select(m => new {
                m.MessageID,
                m.Content,
                Timestamp = m.Timestamp.ToString("O"),
                SenderId = m.SenderID,
                SenderName = m.Sender.FullName,
                m.IsRead
            })
            .ToListAsync();
        return Json(messages);
    }

    [HttpGet]
    public async Task<IActionResult> GetJobWorkflow(int requestId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user.Id);
        if (customer == null) return Forbid();

        var ownsRequest = await _context.ServiceRequests
            .AnyAsync(r => r.RequestID == requestId && r.CustomerID == customer.CustomerID);
        if (!ownsRequest) return Forbid();

        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.RequestID == requestId
            && (!i.IsQuotation || i.QuotationStatus == "ApprovedByAdmin" || i.QuotationStatus == "Approved"));
        var deliverables = await _context.JobDeliverables.Where(d => d.RequestID == requestId).ToListAsync();
        
        return Json(new {
            invoice = invoice != null ? new { invoice.InvoiceID, invoice.TotalAmount, invoice.BreakdownDetails, invoice.PaymentStatus } : null,
            deliverables = deliverables.Select(d => new { d.DeliverableID, d.Phase, d.Description, d.ImagePath, CreatedAt = d.CreatedAt.ToString("O") })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PayInvoice(int invoiceId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login", "Account", new { area = "" });
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user.Id);
        if (customer == null) return Forbid();

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var invoice = await _context.Invoices
            .Include(i => i.ServiceRequest)
            .FirstOrDefaultAsync(i => i.InvoiceID == invoiceId
                                      && i.ServiceRequest != null
                                      && i.ServiceRequest.CustomerID == customer.CustomerID
                                      && (!i.IsQuotation || i.QuotationStatus == "ApprovedByAdmin" || i.QuotationStatus == "Approved"));
        if (invoice == null)
        {
            await transaction.RollbackAsync();
            TempData["ErrorMessage"] = "The invoice could not be found or is not available for payment.";
            return RedirectToAction(nameof(Index));
        }

        if (invoice.PaymentStatus == "Paid")
        {
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = "This invoice has already been paid.";
            return RedirectToAction(nameof(Index));
        }

        var inventoryResult = await _jobInventoryService.DeductForJobStartAsync(
            invoice.RequestID,
            user.FullName ?? user.Email ?? "Customer");
        if (!inventoryResult.Succeeded)
        {
            await transaction.RollbackAsync();
            TempData["ErrorMessage"] = inventoryResult.ErrorMessage;
            return RedirectToAction(nameof(Index));
        }

        invoice.PaymentStatus = "Paid";
        if (invoice.ServiceRequest.Status is not "Completed" and not "Cancelled")
            invoice.ServiceRequest.Status = "In Progress";

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["SuccessMessage"] = "Payment successful! The technician will now begin the work.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewDeliverables(int requestId, string actionType, string? feedback)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login", "Account", new { area = "" });
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user.Id);
        if (customer == null) return Forbid();

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var job = await _context.ServiceRequests
            .FirstOrDefaultAsync(r => r.RequestID == requestId && r.CustomerID == customer.CustomerID);
        if (job == null || job.Status != "PendingCustomerReview")
        {
            await transaction.RollbackAsync();
            TempData["ErrorMessage"] = "This service is not currently awaiting your review.";
            return RedirectToAction(nameof(Index));
        }

        if (!await _context.Invoices.HasPaidInvoiceAsync(requestId))
        {
            await transaction.RollbackAsync();
            TempData["ErrorMessage"] = "This service cannot be completed because it does not have a paid invoice.";
            return RedirectToAction(nameof(Index), new { jobId = requestId });
        }

        if (!job.Check1_Diagnostic || !job.Check2_Hardware || !job.Check3_Firmware || !job.Check4_QA || !job.Check5_Handover
            || !await _context.JobDeliverables.AnyAsync(d => d.RequestID == requestId && d.Phase == "FinalProof" && d.ImagePath != null))
        {
            TempData["ErrorMessage"] = "The technician must complete the checklist and submit repair proof before customer sign-off.";
            return RedirectToAction(nameof(Index));
        }

        if (actionType == "Approve")
        {
            job.Status = "Completed";
            job.CompletedDate = DateTime.UtcNow;
            TempData["SuccessMessage"] = "You have approved the deliverables and marked the job as Completed. Thank you!";
        }
        else if (actionType == "Revise")
        {
            var inventoryResult = await _jobInventoryService.DeductForJobStartAsync(
                requestId,
                user.FullName ?? user.Email ?? "Customer");
            if (!inventoryResult.Succeeded)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = inventoryResult.ErrorMessage;
                return RedirectToAction(nameof(Index));
            }
            job.Status = "In Progress"; // Send back to In Progress

            _context.ServiceMessages.Add(new ServiceMessage {
                RequestID = requestId,
                SenderID = user.Id,
                Content = "REVISION REQUESTED: " + feedback,
                Timestamp = DateTime.UtcNow
            });
            TempData["SuccessMessage"] = "Revision requested. The technician has been notified.";
        }
        else
        {
            await transaction.RollbackAsync();
            TempData["ErrorMessage"] = "Invalid review action.";
            return RedirectToAction(nameof(Index), new { jobId = requestId });
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Index));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelRequest(int requestId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login", "Account", new { area = "" });

        var cust = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user.Id);
        if (cust == null) return Forbid();

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var request = await _context.ServiceRequests.FirstOrDefaultAsync(r => r.RequestID == requestId && r.CustomerID == cust.CustomerID);
        if (request != null && request.TechID == null && request.Status != "Completed" && request.Status != "Cancelled")
        {
            request.Status = "Cancelled";
            request.IsArchived = true;
            await ServiceCancellationCleanup.RemoveFinancialArtifactsAsync(_context, request.RequestID);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = "Your request has been successfully cancelled.";
        }
        else
        {
            await transaction.RollbackAsync();
        }
        return RedirectToAction(nameof(Index), new { jobId = requestId, filter = "Cancelled" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestCancel(int requestId, string reason)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login", "Account", new { area = "" });

        var cust = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user.Id);
        if (cust == null) return Forbid();

        var request = await _context.ServiceRequests.FirstOrDefaultAsync(r => r.RequestID == requestId && r.CustomerID == cust.CustomerID);
        if (request != null && request.TechID != null && request.Status != "Completed" && request.Status != "Cancelled")
        {
            request.IsCancellationRequested = true;
            request.CancellationReason = reason;
            request.CancellationStatus = "Pending";
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Your cancellation request has been submitted to the Admin for approval.";
        }
        return RedirectToAction(nameof(Index));
    }
}
