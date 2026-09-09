using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Services;

public class NotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public NotificationService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<NotificationFeedViewModel> GetFeedAsync(
        ApplicationUser user,
        string? category = null,
        int? take = null)
    {
        var role = await ResolveRoleAsync(user);
        if (role == null)
            return new NotificationFeedViewModel();

        var baseQuery = _context.UserNotifications
            .AsNoTracking()
            .Where(n => n.RecipientUserID == user.Id && n.AudienceRole == role);

        var allNotifications = await baseQuery
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.NotificationID)
            .Take(150)
            .ToListAsync();

        var categories = CategoriesFor(role);
        var activeCategory = categories.Contains(category ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? categories.First(c => c.Equals(category, StringComparison.OrdinalIgnoreCase))
            : "All";

        IEnumerable<UserNotification> visible = allNotifications;
        if (activeCategory != "All")
            visible = visible.Where(n => n.Category.Equals(activeCategory, StringComparison.OrdinalIgnoreCase));
        if (take.HasValue)
            visible = visible.Where(n => !n.IsRead).Take(take.Value);

        var counts = categories.ToDictionary(
            c => c,
            c => c == "All" ? allNotifications.Count : allNotifications.Count(n => n.Category == c));

        return new NotificationFeedViewModel
        {
            Notifications = visible.ToList(),
            Categories = categories,
            CategoryCounts = counts,
            ActiveCategory = activeCategory,
            Area = AreaFor(role),
            UnreadCount = allNotifications.Count(n => !n.IsRead),
            TotalCount = allNotifications.Count
        };
    }

    public async Task<bool> SetReadStateAsync(ApplicationUser user, int notificationId, bool isRead)
    {
        var role = await ResolveRoleAsync(user);
        if (role == null) return false;

        var notification = await _context.UserNotifications
            .FirstOrDefaultAsync(n => n.NotificationID == notificationId
                && n.RecipientUserID == user.Id
                && n.AudienceRole == role);
        if (notification == null)
            return false;

        notification.IsRead = isRead;
        notification.ReadAt = isRead ? DateTime.UtcNow : null;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task MarkAllReadAsync(ApplicationUser user)
    {
        var role = await ResolveRoleAsync(user);
        if (role == null) return;

        var unread = await _context.UserNotifications
            .Where(n => n.RecipientUserID == user.Id && n.AudienceRole == role && !n.IsRead)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        if (unread.Count > 0)
            await _context.SaveChangesAsync();
    }

    public async Task<string?> MarkReadAndGetTargetAsync(ApplicationUser user, int notificationId)
    {
        var role = await ResolveRoleAsync(user);
        if (role == null) return null;

        var notification = await _context.UserNotifications
            .FirstOrDefaultAsync(n => n.NotificationID == notificationId
                && n.RecipientUserID == user.Id
                && n.AudienceRole == role);
        if (notification == null)
            return null;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return IsSafeLocalPath(notification.ActionUrl) ? notification.ActionUrl : null;
    }

    private async Task<string?> ResolveRoleAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(Roles.Administrator)) return Roles.Administrator;
        if (roles.Contains(Roles.Technician)) return Roles.Technician;
        if (roles.Contains(Roles.Customer)) return Roles.Customer;
        return null;
    }

    public async Task SynchronizeUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        // Serialize workers across app instances on the recipient row before reading candidates.
        var users = _context.Database.IsMySql()
            ? await _context.Users.FromSqlInterpolated($"SELECT * FROM AspNetUsers WHERE Id = {userId} FOR UPDATE").ToListAsync(cancellationToken)
            : await _context.Users.Where(u => u.Id == userId).ToListAsync(cancellationToken);
        var user = users.SingleOrDefault();
        if (user == null || user.IsArchived) return;
        var role = await ResolveRoleAsync(user);
        if (role == null) return;
        await SynchronizeAsync(user, role);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SynchronizeAsync(ApplicationUser user, string role)
    {
        var candidates = role switch
        {
            Roles.Administrator => await BuildAdminCandidatesAsync(),
            Roles.Customer => await BuildCustomerCandidatesAsync(user),
            Roles.Technician => await BuildTechnicianCandidatesAsync(user),
            _ => []
        };
        var now = DateTime.UtcNow;
        var existing = await _context.UserNotifications
            .Where(n => n.RecipientUserID == user.Id && n.AudienceRole == role)
            .ToListAsync();
        var candidatesByFamily = candidates
            .GroupBy(candidate => NotificationFamily(candidate.SourceKey))
            .ToDictionary(group => group.Key, group => group.Last());
        var existingByFamily = existing
            .Where(notification => IsManagedSource(notification.SourceKey, role))
            .GroupBy(notification => NotificationFamily(notification.SourceKey))
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var family in existingByFamily.Keys.Union(candidatesByFamily.Keys))
        {
            existingByFamily.TryGetValue(family, out var familyNotifications);
            familyNotifications ??= [];

            if (!candidatesByFamily.TryGetValue(family, out var candidate))
            {
                _context.UserNotifications.RemoveRange(familyNotifications);
                continue;
            }

            var current = familyNotifications.FirstOrDefault(notification => notification.SourceKey == candidate.SourceKey);
            _context.UserNotifications.RemoveRange(familyNotifications.Where(notification => notification != current));

            if (current == null)
            {
                candidate.RecipientUserID = user.Id;
                candidate.AudienceRole = role;
                candidate.CreatedAt = now;
                _context.UserNotifications.Add(candidate);
                continue;
            }

            var contentChanged = current.Category != candidate.Category
                || current.Icon != candidate.Icon
                || current.Title != candidate.Title
                || current.Message != candidate.Message
                || current.ActionUrl != candidate.ActionUrl;
            if (!contentChanged) continue;

            current.Category = candidate.Category;
            current.Icon = candidate.Icon;
            current.Title = candidate.Title;
            current.Message = candidate.Message;
            current.ActionUrl = candidate.ActionUrl;
            current.CreatedAt = now;
            current.IsRead = false;
            current.ReadAt = null;
        }

        if (_context.ChangeTracker.HasChanges())
            await _context.SaveChangesAsync();
    }

    private async Task<List<UserNotification>> BuildAdminCandidatesAsync()
    {
        var notifications = new List<UserNotification>();

        var pendingRequests = await _context.ServiceRequests
            .AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => !r.IsArchived
                && r.Status == "Pending"
                && !r.Customer.User.IsArchived
                && !(r.IsCancellationRequested && r.CancellationStatus == "Pending"))
            .OrderByDescending(r => r.RequestID)
            .ToListAsync();
        notifications.AddRange(pendingRequests.Select(request => New(
            $"admin:request:{request.RequestID}:pending",
            "Jobs",
            "clipboard",
            request.TechID.HasValue ? "Service request is ready to start" : "Service request needs a technician",
            $"JOB-{request.RequestID:D4} for {request.Customer.FirstName} {request.Customer.LastName} is scheduled for {request.ScheduledDate:MMM d, yyyy 'at' h:mm tt}.",
            $"/Admin/Operations/ServiceRequests?requestId={request.RequestID}")));

        var cancellations = await _context.ServiceRequests
            .AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => !r.IsArchived
                && !r.Customer.User.IsArchived
                && r.IsCancellationRequested
                && r.CancellationStatus == "Pending")
            .ToListAsync();
        notifications.AddRange(cancellations.Select(request => New(
            $"admin:request:{request.RequestID}:cancellation-pending",
            "Jobs",
            "alert-circle",
            "Cancellation request needs review",
            $"{request.Customer.FirstName} {request.Customer.LastName} requested cancellation of JOB-{request.RequestID:D4}.",
            $"/Admin/Operations/ServiceRequests?requestId={request.RequestID}")));

        var quotations = await _context.Invoices
            .AsNoTracking()
            .Include(i => i.ServiceRequest)
            .ThenInclude(r => r.Technician)
            .Where(i => i.IsQuotation
                && i.QuotationStatus == "PendingAdmin"
                && !i.ServiceRequest.Customer.User.IsArchived
                && i.ServiceRequest.Status != "Cancelled")
            .OrderByDescending(i => i.DateIssued)
            .ToListAsync();
        notifications.AddRange(quotations.Select(quote => New(
            $"admin:quotation:{quote.InvoiceID}:pending-admin",
            "Quotations",
            "file-text",
            "Quotation needs approval",
            $"QT-{quote.InvoiceID:D4} for JOB-{quote.RequestID:D4} totals ₱{quote.TotalAmount:N2} and is waiting for admin review.",
            "/Admin/Finance/Quotations")));

        var lowStockItems = await _context.InventoryItems
            .AsNoTracking()
            .Where(item => !item.IsArchived && item.StockQuantity <= item.ReorderLevel)
            .OrderBy(item => item.StockQuantity)
            .ToListAsync();
        notifications.AddRange(lowStockItems.Select(item => New(
            $"admin:inventory:{item.ItemID}:low:{item.StockQuantity}",
            "Inventory",
            "package",
            "Inventory is at or below reorder level",
            $"{item.ItemName} has {item.StockQuantity} unit{(item.StockQuantity == 1 ? string.Empty : "s")} remaining; reorder level is {item.ReorderLevel}.",
            "/Admin/Finance/Inventory")));

        var supportTickets = await _context.SupportTickets
            .AsNoTracking()
            .Include(ticket => ticket.Customer)
            .Where(ticket => ticket.Status == "Open" || ticket.Status == "In Progress")
            .OrderByDescending(ticket => ticket.CreatedAt)
            .ToListAsync();
        notifications.AddRange(supportTickets.Select(ticket => New(
            $"admin:support:{ticket.SupportTicketID}:{ticket.Status.ToLowerInvariant()}",
            "Support",
            "message-circle",
            ticket.Status == "Open" ? "New customer support request" : "Customer support request in progress",
            $"{ticket.Customer.FirstName} {ticket.Customer.LastName}: {ticket.Subject}",
            $"/Admin/Support?status=All&ticketId={ticket.SupportTicketID}")));

        return notifications;
    }

    private async Task<List<UserNotification>> BuildCustomerCandidatesAsync(ApplicationUser user)
    {
        var customer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.UserID == user.Id);
        if (customer == null) return [];

        var notifications = new List<UserNotification>();
        if (user.PrefApptReminders)
        {
            var requests = await _context.ServiceRequests
                .AsNoTracking()
                .Include(r => r.Technician)
                .Where(r => r.CustomerID == customer.CustomerID
                    && (!r.IsArchived || r.Status == "Cancelled"))
                .OrderByDescending(r => r.RequestID)
                .ToListAsync();

            foreach (var request in requests)
            {
                var cancellationState = request.CancellationStatus?.ToLowerInvariant() ?? "none";
                var title = request.CancellationStatus switch
                {
                    "Pending" => "Cancellation request is being reviewed",
                    "Approved" => "Cancellation request approved",
                    "Rejected" => "Cancellation request declined",
                    _ => request.Status switch
                    {
                        "Pending" => "Service request received",
                        "Scheduled" => "Service appointment confirmed",
                        "In Progress" => "Your service is in progress",
                        "Diagnosing" => "Your device is being diagnosed",
                        "PendingCustomerReview" => "Service work is ready for your approval",
                        "Completed" => "Service request completed",
                        "Cancelled" => "Service request cancelled",
                        _ => $"Service request updated to {request.Status}"
                    }
                };
                var technician = request.Technician == null
                    ? "A technician will be assigned soon."
                    : $"Assigned technician: {request.Technician.FirstName} {request.Technician.LastName}.";
                notifications.Add(New(
                    $"customer:request:{request.RequestID}:{request.Status.ToLowerInvariant()}:{cancellationState}",
                    "Jobs",
                    request.Status == "Completed" ? "check-circle" : "wrench",
                    title,
                    $"JOB-{request.RequestID:D4} is scheduled for {request.ScheduledDate:MMM d, yyyy 'at' h:mm tt}. {technician}",
                    request.Status == "Cancelled"
                        ? $"/Customer/ServiceRequests?jobId={request.RequestID}&filter=Cancelled"
                        : $"/Customer/ServiceRequests?jobId={request.RequestID}"));
            }
        }

        var invoices = await _context.Invoices
            .AsNoTracking()
            .Include(i => i.ServiceRequest)
            .Where(i => i.ServiceRequest.CustomerID == customer.CustomerID
                && i.ServiceRequest.Status != "Cancelled")
            .OrderByDescending(i => i.DateIssued)
            .ToListAsync();

        foreach (var invoice in invoices)
        {
            if (invoice.IsQuotation && user.PrefQuotations)
            {
                var title = invoice.QuotationStatus switch
                {
                    "Approved" or "ApprovedByAdmin" => "Quotation ready for your review",
                    "Rejected" => "Quotation returned for revision",
                    "PendingAdmin" => "Quotation is under admin review",
                    _ => "Quotation updated"
                };
                notifications.Add(New(
                    $"customer:quotation:{invoice.InvoiceID}:{invoice.QuotationStatus.ToLowerInvariant()}",
                    "Quotations",
                    "file-text",
                    title,
                    $"QT-{invoice.InvoiceID:D4} for JOB-{invoice.RequestID:D4} totals ₱{invoice.TotalAmount:N2}.",
                    "/Customer/Quotations"));
            }
            else if (!invoice.IsQuotation && user.PrefInvoices)
            {
                notifications.Add(New(
                    $"customer:invoice:{invoice.InvoiceID}:{invoice.PaymentStatus.ToLowerInvariant()}",
                    "Billing",
                    invoice.PaymentStatus == "Paid" ? "check-circle" : "credit-card",
                    invoice.PaymentStatus == "Paid" ? "Payment confirmed" : "New invoice available",
                    $"INV-{invoice.InvoiceID:D4} for JOB-{invoice.RequestID:D4} totals ₱{invoice.TotalAmount:N2} and is {invoice.PaymentStatus.ToLowerInvariant()}.",
                    "/Customer/BillsAndPayments"));
            }
        }

        var supportTickets = await _context.SupportTickets
            .AsNoTracking()
            .Where(ticket => ticket.CustomerID == customer.CustomerID)
            .OrderByDescending(ticket => ticket.CreatedAt)
            .ToListAsync();
        notifications.AddRange(supportTickets.Select(ticket => New(
            $"customer:support:{ticket.SupportTicketID}:{ticket.Status.ToLowerInvariant()}:{ticket.RespondedAt?.Ticks ?? 0}",
            "Support",
            "message-circle",
            ticket.AdminResponse == null ? "Support request received" : "Support replied to your request",
            ticket.AdminResponse == null
                ? $"Your request “{ticket.Subject}” is {ticket.Status.ToLowerInvariant()}."
                : $"Your request “{ticket.Subject}” has a new response and is {ticket.Status.ToLowerInvariant()}.",
            $"/Customer/Support?ticketId={ticket.SupportTicketID}")));

        return notifications;
    }

    private async Task<List<UserNotification>> BuildTechnicianCandidatesAsync(ApplicationUser user)
    {
        var technician = await _context.Technicians.AsNoTracking().FirstOrDefaultAsync(t => t.UserID == user.Id);
        if (technician == null) return [];

        var notifications = new List<UserNotification>();
        var requests = await _context.ServiceRequests
            .AsNoTracking()
            .Include(r => r.Customer)
            .ActionableTechnicianAssignments(technician.TechID)
            .OrderByDescending(r => r.RequestID)
            .ToListAsync();

        foreach (var request in requests)
        {
            var title = request.Status switch
            {
                "Pending" => "New job assigned to you",
                "Scheduled" => "Job schedule confirmed",
                "In Progress" => "Job is marked in progress",
                "Diagnosing" => "Diagnosis stage recorded",
                "PendingCustomerReview" => "Awaiting customer approval",
                "Completed" => "Job marked completed",
                "Cancelled" => "Assigned job cancelled",
                _ => $"Assigned job updated to {request.Status}"
            };
            notifications.Add(New(
                $"technician:request:{request.RequestID}:{request.Status.ToLowerInvariant()}",
                "Jobs",
                request.Status == "Completed" ? "check-circle" : "tool",
                title,
                $"JOB-{request.RequestID:D4} for {request.Customer.FirstName} {request.Customer.LastName} is scheduled for {request.ScheduledDate:MMM d, yyyy 'at' h:mm tt}.",
                $"/Technician/AssignedJobs?jobId={request.RequestID}"));
        }

        var quotations = await _context.Invoices
            .AsNoTracking()
            .Include(i => i.ServiceRequest)
            .Where(i => i.IsQuotation
                && i.ServiceRequest.TechID == technician.TechID
                && !i.ServiceRequest.IsArchived
                && !i.ServiceRequest.Customer.User.IsArchived
                && i.ServiceRequest.Status != "Cancelled")
            .OrderByDescending(i => i.DateIssued)
            .ToListAsync();
        notifications.AddRange(quotations.Select(quote => New(
            $"technician:quotation:{quote.InvoiceID}:{quote.QuotationStatus.ToLowerInvariant()}",
            "Quotations",
            "file-text",
            quote.QuotationStatus switch
            {
                "Approved" or "ApprovedByAdmin" => "Your quotation was approved",
                "Rejected" => "Your quotation needs revision",
                "PendingAdmin" => "Quotation submitted for admin review",
                _ => "Quotation draft updated"
            },
            $"QT-{quote.InvoiceID:D4} for JOB-{quote.RequestID:D4} totals ₱{quote.TotalAmount:N2}.",
            $"/Technician/AssignedJobs?jobId={quote.RequestID}")));

        return notifications;
    }

    private static UserNotification New(
        string sourceKey,
        string category,
        string icon,
        string title,
        string message,
        string actionUrl) => new()
        {
            SourceKey = sourceKey,
            Category = category,
            Icon = icon,
            Title = title,
            Message = message,
            ActionUrl = actionUrl
        };

    private static string NotificationFamily(string sourceKey)
    {
        var parts = sourceKey.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3
            ? string.Join(':', parts.Take(3))
            : sourceKey;
    }

    private static bool IsManagedSource(string sourceKey, string role)
    {
        var prefix = role switch
        {
            Roles.Administrator => "admin:",
            Roles.Technician => "technician:",
            Roles.Customer => "customer:",
            _ => string.Empty
        };
        return prefix.Length > 0 && sourceKey.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> CategoriesFor(string role) => role switch
    {
        Roles.Administrator => ["All", "Jobs", "Quotations", "Inventory", "Support"],
        Roles.Technician => ["All", "Jobs", "Quotations"],
        _ => ["All", "Jobs", "Quotations", "Billing", "Support"]
    };

    private static string AreaFor(string role) => role switch
    {
        Roles.Administrator => "Admin",
        Roles.Technician => "Technician",
        _ => "Customer"
    };

    private static bool IsSafeLocalPath(string path) =>
        path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal);
}
