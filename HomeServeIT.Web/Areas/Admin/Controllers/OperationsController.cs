using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Areas.Admin.Models;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;

namespace HomeServeIT.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = Roles.Administrator)]
    public class OperationsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly JobInventoryService _jobInventoryService;
        private readonly TechnicianAssignmentService _technicianAssignmentService;

        public OperationsController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            JobInventoryService jobInventoryService,
            TechnicianAssignmentService technicianAssignmentService)
        {
            _context = context;
            _env = env;
            _jobInventoryService = jobInventoryService;
            _technicianAssignmentService = technicianAssignmentService;
        }

        public async Task<IActionResult> ServiceRequests()
        {
            var requests = await _context.ServiceRequests
                .Include(r => r.Customer)
                .Include(r => r.Technician!)
                .ThenInclude(t => t.User)
                .Where(r => !r.IsArchived && r.Status != "Cancelled")
                .OrderByDescending(r => r.ScheduledDate)
                .ToListAsync();
            
            ViewBag.Customers = await _context.Customers.ToListAsync();
            ViewBag.Technicians = await _context.Technicians.Include(t => t.User).ToListAsync();
            
            return View(requests);
        }

        public IActionResult CancelledRequests() =>
            RedirectToAction("ArchivedUsers", "System", new { area = "Admin" });

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(PrivateUploadService.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = PrivateUploadService.MaxRequestBytes)]
        public async Task<IActionResult> AddServiceRequest(int customerId, string issueDescription, DateTime scheduledDate, string? serviceCategory, IFormFile? image, [FromServices] PrivateUploadService uploads)
        {
            if (string.IsNullOrWhiteSpace(issueDescription))
            {
                TempData["ErrorMessage"] = "Please provide a description of the issue.";
                return RedirectToAction(nameof(ServiceRequests));
            }

            if (scheduledDate <= DateTime.Now.AddMinutes(1))
            {
                TempData["ErrorMessage"] = "Scheduled date must be in the future.";
                return RedirectToAction(nameof(ServiceRequests));
            }

            var customerExists = await _context.Customers.AnyAsync(c => c.CustomerID == customerId);
            if (!customerExists)
            {
                TempData["ErrorMessage"] = $"Customer #{customerId} does not exist.";
                return RedirectToAction(nameof(ServiceRequests));
            }

            StoredUpload? stored;
            try { stored = await uploads.StoreAsync(image, "service-requests", HttpContext.RequestAborted); }
            catch (UploadValidationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(ServiceRequests));
            }
            await using var upload = stored;
            var imagePath = upload?.Url;

            var request = new ServiceRequest
            {
                CustomerID = customerId,
                IssueDescription = issueDescription,
                ScheduledDate = scheduledDate,
                Status = "Pending",
                ServiceCategory = string.IsNullOrWhiteSpace(serviceCategory) ? "Other" : serviceCategory,
                ImagePath = imagePath
            };
            _context.ServiceRequests.Add(request);
            await _context.SaveChangesAsync();
            upload?.Complete();
            TempData["SuccessMessage"] = $"Service Request created successfully for Customer #{customerId}.";
            return RedirectToAction(nameof(ServiceRequests));
        }

        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Technicians()
        {
            var techs = await _context.Technicians
                .Include(t => t.User)
                .ToListAsync();
            var now = DateTimeOffset.UtcNow;
            var activeTechnicians = techs
                .Where(t => t.User != null && !t.User.IsArchived && (t.User.LockoutEnd == null || t.User.LockoutEnd <= now))
                .ToList();
            var suspendedTechnicians = techs
                .Where(t => t.User == null || t.User.IsArchived || t.User.LockoutEnd > now)
                .ToList();

            var activeJobCounts = await _context.ServiceRequests
                .Where(r => r.TechID != null && r.Status != "Completed" && r.Status != "Cancelled" && !r.IsArchived)
                .GroupBy(r => r.TechID!.Value)
                .Select(g => new { TechID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TechID, x => x.Count);

            var completedJobCounts = await _context.ServiceRequests
                .Where(r => r.TechID != null && r.Status == "Completed")
                .GroupBy(r => r.TechID!.Value)
                .Select(g => new { TechID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TechID, x => x.Count);

            ViewBag.ActiveJobCounts = activeJobCounts;
            ViewBag.CompletedJobCounts = completedJobCounts;

            return View(new TechnicianDirectoryViewModel
            {
                ActiveTechnicians = activeTechnicians,
                SuspendedTechnicians = suspendedTechnicians
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTechnician([FromServices] Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager, string firstName, string lastName, string email, string phone, string specialty, [FromServices] HomeServeIT.Web.Services.AccountProfileService profiles)
        {
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, PhoneNumber = phone, FullName = $"{firstName} {lastName}" };
                var temporaryPassword = "Ht!7" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
                var result = await profiles.CreateAsync(user, Roles.Technician, password: temporaryPassword, specialty: specialty);
                if (result.Succeeded)
                {
                    // Cookie TempData is protected by ASP.NET Core Data Protection and consumed on the next page.
                    TempData["CreatedTechnicianEmail"] = user.Email;
                    TempData["CreatedTechnicianPassword"] = temporaryPassword;
                    return RedirectToAction(nameof(Technicians));
                }
                else
                {
                    TempData["ErrorMessage"] = string.Join(", ", result.Errors.Select(e => e.Description));
                }
            }
            else
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                TempData["ErrorMessage"] = "Validation failed: " + string.Join(", ", errors);
            }
            return RedirectToAction(nameof(Technicians));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTechnician([FromServices] HomeServeIT.Web.Services.AccountProfileService profiles, int techId, string firstName, string lastName, string email, string phone, string specialty, string? password, bool isAvailable, bool isSuspended)
        {
            var tech = await _context.Technicians.Include(t => t.User).FirstOrDefaultAsync(t => t.TechID == techId);
            if (tech?.User == null) return NotFound();
            var model = ProfileViewModel.FromUser(tech.User);
            model.FullName = $"{firstName} {lastName}";
            model.Email = email;
            model.Mobile = phone;
            var result = await profiles.UpdateAsync(tech.User, model, password: password,
                specialty: specialty, isAvailable: isAvailable, isSuspended: isSuspended);
            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Succeeded
                ? "Technician updated successfully." : string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Technicians));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int requestId, string status)
        {
            var allowedStatuses = new[] { "Pending", "Diagnosing", "In Progress", "PendingCustomerReview", "Completed", "Cancelled" };
            if (string.IsNullOrWhiteSpace(status) || !allowedStatuses.Contains(status))
            {
                TempData["ErrorMessage"] = "Invalid service status.";
                return RedirectToAction(nameof(ServiceRequests));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var sr = await _context.ServiceRequests.FindAsync(requestId);
            if (sr == null)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = $"Service Request #{requestId} was not found.";
                return RedirectToAction(nameof(ServiceRequests));
            }

            if (status is "In Progress" or "PendingCustomerReview" or "Completed"
                && !await _context.Invoices.HasPaidInvoiceAsync(requestId))
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = $"JOB-{requestId:D4} cannot start or be completed until its approved invoice has been paid.";
                return RedirectToAction(nameof(ServiceRequests));
            }

            if (status == "In Progress")
            {
                var inventoryResult = await _jobInventoryService.DeductForJobStartAsync(
                    requestId,
                    User.Identity?.Name ?? "Administrator");
                if (!inventoryResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = inventoryResult.ErrorMessage;
                    return RedirectToAction(nameof(ServiceRequests));
                }
            }

            if (status == "Completed")
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Only customer sign-off can complete a service request.";
                return RedirectToAction(nameof(ServiceRequests));
            }
            sr.Status = status;
            if (status == "Cancelled")
            {
                sr.TechID = null;
                sr.IsArchived = true;
                await ServiceCancellationCleanup.RemoveFinancialArtifactsAsync(_context, sr.RequestID);
            }
            sr.CompletedDate = null;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = $"Service Request #{requestId} status updated to {status}.";

            return status == "Cancelled"
                ? RedirectToAction("ArchivedUsers", "System", new { area = "Admin" })
                : RedirectToAction(nameof(ServiceRequests));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignTechnician(int requestId, int techId)
        {
            var result = await _technicianAssignmentService.AssignAsync(requestId, techId);
            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(ServiceRequests));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRequest(int requestId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var sr = await _context.ServiceRequests.FindAsync(requestId);
            if (sr != null)
            {
                sr.Status = "Cancelled";
                sr.TechID = null; // Unassign technician when cancelled
                sr.IsArchived = true;
                await ServiceCancellationCleanup.RemoveFinancialArtifactsAsync(_context, sr.RequestID);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                TempData["SuccessMessage"] = $"Service Request #{requestId} has been cancelled.";
            }
            else
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = $"Service Request #{requestId} not found.";
            }
            return sr == null
                ? RedirectToAction(nameof(ServiceRequests))
                : RedirectToAction("ArchivedUsers", "System", new { area = "Admin" });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveCancellation(int requestId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var request = await _context.ServiceRequests.FindAsync(requestId);
            if (request != null && request.IsCancellationRequested && request.CancellationStatus == "Pending")
            {
                request.CancellationStatus = "Approved";
                request.Status = "Cancelled";
                request.TechID = null;
                request.IsArchived = true;
                await ServiceCancellationCleanup.RemoveFinancialArtifactsAsync(_context, request.RequestID);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                TempData["SuccessMessage"] = $"Cancellation approved for Request {requestId}.";
            }
            else
            {
                await transaction.RollbackAsync();
            }
            return request == null
                ? RedirectToAction(nameof(ServiceRequests))
                : RedirectToAction("ArchivedUsers", "System", new { area = "Admin" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectCancellation(int requestId, string rejectReason)
        {
            var request = await _context.ServiceRequests.FindAsync(requestId);
            if (request != null && request.IsCancellationRequested && request.CancellationStatus == "Pending")
            {
                request.CancellationStatus = "Rejected";
                request.CancellationRejectReason = rejectReason;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Cancellation request for JOB-{request.RequestID:D4} was rejected.";
            }
            return RedirectToAction(nameof(ServiceRequests));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveRequest(int requestId)
        {
            var request = await _context.ServiceRequests.FindAsync(requestId);
            if (request == null)
            {
                TempData["ErrorMessage"] = $"JOB-{requestId:D4} could not be found.";
            }
            else if (request.IsArchived)
            {
                TempData["ErrorMessage"] = $"JOB-{request.RequestID:D4} is already archived.";
            }
            else if (request.Status != "Completed")
            {
                TempData["ErrorMessage"] = "Only completed jobs can be archived. Active work must remain visible to the service team.";
            }
            else
            {
                request.IsArchived = true;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"JOB-{request.RequestID:D4} moved to Archive. Its service and financial records were preserved.";
            }

            return RedirectToAction(nameof(ServiceRequests));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreRequest(int requestId)
        {
            var request = await _context.ServiceRequests.FindAsync(requestId);
            if (request != null && request.IsArchived)
            {
                request.IsArchived = false;
                var restoredStatus = request.Status;
                if (request.Status == "Cancelled")
                {
                    request.Status = "Pending";
                    request.IsCancellationRequested = false;
                    request.CancellationStatus = null;
                    request.CancellationReason = null;
                    request.CancellationRejectReason = null;
                }
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = restoredStatus == "Cancelled"
                    ? $"JOB-{request.RequestID:D4} was reopened as a pending request."
                    : $"JOB-{request.RequestID:D4} was restored to Jobs & Requests with its completed status preserved.";
            }

            return RedirectToAction(nameof(ServiceRequests));
        }

        [HttpGet]
        public async Task<IActionResult> GetChatLogs(int requestId)
        {
            var messages = await _context.ServiceMessages
                .Where(m => m.RequestID == requestId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new {
                    m.MessageID,
                    m.Content,
                    m.Timestamp,
                    m.SenderID,
                    m.IsRead
                })
                .ToListAsync();
            return Json(messages);
        }
    }
}
