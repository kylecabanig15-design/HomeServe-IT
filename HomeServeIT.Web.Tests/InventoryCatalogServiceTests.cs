using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Tests;

public sealed class InventoryCatalogServiceTests
{
    [Fact]
    public async Task ArchiveRetainsReferencedMaterialAndCreatesAuditMovement()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        int itemId;
        int usageId;

        await using (var context = database.CreateContext())
        {
            var customer = await TestDataBuilder.AddCustomerAsync(context);
            var request = await TestDataBuilder.AddRequestAsync(
                context,
                customer.CustomerID,
                DateTime.UtcNow.AddDays(1));
            var allocation = await TestDataBuilder.AddInventoryAllocationAsync(
                context,
                request.RequestID,
                stockQuantity: 4,
                allocatedQuantity: 2);
            var item = allocation.Item;
            item.ItemName = "Referenced SSD";
            itemId = item.ItemID;
            usageId = allocation.Usage.UsageID;

            context.StockMovements.Add(new StockMovement
            {
                ItemID = itemId,
                MovementType = "Initial Stock",
                Quantity = 4,
                UnitCost = item.UnitCost,
                UnitPrice = item.UnitPrice,
                PerformedBy = "Test Admin",
                DestinationOrSource = "Test fixture"
            });
            await context.SaveChangesAsync();

            var result = await new InventoryCatalogService(context)
                .ArchiveAsync(itemId, "Test Admin (Admin)");

            Assert.Equal(InventoryArchiveStatus.Archived, result.Status);
            Assert.Equal("Referenced SSD", result.ItemName);
        }

        await using var verification = database.CreateContext();
        var retainedItem = await verification.InventoryItems.SingleAsync(item => item.ItemID == itemId);
        Assert.True(retainedItem.IsArchived);
        Assert.True(await verification.JobInventoryUsages.AnyAsync(usage => usage.UsageID == usageId));
        Assert.Equal(2, await verification.StockMovements.CountAsync(movement => movement.ItemID == itemId));

        var archiveAudit = await verification.StockMovements
            .SingleAsync(movement => movement.ItemID == itemId && movement.MovementType == "Archived");
        Assert.Equal(0, archiveAudit.Quantity);
        Assert.Equal("Test Admin (Admin)", archiveAudit.PerformedBy);
    }

    [Fact]
    public async Task ArchiveRejectsAnAlreadyArchivedMaterialWithoutAnotherAudit()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var item = new InventoryItem { ItemName = "Old cable", IsArchived = true };
        context.InventoryItems.Add(item);
        await context.SaveChangesAsync();

        var result = await new InventoryCatalogService(context)
            .ArchiveAsync(item.ItemID, "Test Admin");

        Assert.Equal(InventoryArchiveStatus.NotFound, result.Status);
        Assert.Empty(await context.StockMovements.ToListAsync());
    }
}
