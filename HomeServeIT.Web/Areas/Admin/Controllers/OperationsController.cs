using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
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

        public OperationsController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
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
        public async Task<IActionResult> AddServiceRequest(int customerId, string issueDescription, DateTime scheduledDate, string? serviceCategory, IFormFile? image)
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

            string? imagePath = null;
            if (image != null && image.Length > 0)
            {
                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "service-requests");
                Directory.CreateDirectory(uploadsDir);
                var ext = Path.GetExtension(image.FileName).ToLowerInvariant();
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                if (allowed.Contains(ext))
                {
                    var fileName = $"{Guid.NewGuid()}{ext}";
                    var filePath = Path.Combine(uploadsDir, fileName);
                    using var stream = new FileStream(filePath, FileMode.Create);
                    await image.CopyToAsync(stream);
                    imagePath = $"/uploads/service-requests/{fileName}";
                }
            }

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
            TempData["SuccessMessage"] = $"Service Request created successfully for Customer #{customerId}.";
            return RedirectToAction(nameof(ServiceRequests));
        }

        public async Task<IActionResult> Technicians()
        {
            var techs = await _context.Technicians
                .Include(t => t.User)
                .ToListAsync();

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

            return View(techs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTechnician([FromServices] Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager, string firstName, string lastName, string email, string phone, string specialty)
        {
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, PhoneNumber = phone, FullName = $"{firstName} {lastName}" };
                var result = await userManager.CreateAsync(user, "TempPass123!");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, Roles.Technician);
                    var tech = new HomeServeIT.Web.Models.Technician
                    {
                        UserID = user.Id,
                        FirstName = firstName,
                        LastName = lastName,
                        Specialty = specialty
                    };
                    _context.Technicians.Add(tech);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Technician {firstName} {lastName} added successfully.";
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
        public async Task<IActionResult> EditTechnician([FromServices] Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager, int techId, string firstName, string lastName, string email, string phone, string specialty, string? password, bool isAvailable, bool isSuspended)
        {
            var tech = await _context.Technicians.Include(t => t.User).FirstOrDefaultAsync(t => t.TechID == techId);
            if (tech != null && tech.User != null)
            {
                // Update Technician Entity
                tech.FirstName = firstName;
                tech.LastName = lastName;
                tech.Specialty = specialty;
                tech.IsAvailable = isAvailable;

                // Update ApplicationUser Entity
                var user = tech.User;
                user.Email = email;
                user.UserName = email;
                user.PhoneNumber = phone;
                user.FullName = $"{firstName} {lastName}";

                await userManager.UpdateAsync(user);

                // Handle Password change
                if (!string.IsNullOrEmpty(password))
                {
                    var token = await userManager.GeneratePasswordResetTokenAsync(user);
                    await userManager.ResetPasswordAsync(user, token, password);
                }

                // Handle Suspend status
                if (isSuspended)
                {
                    await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                    tech.IsAvailable = false; // Auto-unavailable if suspended
                }
                else
                {
                    await userManager.SetLockoutEndDateAsync(user, null);
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Technician {firstName} {lastName} updated successfully.";
            }
            return RedirectToAction(nameof(Technicians));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int requestId, string status)
        {
            var sr = await _context.ServiceRequests.FindAsync(requestId);
            if (sr != null)
            {
                var allowedStatuses = new[] { "Pending", "Diagnosing", "In Progress", "PendingCustomerReview", "Completed", "Cancelled" };
                if (string.IsNullOrWhiteSpace(status) || !allowedStatuses.Contains(status))
                {
                    TempData["ErrorMessage"] = "Invalid service status.";
                    return RedirectToAction(nameof(ServiceRequests));
                }

                if (status is "In Progress" or "PendingCustomerReview" or "Completed"
                    && !await _context.Invoices.HasPaidInvoiceAsync(requestId))
                {
                    TempData["ErrorMessage"] = $"JOB-{requestId:D4} cannot start or be completed until its approved invoice has been paid.";
                    return RedirectToAction(nameof(ServiceRequests));
                }

                if (status == "In Progress")
                {
                    var inventoryError = await JobInventoryService.DeductForJobStartAsync(_context, requestId);
                    if (inventoryError != null)
                    {
                        TempData["ErrorMessage"] = inventoryError;
                        return RedirectToAction(nameof(ServiceRequests));
                    }
                }
                sr.Status = status;
                if (status == "Cancelled")
                {
                    sr.TechID = null;
                    sr.IsArchived = true;
                    await ServiceCancellationCleanup.RemoveFinancialArtifactsAsync(_context, sr.RequestID);
                }
                if (status == "Completed" && !sr.CompletedDate.HasValue)
                {
                    sr.CompletedDate = DateTime.UtcNow;
                }
                else if (status != "Completed")
                {
                    sr.CompletedDate = null;
                }
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Service Request #{requestId} status updated to {status}.";
            }
            return status == "Cancelled"
                ? RedirectToAction("ArchivedUsers", "System", new { area = "Admin" })
                : RedirectToAction(nameof(ServiceRequests));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignTechnician(int requestId, int techId)
        {
            var sr = await _context.ServiceRequests.FindAsync(requestId);
            if (sr != null)
            {
                var techExists = await _context.Technicians.AnyAsync(t => t.TechID == techId);
                if (!techExists)
                {
                    TempData["ErrorMessage"] = $"Technician #{techId} does not exist.";
                    return RedirectToAction(nameof(ServiceRequests));
                }

                if (techId != sr.TechID)
                {
                    bool conflict = await _context.ServiceRequests
                        .AnyAsync(r => r.TechID == techId
                                       && r.RequestID != requestId
                                       && r.Status != "Completed"
                                       && r.Status != "Cancelled"
                                       && r.ScheduledDate.Date == sr.ScheduledDate.Date);
                    if (conflict)
                    {
                        TempData["ErrorMessage"] = $"Technician #{techId} is already assigned to another job on {sr.ScheduledDate:MMM d, yyyy}.";
                        return RedirectToAction(nameof(ServiceRequests));
                    }
                }

                sr.TechID = techId;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Technician assigned to Service Request #{requestId}.";
            }
            return RedirectToAction(nameof(ServiceRequests));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRequest(int requestId)
        {
            var sr = await _context.ServiceRequests.FindAsync(requestId);
            if (sr != null)
            {
                sr.Status = "Cancelled";
                sr.TechID = null; // Unassign technician when cancelled
                sr.IsArchived = true;
                await ServiceCancellationCleanup.RemoveFinancialArtifactsAsync(_context, sr.RequestID);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Service Request #{requestId} has been cancelled.";
            }
            else
            {
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
            var request = await _context.ServiceRequests.FindAsync(requestId);
            if (request != null && request.IsCancellationRequested && request.CancellationStatus == "Pending")
            {
                request.CancellationStatus = "Approved";
                request.Status = "Cancelled";
                request.TechID = null;
                request.IsArchived = true;
                await ServiceCancellationCleanup.RemoveFinancialArtifactsAsync(_context, request.RequestID);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Cancellation approved for Request {requestId}.";
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
