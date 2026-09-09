using System.Data;
using HomeServeIT.Web.Services;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Tests;

public sealed class JobInventoryServiceTests
{
    [Fact]
    public async Task DeductForJobStart_AppliesStockUsageAndMovementTogether()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        int itemId;
        int usageId;

        await using (var context = database.CreateContext())
        {
            var customer = await TestDataBuilder.AddCustomerAsync(context);
            var request = await TestDataBuilder.AddRequestAsync(context, customer.CustomerID, DateTime.UtcNow.AddDays(1));
            var allocation = await TestDataBuilder.AddInventoryAllocationAsync(context, request.RequestID, 5, 2);
            itemId = allocation.Item.ItemID;
            usageId = allocation.Usage.UsageID;

            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await new JobInventoryService(context)
                .DeductForJobStartAsync(request.RequestID, "Test Technician");
            Assert.Equal(JobInventoryDeductionStatus.Applied, result.Status);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.CreateContext();
        Assert.Equal(3, await verification.InventoryItems.Where(item => item.ItemID == itemId).Select(item => item.StockQuantity).SingleAsync());
        Assert.True(await verification.JobInventoryUsages.Where(usage => usage.UsageID == usageId).Select(usage => usage.IsDeducted).SingleAsync());
        var movement = await verification.StockMovements.SingleAsync();
        Assert.Equal(-2, movement.Quantity);
        Assert.Equal("Test Technician", movement.PerformedBy);
    }

    [Fact]
    public async Task DeductForJobStart_InsufficientStockRollsBackTheClaimAndAudit()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        int itemId;
        int usageId;

        await using (var context = database.CreateContext())
        {
            var customer = await TestDataBuilder.AddCustomerAsync(context);
            var request = await TestDataBuilder.AddRequestAsync(context, customer.CustomerID, DateTime.UtcNow.AddDays(1));
            var allocation = await TestDataBuilder.AddInventoryAllocationAsync(context, request.RequestID, 2, 3);
            itemId = allocation.Item.ItemID;
            usageId = allocation.Usage.UsageID;

            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await new JobInventoryService(context)
                .DeductForJobStartAsync(request.RequestID, "Test Technician");
            Assert.False(result.Succeeded);
            await transaction.RollbackAsync();
        }

        await using var verification = database.CreateContext();
        Assert.Equal(2, await verification.InventoryItems.Where(item => item.ItemID == itemId).Select(item => item.StockQuantity).SingleAsync());
        Assert.False(await verification.JobInventoryUsages.Where(usage => usage.UsageID == usageId).Select(usage => usage.IsDeducted).SingleAsync());
        Assert.Empty(await verification.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task DeductForJobStart_ReplayDoesNotDeductOrAuditTwice()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        int requestId;
        int itemId;

        await using (var setup = database.CreateContext())
        {
            var customer = await TestDataBuilder.AddCustomerAsync(setup);
            var request = await TestDataBuilder.AddRequestAsync(setup, customer.CustomerID, DateTime.UtcNow.AddDays(1));
            var allocation = await TestDataBuilder.AddInventoryAllocationAsync(setup, request.RequestID, 5, 4);
            requestId = request.RequestID;
            itemId = allocation.Item.ItemID;
        }

        Assert.Equal(JobInventoryDeductionStatus.Applied, await ExecuteDeductionAsync(database, requestId));
        Assert.Equal(JobInventoryDeductionStatus.AlreadyDeducted, await ExecuteDeductionAsync(database, requestId));

        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.InventoryItems.Where(item => item.ItemID == itemId).Select(item => item.StockQuantity).SingleAsync());
        Assert.Single(await verification.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task DeductForJobStart_RequiresAnOwningTransaction()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new JobInventoryService(context).DeductForJobStartAsync(1, "Test Technician"));

        Assert.Contains("inside the transaction", exception.Message);
    }

    private static async Task<JobInventoryDeductionStatus> ExecuteDeductionAsync(
        SqliteTestDatabase database,
        int requestId)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var result = await new JobInventoryService(context)
            .DeductForJobStartAsync(requestId, "Test Technician");
        Assert.True(result.Succeeded, result.ErrorMessage);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return result.Status;
    }
}
