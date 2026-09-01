using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        public AssignedJobsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment env)
        {
            _context = context;
            _userManager = userManager;
            _env = env;
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

            ViewBag.InventoryItems = await _context.InventoryItems
                .Where(i => i.StockQuantity > 0)
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

            var request = await _context.ServiceRequests
                .ActionableTechnicianAssignments(tech.TechID)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);
            if (request == null) return NotFound();

            var allowedStatuses = new[] { "Diagnosing", "In Progress", "PendingCustomerReview", "Completed", "PendingAdminApproval", "Pending" };
            if (string.IsNullOrWhiteSpace(status) || !allowedStatuses.Contains(status))
            {
                TempData["ErrorMessage"] = "Invalid status value.";
                return RedirectToAction(nameof(Index));
            }

            if (status is "In Progress" or "PendingCustomerReview" or "Completed"
                && !await _context.Invoices.HasPaidInvoiceAsync(requestId))
            {
                TempData["ErrorMessage"] = "This job cannot start or be completed until its approved invoice has been paid.";
                return RedirectToAction(nameof(Index), new { jobId = requestId });
            }

            if (status == "In Progress")
            {
                var inventoryError = await JobInventoryService.DeductForJobStartAsync(_context, requestId);
                if (inventoryError != null)
                {
                    TempData["ErrorMessage"] = inventoryError;
                    return RedirectToAction(nameof(Index));
                }
            }

            if (status == "Completed")
            {
                bool checklistComplete = request.Check1_Diagnostic && request.Check2_Hardware &&
                                         request.Check3_Firmware && request.Check4_QA && request.Check5_Handover;
                if (!checklistComplete)
                {
                    TempData["ErrorMessage"] = "You must complete the entire job checklist before marking this job as Completed.";
                    return RedirectToAction(nameof(Index));
                }
                request.CompletedDate = DateTime.UtcNow;
            }
            else
            {
                request.CompletedDate = null;
            }

            request.Status = status;
            await _context.SaveChangesAsync();

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

            var job = await _context.ServiceRequests
                .ActionableTechnicianAssignments(tech.TechID)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);
            if (job == null) return NotFound();
            if (job.Status == "Cancelled")
            {
                TempData["ErrorMessage"] = "This service was cancelled, so a quotation can no longer be submitted.";
                return RedirectToAction(nameof(Index));
            }

            if (itemIds.Length == 0 || itemIds.Length != quantities.Length)
            {
                TempData["ErrorMessage"] = "Select at least one in-stock part and quantity.";
                return RedirectToAction(nameof(Index));
            }

            var requested = itemIds.Zip(quantities).Where(x => x.Second > 0).GroupBy(x => x.First).ToDictionary(g => g.Key, g => g.Sum(x => x.Second));
            var items = await _context.InventoryItems.Where(i => requested.Keys.Contains(i.ItemID)).ToListAsync();
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

            TempData["SuccessMessage"] = "Quotation sent to Admin for approval.";
            return RedirectToAction(nameof(Index));
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

            if (itemIds.Length == 0 || itemIds.Length != quantities.Length || laborAmount < 0)
            {
                TempData["ErrorMessage"] = "Select at least one valid inventory item.";
                return RedirectToAction(nameof(Index));
            }

            var requested = itemIds.Zip(quantities).Where(x => x.Second > 0).GroupBy(x => x.First).ToDictionary(g => g.Key, g => g.Sum(x => x.Second));
            var items = await _context.InventoryItems.Where(i => requested.Keys.Contains(i.ItemID)).ToListAsync();
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
            TempData["SuccessMessage"] = "Quotation updated and resubmitted for admin review.";
            return RedirectToAction(nameof(Index), new { jobId = quotation.RequestID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDeliverable(int requestId, string phase, string description, IFormFile? imageFile)
        {
            var user = await _userManager.GetUserAsync(User);
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);
            if (tech == null) return Forbid();

            var job = await _context.ServiceRequests
                .ActionableTechnicianAssignments(tech.TechID)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);
            if (job == null) return NotFound();

            if (phase == "FinalProof" && !await _context.Invoices.HasPaidInvoiceAsync(requestId))
            {
                TempData["ErrorMessage"] = "Final proof cannot be submitted until the approved invoice has been paid.";
                return RedirectToAction(nameof(Index), new { jobId = requestId });
            }

            string? imagePath = null;
            if (imageFile != null && imageFile.Length > 0)
            {
                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "deliverables");
                Directory.CreateDirectory(uploadsFolder);

                var ext = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
                var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };
                if (!allowedExts.Contains(ext))
                {
                    TempData["ErrorMessage"] = "Unsupported file type.";
                    return RedirectToAction(nameof(Index));
                }

                string uniqueFileName = Guid.NewGuid().ToString() + ext;
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(fileStream);
                }
                imagePath = "/uploads/deliverables/" + uniqueFileName;
            }

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
            TempData["SuccessMessage"] = "Deliverable uploaded successfully.";
            return RedirectToAction(nameof(Index));
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveChecklistProgress(int requestId, bool c1, bool c2, bool c3, bool c4, bool c5)
        {
            var user = await _userManager.GetUserAsync(User);
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);

            if (tech == null) return NotFound();

            var job = await _context.ServiceRequests
                .ActionableTechnicianAssignments(tech.TechID)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);
            if (job == null) return NotFound();

            job.Check1_Diagnostic = c1;
            job.Check2_Hardware = c2;
            job.Check3_Firmware = c3;
            job.Check4_QA = c4;
            job.Check5_Handover = c5;

            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }
    }
}
