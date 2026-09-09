using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Services;

public enum JobInventoryDeductionStatus
{
    Applied,
    NoInventoryAllocated,
    AlreadyDeducted,
    Failed
}

public sealed record JobInventoryDeductionResult(
    JobInventoryDeductionStatus Status,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status != JobInventoryDeductionStatus.Failed;
}

public sealed class JobInventoryService(ApplicationDbContext context)
{
    public async Task<JobInventoryDeductionResult> DeductForJobStartAsync(
        int requestId,
        string performedBy,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "Inventory deduction must run inside the transaction that changes the job state.");
        }

        var job = await context.ServiceRequests
            .AsNoTracking()
            .Where(request => request.RequestID == requestId)
            .Select(request => new
            {
                request.RequestID,
                request.ServiceCategory,
                request.IssueDescription,
                CustomerName = request.Customer.FirstName + " " + request.Customer.LastName,
                request.Customer.HomeAddress
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (job == null)
        {
            return new JobInventoryDeductionResult(
                JobInventoryDeductionStatus.Failed,
                $"Service Request #{requestId} does not exist.");
        }

        var usages = await context.JobInventoryUsages
            .AsNoTracking()
            .Where(usage => usage.RequestID == requestId && !usage.IsDeducted)
            .OrderBy(usage => usage.UsageID)
            .Select(usage => new
            {
                usage.UsageID,
                usage.ItemID,
                usage.Quantity,
                usage.UnitPrice,
                usage.InventoryItem.ItemName,
                usage.InventoryItem.UnitCost
            })
            .ToListAsync(cancellationToken);

        if (usages.Count == 0)
        {
            var hasAllocatedInventory = await context.JobInventoryUsages
                .AsNoTracking()
                .AnyAsync(usage => usage.RequestID == requestId, cancellationToken);

            return new JobInventoryDeductionResult(
                hasAllocatedInventory
                    ? JobInventoryDeductionStatus.AlreadyDeducted
                    : JobInventoryDeductionStatus.NoInventoryAllocated);
        }

        var destination = $"Job #JOB-{job.RequestID:D4} · {job.CustomerName.Trim()}";
        if (!string.IsNullOrWhiteSpace(job.HomeAddress))
            destination += $" ({job.HomeAddress})";

        var appliedCount = 0;
        foreach (var usage in usages)
        {
            if (usage.Quantity <= 0)
            {
                return new JobInventoryDeductionResult(
                    JobInventoryDeductionStatus.Failed,
                    $"{usage.ItemName} has an invalid allocated quantity of {usage.Quantity}.");
            }

            // Claim the usage first. Concurrent retries can no longer deduct the same
            // allocation twice; a failed transaction rolls the claim back with the stock change.
            var claimed = await context.JobInventoryUsages
                .Where(candidate => candidate.UsageID == usage.UsageID && !candidate.IsDeducted)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(candidate => candidate.IsDeducted, true),
                    cancellationToken);

            if (claimed == 0)
            {
                return appliedCount == 0
                    ? new JobInventoryDeductionResult(JobInventoryDeductionStatus.AlreadyDeducted)
                    : new JobInventoryDeductionResult(
                        JobInventoryDeductionStatus.Failed,
                        "Inventory allocation changed concurrently. Please retry the job start.");
            }

            // The stock predicate and decrement execute as one SQL statement. A stale
            // application-side stock value therefore cannot make inventory negative.
            var deducted = await context.InventoryItems
                .Where(item => item.ItemID == usage.ItemID && item.StockQuantity >= usage.Quantity)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.StockQuantity,
                        item => item.StockQuantity - usage.Quantity),
                    cancellationToken);

            if (deducted == 0)
            {
                var available = await context.InventoryItems
                    .AsNoTracking()
                    .Where(item => item.ItemID == usage.ItemID)
                    .Select(item => (int?)item.StockQuantity)
                    .SingleOrDefaultAsync(cancellationToken);

                var availability = available.HasValue
                    ? $"has only {available.Value} in stock"
                    : "is no longer available";

                return new JobInventoryDeductionResult(
                    JobInventoryDeductionStatus.Failed,
                    $"Cannot start the job: {usage.ItemName} {availability}, but {usage.Quantity} is allocated.");
            }

            var notes = $"Used for {job.ServiceCategory}: {job.IssueDescription}";
            context.StockMovements.Add(new StockMovement
            {
                ItemID = usage.ItemID,
                RequestID = requestId,
                MovementType = "Job Usage",
                Quantity = -usage.Quantity,
                UnitCost = usage.UnitCost,
                UnitPrice = usage.UnitPrice,
                Timestamp = DateTime.UtcNow,
                PerformedBy = Truncate(string.IsNullOrWhiteSpace(performedBy) ? "System" : performedBy, 100),
                DestinationOrSource = Truncate(destination, 255),
                Notes = Truncate(notes, 500)
            });
            appliedCount++;
        }

        return new JobInventoryDeductionResult(JobInventoryDeductionStatus.Applied);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
