using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    public class FinanceController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public FinanceController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Billing()
        {
            var invoices = await _context.Invoices
                .Include(i => i.ServiceRequest)
                    .ThenInclude(r => r!.Customer)
                .Include(i => i.ServiceRequest)
                    .ThenInclude(r => r!.Technician)
                .CustomerFacingInvoices()
                .Where(i => i.ServiceRequest.Status != "Cancelled")
                .OrderByDescending(i => i.DateIssued)
                .ToListAsync();

            // Active legacy exceptions only. Archived records remain preserved without
            // polluting the current billing workflow.
            var billedRequestIds = invoices.Select(i => i.RequestID).ToHashSet();
            var completedUnbilled = await _context.ServiceRequests
                .Include(r => r.Customer)
                .Where(r => r.Status == "Completed"
                    && !r.IsArchived
                    && !r.Customer.User.IsArchived
                    && !billedRequestIds.Contains(r.RequestID))
                .OrderByDescending(r => r.CompletedDate)
                .ToListAsync();

            ViewBag.CompletedUnbilledJobs = completedUnbilled;
            return View(invoices);
        }

        public async Task<IActionResult> Inventory()
        {
            var items = await _context.InventoryItems
                .OrderBy(i => i.ItemName)
                .ToListAsync();
            return View(items);
        }

        public async Task<IActionResult> StockMovements(string? movementType, int? itemId, string? search)
        {
            var query = _context.StockMovements
                .Include(m => m.InventoryItem)
                .Include(m => m.ServiceRequest)
                    .ThenInclude(r => r!.Customer)
                .Include(m => m.ServiceRequest)
                    .ThenInclude(r => r!.Technician)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(movementType) && movementType != "All")
            {
                query = query.Where(m => m.MovementType == movementType);
            }

            if (itemId.HasValue && itemId.Value > 0)
            {
                query = query.Where(m => m.ItemID == itemId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(m =>
                    m.InventoryItem.ItemName.ToLower().Contains(s) ||
                    m.InventoryItem.SKU.ToLower().Contains(s) ||
                    m.PerformedBy.ToLower().Contains(s) ||
                    m.DestinationOrSource.ToLower().Contains(s) ||
                    (m.Notes != null && m.Notes.ToLower().Contains(s)));
            }

            var movements = await query
                .OrderByDescending(m => m.Timestamp)
                .ToListAsync();

            ViewBag.Items = await _context.InventoryItems.OrderBy(i => i.ItemName).ToListAsync();
            ViewBag.SelectedMovementType = movementType ?? "All";
            ViewBag.SelectedItemId = itemId ?? 0;
            ViewBag.Search = search ?? string.Empty;

            var allMovements = await _context.StockMovements.Include(m => m.InventoryItem).ToListAsync();
            ViewBag.TotalUsedInJobs = allMovements.Where(m => m.MovementType == "Job Usage").Sum(m => Math.Abs(m.Quantity));
            ViewBag.TotalRestocked = allMovements.Where(m => m.MovementType == "Restock" || m.MovementType == "Initial Stock").Sum(m => m.Quantity);
            ViewBag.TotalValueConsumed = allMovements.Where(m => m.MovementType == "Job Usage").Sum(m => Math.Abs(m.Quantity) * m.UnitPrice);
            ViewBag.TotalTransactions = allMovements.Count;

            return View(movements);
        }

        public async Task<IActionResult> Quotations()
        {
            var quotations = await _context.Invoices
                .Include(i => i.ServiceRequest)
                    .ThenInclude(r => r!.Customer)
                .Include(i => i.ServiceRequest)
                    .ThenInclude(r => r!.Technician)
                .Where(i => i.IsQuotation && i.ServiceRequest.Status != "Cancelled")
                .OrderByDescending(i => i.DateIssued)
                .ToListAsync();

            ViewBag.InventoryUsages = await _context.JobInventoryUsages
                .Include(u => u.InventoryItem)
                .Where(u => quotations.Select(q => q.RequestID).Contains(u.RequestID))
                .ToListAsync();
            return View(quotations);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveQuotation(int invoiceId, decimal finalAmount, string finalBreakdown)
        {
            var quotation = await _context.Invoices
                .Include(i => i.ServiceRequest)
                .FirstOrDefaultAsync(i => i.InvoiceID == invoiceId
                    && i.IsQuotation
                    && i.QuotationStatus == "PendingAdmin"
                    && i.ServiceRequest.Status != "Cancelled");

            if (quotation == null) return NotFound();
            if (finalAmount <= 0 || string.IsNullOrWhiteSpace(finalBreakdown))
            {
                TempData["ErrorMessage"] = "Enter a valid final amount and customer-facing breakdown.";
                return RedirectToAction(nameof(Quotations));
            }

            var allocations = await _context.JobInventoryUsages
                .Include(u => u.InventoryItem)
                .Where(u => u.RequestID == quotation.RequestID && !u.IsDeducted)
                .ToListAsync();
            var unavailable = allocations.FirstOrDefault(u => u.InventoryItem.StockQuantity < u.Quantity);
            if (unavailable != null)
            {
                TempData["ErrorMessage"] = $"Cannot approve: {unavailable.InventoryItem.ItemName} requires {unavailable.Quantity}, but only {unavailable.InventoryItem.StockQuantity} remain in stock.";
                return RedirectToAction(nameof(Quotations));
            }

            quotation.TotalAmount = finalAmount;
            quotation.BreakdownDetails = finalBreakdown.Trim();
            quotation.QuotationStatus = "ApprovedByAdmin";
            quotation.PaymentStatus = "Unpaid";
            quotation.ServiceRequest.Status = "Pending";

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Quotation QT-{quotation.DateIssued.Year}-{quotation.InvoiceID:D4} was approved and sent to the customer.";
            return RedirectToAction(nameof(Quotations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsPaid(int invoiceId)
        {
            var invoice = await _context.Invoices
                .Include(i => i.ServiceRequest)
                .FirstOrDefaultAsync(i => i.InvoiceID == invoiceId
                    && i.ServiceRequest.Status != "Cancelled");
            if (invoice == null) return NotFound();

            invoice.PaymentStatus = "Paid";
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Invoice #{invoiceId} marked as Paid.";
            return RedirectToAction(nameof(Billing));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddInventoryItem(HomeServeIT.Web.Models.InventoryItem item)
        {
            if (item.StockQuantity < 0 || item.ReorderLevel < 0 || item.UnitCost < 0 || item.UnitPrice < 0)
            {
                TempData["ErrorMessage"] = "Quantities, costs, and reorder levels must be zero or greater.";
                return RedirectToAction(nameof(Inventory));
            }

            _context.InventoryItems.Add(item);
            await _context.SaveChangesAsync();

            var currentUser = await _userManager.GetUserAsync(User);
            var performerName = currentUser?.FullName ?? "Admin User";

            if (item.StockQuantity > 0)
            {
                _context.StockMovements.Add(new StockMovement
                {
                    ItemID = item.ItemID,
                    MovementType = "Initial Stock",
                    Quantity = item.StockQuantity,
                    UnitCost = item.UnitCost,
                    UnitPrice = item.UnitPrice,
                    Timestamp = DateTime.UtcNow,
                    PerformedBy = $"{performerName} (Admin)",
                    DestinationOrSource = "Inventory Registration / Warehouse Shelf",
                    Notes = $"Initial stock intake for newly added SKU {item.SKU}."
                });
                await _context.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = $"Inventory item '{item.ItemName}' added successfully.";
            return RedirectToAction(nameof(Inventory));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditInventoryItem(HomeServeIT.Web.Models.InventoryItem item)
        {
            if (item.StockQuantity < 0 || item.ReorderLevel < 0 || item.UnitCost < 0 || item.UnitPrice < 0)
            {
                TempData["ErrorMessage"] = "Quantities, costs, and reorder levels must be zero or greater.";
                return RedirectToAction(nameof(Inventory));
            }

            var existingItem = await _context.InventoryItems.FindAsync(item.ItemID);
            if (existingItem != null)
            {
                var diff = item.StockQuantity - existingItem.StockQuantity;

                existingItem.ItemName = item.ItemName;
                existingItem.Category = item.Category;
                existingItem.SKU = item.SKU;
                existingItem.StockQuantity = item.StockQuantity;
                existingItem.UnitCost = item.UnitCost;
                existingItem.UnitPrice = item.UnitPrice;
                existingItem.ReorderLevel = item.ReorderLevel;
                existingItem.ImageUrl = item.ImageUrl;

                if (diff != 0)
                {
                    var currentUser = await _userManager.GetUserAsync(User);
                    var performerName = currentUser?.FullName ?? "Admin User";

                    _context.StockMovements.Add(new StockMovement
                    {
                        ItemID = existingItem.ItemID,
                        MovementType = "Manual Adjustment",
                        Quantity = diff,
                        UnitCost = item.UnitCost,
                        UnitPrice = item.UnitPrice,
                        Timestamp = DateTime.UtcNow,
                        PerformedBy = $"{performerName} (Admin)",
                        DestinationOrSource = "Stock Count Audit / Manual Correction",
                        Notes = $"Manual inventory adjustment: {(diff > 0 ? $"+{diff}" : $"{diff}")} units."
                    });
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Inventory item '{item.ItemName}' updated successfully.";
            }
            return RedirectToAction(nameof(Inventory));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteInventoryItem(int itemId)
        {
            var item = await _context.InventoryItems.FindAsync(itemId);
            if (item != null)
            {
                _context.InventoryItems.Remove(item);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Inventory item '{item.ItemName}' deleted successfully.";
            }
            return RedirectToAction(nameof(Inventory));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestockInventory(int itemId, int quantity)
        {
            if (quantity <= 0)
            {
                TempData["ErrorMessage"] = "Restock quantity must be greater than zero.";
                return RedirectToAction(nameof(Inventory));
            }

            var item = await _context.InventoryItems.FindAsync(itemId);
            if (item != null)
            {
                item.StockQuantity += quantity;

                var currentUser = await _userManager.GetUserAsync(User);
                var performerName = currentUser?.FullName ?? "Admin User";

                _context.StockMovements.Add(new StockMovement
                {
                    ItemID = item.ItemID,
                    MovementType = "Restock",
                    Quantity = quantity,
                    UnitCost = item.UnitCost,
                    UnitPrice = item.UnitPrice,
                    Timestamp = DateTime.UtcNow,
                    PerformedBy = $"{performerName} (Admin)",
                    DestinationOrSource = "Supplier Inbound Restock → Central Warehouse",
                    Notes = $"Restocked {quantity} pcs into inventory stock."
                });

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Restocked {quantity} pcs of '{item.ItemName}'.";
            }
            return RedirectToAction(nameof(Inventory));
        }
    }
}
