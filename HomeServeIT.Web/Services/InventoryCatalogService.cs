using System.Data;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Services;

public enum InventoryArchiveStatus
{
    Archived,
    NotFound
}

public sealed record InventoryArchiveResult(
    InventoryArchiveStatus Status,
    string? ItemName = null);

public sealed class InventoryCatalogService(ApplicationDbContext context)
{
    public async Task<InventoryArchiveResult> ArchiveAsync(
        int itemId,
        string performedBy,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var item = await context.InventoryItems
            .SingleOrDefaultAsync(
                candidate => candidate.ItemID == itemId && !candidate.IsArchived,
                cancellationToken);

        if (item == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new InventoryArchiveResult(InventoryArchiveStatus.NotFound);
        }

        item.IsArchived = true;
        context.StockMovements.Add(new StockMovement
        {
            ItemID = item.ItemID,
            MovementType = "Archived",
            Quantity = 0,
            UnitCost = item.UnitCost,
            UnitPrice = item.UnitPrice,
            Timestamp = DateTime.UtcNow,
            PerformedBy = Truncate(
                string.IsNullOrWhiteSpace(performedBy) ? "Administrator" : performedBy,
                100),
            DestinationOrSource = "Active inventory catalogue",
            Notes = "Material removed from active inventory; job and stock history retained."
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new InventoryArchiveResult(InventoryArchiveStatus.Archived, item.ItemName);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
