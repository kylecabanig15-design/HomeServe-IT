using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Areas.Technician.Models;
using HomeServeIT.Web.Services;

namespace HomeServeIT.Web.Areas.Technician.Controllers
{
    [Area("Technician")]
    [Authorize(Roles = Roles.Technician)]
    public class AssignedJobsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly JobInventoryService _jobInventoryService;

        public AssignedJobsController(
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

        public async Task<IActionResult> Index(int? jobId = null, string? tab = null)
        {
            var user = await _userManager.GetUserAsync(User);
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);

            if (tech == null)
                return View(new TechnicianAssignedJobsViewModel());

            var allJobs = await _context.ServiceRequests
                .Include(r => r.Customer)
                    .ThenInclude(customer => customer.User)
                .VisibleTechnicianAssignments(tech.TechID)
                .OrderByDescending(r => r.ScheduledDate)
                .ToListAsync();

            var vm = new TechnicianAssignedJobsViewModel
            {
                ActiveJobs    = allJobs.Where(j => j.Status != "Completed").ToList(),
                CompletedJobs = allJobs.Where(j => j.Status == "Completed").ToList()
            };

            var jobIds = allJobs.Select(job => job.RequestID).ToList();
            var financialRecords = await _context.Invoices.AsNoTracking().ActiveFinancialRecords()
                .Where(invoice => jobIds.Contains(invoice.RequestID))
                .Select(invoice => new { invoice.RequestID, invoice.IsQuotation, invoice.QuotationStatus, invoice.PaymentStatus })
                .ToListAsync();
            vm.QuotedRequestIds = financialRecords.Select(invoice => invoice.RequestID).ToHashSet();
            vm.PaidRequestIds = financialRecords.Where(invoice => invoice.PaymentStatus == "Paid"
                && (!invoice.IsQuotation || invoice.QuotationStatus is "ApprovedByAdmin" or "Approved"))
                .Select(invoice => invoice.RequestID).ToHashSet();
            vm.PendingReviewRequestIds = financialRecords.Where(invoice => invoice.IsQuotation && invoice.QuotationStatus == "PendingAdmin")
                .Select(invoice => invoice.RequestID).ToHashSet();

            ViewBag.InventoryItems = await _context.InventoryItems
                .Where(i => !i.IsArchived && i.StockQuantity > 0)
                .OrderBy(i => i.Category)
                .ThenBy(i => i.ItemName)
                .ToListAsync();

            ViewData["OpenJobId"] = jobId.HasValue
                && allJobs.Any(job => job.RequestID == jobId.Value && !job.Customer.User.IsArchived)
                    ? jobId
                    : null;
            ViewData["OpenTab"] = !string.IsNullOrEmpty(tab) ? tab : "details";
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int requestId, string status)
        {
            var user = await _userManager.GetUserAsync(User);
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);

            if (tech == null) return NotFound();

            if (status != "In Progress")
            {
                TempData["ErrorMessage"] = "Invalid status value.";
                return RedirectToAction(nameof(Index));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var request = await _context.ServiceRequests
                .ActionableTechnicianAssignments(tech.TechID)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);
            if (request == null)
            {
                await transaction.RollbackAsync();
                return NotFound();
            }

            if (request.Status != "Pending")
            {
                TempData["ErrorMessage"] = "This job cannot change to that state. Submit repair proof for customer sign-off to finish the job.";
                return RedirectToAction(nameof(Index), new { jobId = requestId, tab = "workflow" });
            }

            if (!await _context.Invoices.HasPaidInvoiceAsync(requestId))
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "This job cannot start until its approved invoice has been paid.";
                return RedirectToAction(nameof(Index), new { jobId = requestId });
            }

            var inventoryResult = await _jobInventoryService.DeductForJobStartAsync(
                requestId,
                user?.FullName ?? user?.Email ?? $"Technician #{tech.TechID}");
            if (!inventoryResult.Succeeded)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = inventoryResult.ErrorMessage;
                return RedirectToAction(nameof(Index));
            }

            request.CompletedDate = null;

            request.Status = status;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["SuccessMessage"] = $"Job {requestId} status updated to {status}.";
            return RedirectToAction(nameof(Index), new { jobId = requestId, tab = "workflow" });
        }

        [HttpGet]
        public async Task<IActionResult> GetChatHistory(int requestId)
        {
            var user = await _userManager.GetUserAsync(User);
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);
            if (tech == null) return Forbid();

            var assigned = await _context.ServiceRequests
                .ActionableTechnicianAssignments(tech.TechID)
                .AnyAsync(r => r.RequestID == requestId);
            if (!assigned) return Forbid();

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProposeQuotation(int requestId, int[] itemIds, int[] quantities, decimal laborAmount, string? serviceNotes, DateTime proposedDeadline)
        {
            var user = await _userManager.GetUserAsync(User);
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);
            if (tech == null) return Forbid();

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var job = await _context.ServiceRequests
                .ActionableTechnicianAssignments(tech.TechID)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);
            if (job == null) return NotFound();
            if (job.Status is not ("Pending" or "Diagnosing")
                || await _context.Invoices.ActiveFinancialRecords().AnyAsync(invoice => invoice.RequestID == requestId))
            {
                TempData["ErrorMessage"] = "A quotation can only be created before review, payment, or repair begins.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid || itemIds.Length != quantities.Length || quantities.Any(quantity => quantity <= 0) || itemIds.Any(id => id <= 0) || laborAmount < 0)
            {
                TempData["ErrorMessage"] = "Enter valid labor and completion details. Parts are optional; added parts need a positive quantity.";
                return RedirectToAction(nameof(Index));
            }

            var requested = itemIds.Zip(quantities).Where(x => x.Second > 0).GroupBy(x => x.First).ToDictionary(g => g.Key, g => g.Sum(x => x.Second));
            var items = await _context.InventoryItems
                .Where(i => !i.IsArchived && requested.Keys.Contains(i.ItemID))
                .ToListAsync();
            if (items.Count != requested.Count || items.Any(i => requested[i.ItemID] > i.StockQuantity) || laborAmount < 0)
            {
                TempData["ErrorMessage"] = "One or more selected quantities exceed available stock.";
                return RedirectToAction(nameof(Index));
            }

            var partsTotal = items.Sum(i => i.UnitPrice * requested[i.ItemID]);
            var proposedAmount = partsTotal + laborAmount;
            var breakdown = string.Join("\n", items.Select(i => $"{i.ItemName} × {requested[i.ItemID]} — ₱{i.UnitPrice * requested[i.ItemID]:N2}"));
            if (laborAmount > 0) breakdown += $"\nLabor — ₱{laborAmount:N2}";
            if (!string.IsNullOrWhiteSpace(serviceNotes)) breakdown += $"\nNotes: {serviceNotes.Trim()}";

            var existingQuotation = await _context.Invoices
                .AnyAsync(i => i.RequestID == requestId && i.IsQuotation && i.QuotationStatus == "PendingAdmin");
            if (existingQuotation)
            {
                TempData["ErrorMessage"] = "A quotation for this job is already pending approval.";
                return RedirectToAction(nameof(Index));
            }

            job.EstimatedDeadline = proposedDeadline;
            job.Status = "PendingAdminApproval";

            var invoice = new Invoice
            {
                RequestID = requestId,
                TotalAmount = proposedAmount,
                IsQuotation = true,
                QuotationStatus = "PendingAdmin",
                BreakdownDetails = breakdown,
                DateIssued = DateTime.UtcNow,
                PaymentStatus = "Unpaid"
            };

            _context.Invoices.Add(invoice);
            _context.JobInventoryUsages.RemoveRange(_context.JobInventoryUsages.Where(u => u.RequestID == requestId && !u.IsDeducted));
            _context.JobInventoryUsages.AddRange(items.Select(i => new JobInventoryUsage
            {
                RequestID = requestId,
                ItemID = i.ItemID,
                Quantity = requested[i.ItemID],
                UnitPrice = i.UnitPrice
            }));
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["SuccessMessage"] = "Quotation sent to Admin for approval.";
            return RedirectToAction(nameof(Index), new { jobId = requestId, tab = "workflow" });
        }

        [HttpGet]
        public async Task<IActionResult> GetPendingQuotation(int requestId)
        {
            var user = await _userManager.GetUserAsync(User);
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);
            if (tech == null) return Forbid();

            var quotation = await _context.Invoices
                .Include(i => i.ServiceRequest)
                .FirstOrDefaultAsync(i => i.RequestID == requestId
                    && i.IsQuotation
                    && i.QuotationStatus == "PendingAdmin"
                    && i.ServiceRequest.Status != "Cancelled"
                    && !i.ServiceRequest.IsArchived
                    && i.ServiceRequest.TechID == tech.TechID
                    && !i.ServiceRequest.Customer.User.IsArchived);
            if (quotation == null) return NotFound();

            var usages = await _context.JobInventoryUsages
                .Where(u => u.RequestID == requestId && !u.IsDeducted)
                .ToListAsync();
            var partsTotal = usages.Sum(u => u.UnitPrice * u.Quantity);

            return Json(new
            {
                quotation.InvoiceID,
                LaborAmount = Math.Max(0, quotation.TotalAmount - partsTotal),
                Deadline = quotation.ServiceRequest.EstimatedDeadline?.ToString("yyyy-MM-ddTHH:mm"),
                Lines = usages.Select(u => new { ItemId = u.ItemID, u.Quantity })
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditQuotation(int invoiceId, int[] itemIds, int[] quantities, decimal laborAmount, string? serviceNotes, DateTime proposedDeadline)
        {
            var user = await _userManager.GetUserAsync(User);
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);
            if (tech == null) return Forbid();

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var quotation = await _context.Invoices
                .Include(i => i.ServiceRequest)
                .FirstOrDefaultAsync(i => i.InvoiceID == invoiceId
                    && i.IsQuotation
                    && i.QuotationStatus == "PendingAdmin"
                    && i.ServiceRequest.Status != "Cancelled"
                    && !i.ServiceRequest.IsArchived
                    && i.ServiceRequest.TechID == tech.TechID
                    && !i.ServiceRequest.Customer.User.IsArchived);
            if (quotation == null)
            {
                TempData["ErrorMessage"] = "This quotation can no longer be edited because it has already been reviewed.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid || itemIds.Length != quantities.Length || quantities.Any(quantity => quantity <= 0) || itemIds.Any(id => id <= 0) || laborAmount < 0)
            {
                TempData["ErrorMessage"] = "Enter valid labor and completion details. Parts are optional; added parts need a positive quantity.";
                return RedirectToAction(nameof(Index));
            }

            var requested = itemIds.Zip(quantities).Where(x => x.Second > 0).GroupBy(x => x.First).ToDictionary(g => g.Key, g => g.Sum(x => x.Second));
            var items = await _context.InventoryItems
                .Where(i => !i.IsArchived && requested.Keys.Contains(i.ItemID))
                .ToListAsync();
            if (items.Count != requested.Count || items.Any(i => requested[i.ItemID] > i.StockQuantity))
            {
                TempData["ErrorMessage"] = "One or more selected quantities exceed available stock.";
                return RedirectToAction(nameof(Index));
            }

            var partsTotal = items.Sum(i => i.UnitPrice * requested[i.ItemID]);
            var breakdown = string.Join("\n", items.Select(i => $"{i.ItemName} × {requested[i.ItemID]} — ₱{i.UnitPrice * requested[i.ItemID]:N2}"));
            if (laborAmount > 0) breakdown += $"\nLabor — ₱{laborAmount:N2}";
            if (!string.IsNullOrWhiteSpace(serviceNotes)) breakdown += $"\nNotes: {serviceNotes.Trim()}";

            quotation.TotalAmount = partsTotal + laborAmount;
            quotation.BreakdownDetails = breakdown;
            quotation.ServiceRequest.EstimatedDeadline = proposedDeadline;

            var oldUsages = await _context.JobInventoryUsages.Where(u => u.RequestID == quotation.RequestID && !u.IsDeducted).ToListAsync();
            _context.JobInventoryUsages.RemoveRange(oldUsages);
            _context.JobInventoryUsages.AddRange(items.Select(i => new JobInventoryUsage { RequestID = quotation.RequestID, ItemID = i.ItemID, Quantity = requested[i.ItemID], UnitPrice = i.UnitPrice }));

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = "Quotation updated and resubmitted for admin review.";
            return RedirectToAction(nameof(Index), new { jobId = quotation.RequestID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(PrivateUploadService.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = PrivateUploadService.MaxRequestBytes)]
        public async Task<IActionResult> UploadDeliverable(int requestId, string phase, string description, IFormFile? imageFile, [FromServices] PrivateUploadService uploads)
        {
            if (phase is not ("Diagnosis" or "FinalProof") || string.IsNullOrWhiteSpace(description)) return BadRequest();
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);
            if (tech == null) return Forbid();

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var job = await _context.ServiceRequests
                .ActionableTechnicianAssignments(tech.TechID)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);
            if (job == null) return NotFound();

            if ((phase == "FinalProof" && (job.Status != "In Progress"
                    || !job.Check1_Diagnostic || !job.Check2_Hardware || !job.Check3_Firmware || !job.Check4_QA || !job.Check5_Handover
                    || imageFile == null || imageFile.Length == 0))
                || (phase == "Diagnosis" && job.Status is not ("Pending" or "Diagnosing")))
            {
                TempData["ErrorMessage"] = "Complete the repair checklist and attach proof while the job is in progress before requesting customer sign-off.";
                return RedirectToAction(nameof(Index), new { jobId = requestId, tab = "workflow" });
            }

            if (phase == "FinalProof" && !await _context.Invoices.HasPaidInvoiceAsync(requestId))
            {
                TempData["ErrorMessage"] = "Final proof cannot be submitted until the approved invoice has been paid.";
                return RedirectToAction(nameof(Index), new { jobId = requestId });
            }

            StoredUpload? stored;
            try { stored = await uploads.StoreAsync(imageFile, "deliverables", HttpContext.RequestAborted); }
            catch (UploadValidationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
            await using var upload = stored;
            var imagePath = upload?.Url;

            var deliverable = new JobDeliverable
            {
                RequestID = requestId,
                Phase = phase,
                Description = description,
                ImagePath = imagePath,
                CreatedAt = DateTime.UtcNow
            };
            _context.JobDeliverables.Add(deliverable);

            if (phase == "Diagnosis")
            {
                job.Status = "Diagnosing";
            }
            else if (phase == "FinalProof")
            {
                job.Status = "PendingCustomerReview";
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            upload?.Complete();
            TempData["SuccessMessage"] = phase == "FinalProof" ? "Proof submitted. Awaiting customer sign-off." : "Diagnosis uploaded successfully.";
            return RedirectToAction(nameof(Index), new { jobId = requestId, tab = "workflow" });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveChecklistProgress(int requestId, bool c1, bool c2, bool c3, bool c4, bool c5)
        {
            var user = await _userManager.GetUserAsync(User);
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);

            if (tech == null) return NotFound();

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var job = await _context.ServiceRequests
                .ActionableTechnicianAssignments(tech.TechID)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);
            if (job == null) return NotFound();
            if (job.Status != "In Progress") return BadRequest("The checklist can only change while repair is in progress.");

            job.Check1_Diagnostic = c1;
            job.Check2_Hardware = c2;
            job.Check3_Firmware = c3;
            job.Check4_QA = c4;
            job.Check5_Handover = c5;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Ok(new { success = true });
        }
    }
}
