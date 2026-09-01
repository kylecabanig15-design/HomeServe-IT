using HomeServeIT.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Services;

public static class JobInventoryService
{
    public static async Task<string?> DeductForJobStartAsync(ApplicationDbContext context, int requestId)
    {
        var usages = await context.JobInventoryUsages
            .Include(u => u.InventoryItem)
            .Where(u => u.RequestID == requestId && !u.IsDeducted)
            .ToListAsync();

        var insufficient = usages.FirstOrDefault(u => u.InventoryItem.StockQuantity < u.Quantity);
        if (insufficient != null)
            return $"Cannot complete the job: {insufficient.InventoryItem.ItemName} has only {insufficient.InventoryItem.StockQuantity} in stock, but {insufficient.Quantity} is allocated.";

        var serviceRequest = await context.ServiceRequests
            .Include(r => r.Customer)
            .Include(r => r.Technician)
            .FirstOrDefaultAsync(r => r.RequestID == requestId);

        var techName = serviceRequest?.Technician != null
            ? $"{serviceRequest.Technician.FirstName} {serviceRequest.Technician.LastName} (Technician)"
            : "Assigned Technician";

        var customerName = serviceRequest?.Customer != null
            ? $"{serviceRequest.Customer.FirstName} {serviceRequest.Customer.LastName}"
            : "Customer";

        var destination = serviceRequest != null
            ? $"Job #JOB-{serviceRequest.RequestID:D4} · {customerName}{(string.IsNullOrWhiteSpace(serviceRequest.Customer?.HomeAddress) ? "" : $" ({serviceRequest.Customer.HomeAddress})")}"
            : $"Service Request #{requestId}";

        foreach (var usage in usages)
        {
            usage.InventoryItem.StockQuantity -= usage.Quantity;
            usage.IsDeducted = true;

            context.StockMovements.Add(new HomeServeIT.Web.Models.StockMovement
            {
                ItemID = usage.ItemID,
                RequestID = requestId,
                MovementType = "Job Usage",
                Quantity = -usage.Quantity,
                UnitCost = usage.InventoryItem.UnitCost,
                UnitPrice = usage.UnitPrice,
                Timestamp = DateTime.UtcNow,
                PerformedBy = techName,
                DestinationOrSource = destination,
                Notes = serviceRequest != null ? $"Used for {serviceRequest.ServiceCategory}: {serviceRequest.IssueDescription}" : null
            });
        }

        return null;
    }
}
