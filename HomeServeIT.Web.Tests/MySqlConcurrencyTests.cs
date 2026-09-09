using System.Data;
using HomeServeIT.Web.Services;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Data;

namespace HomeServeIT.Web.Tests;

[Collection(MySqlTestDatabaseCollection.CollectionName)]
public sealed class MySqlConcurrencyTests(MySqlTestDatabase database)
{
    [MySqlFact]
    public async Task ConcurrentNotificationWorkersCreateOnlyOneSourceRecord()
    {
        string userId;
        await using (var setup = database.CreateContext())
        {
            var fixture = await TestDataBuilder.SeedRoleAccountsAsync(setup);
            userId = fixture.CustomerUser.Id;
            await TestDataBuilder.AddRequestAsync(setup, fixture.Customer.CustomerID, DateTime.UtcNow);
        }
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => database.CreateContext());
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<NotificationService>();
        await using var provider = services.BuildServiceProvider();
        async Task Sync()
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<NotificationService>().SynchronizeUserAsync(userId);
        }
        await Task.WhenAll(Sync(), Sync());
        await using var verify = database.CreateContext();
        Assert.Single(await verify.UserNotifications.Where(n => n.RecipientUserID == userId).ToListAsync());
    }

    [MySqlFact]
    public async Task ConcurrentInventoryStarts_DeductAllocationOnlyOnce()
    {
        int requestId;
        int itemId;
        await using (var setup = database.CreateContext())
        {
            var customer = await TestDataBuilder.AddCustomerAsync(setup);
            var request = await TestDataBuilder.AddRequestAsync(setup, customer.CustomerID, DateTime.UtcNow.AddDays(2));
            var allocation = await TestDataBuilder.AddInventoryAllocationAsync(setup, request.RequestID, 5, 4);
            requestId = request.RequestID;
            itemId = allocation.Item.ItemID;
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new[]
        {
            DeductConcurrentlyAsync(database, requestId, start.Task),
            DeductConcurrentlyAsync(database, requestId, start.Task)
        };
        start.SetResult();
        var results = await Task.WhenAll(attempts);

        Assert.All(results, result => Assert.True(result.Succeeded, result.ErrorMessage));
        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.InventoryItems.Where(item => item.ItemID == itemId).Select(item => item.StockQuantity).SingleAsync());
        Assert.Single(await verification.StockMovements.Where(movement => movement.RequestID == requestId).ToListAsync());
    }

    [MySqlFact]
    public async Task ConcurrentAssignments_AllowOnlyOneActiveJobPerTechnicianPerDay()
    {
        int technicianId;
        int firstRequestId;
        int secondRequestId;
        await using (var setup = database.CreateContext())
        {
            var customer = await TestDataBuilder.AddCustomerAsync(setup);
            var technician = await TestDataBuilder.AddTechnicianAsync(setup);
            var scheduled = new DateTime(2030, 2, 1, 9, 0, 0, DateTimeKind.Utc);
            var first = await TestDataBuilder.AddRequestAsync(setup, customer.CustomerID, scheduled);
            var second = await TestDataBuilder.AddRequestAsync(setup, customer.CustomerID, scheduled.AddHours(4));
            technicianId = technician.TechID;
            firstRequestId = first.RequestID;
            secondRequestId = second.RequestID;
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new[]
        {
            AssignConcurrentlyAsync(database, firstRequestId, technicianId, start.Task),
            AssignConcurrentlyAsync(database, secondRequestId, technicianId, start.Task)
        };
        start.SetResult();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, result => result.Status == TechnicianAssignmentStatus.Assigned);
        Assert.Single(results, result => result.Status is TechnicianAssignmentStatus.ScheduleConflict or TechnicianAssignmentStatus.ConcurrencyConflict);
        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.ServiceRequests.CountAsync(request =>
            (request.RequestID == firstRequestId || request.RequestID == secondRequestId)
            && request.TechID == technicianId));
    }

    private static async Task<JobInventoryDeductionResult> DeductConcurrentlyAsync(
        MySqlTestDatabase database,
        int requestId,
        Task start)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await start;
        var result = await new JobInventoryService(context).DeductForJobStartAsync(requestId, "Concurrent Test");
        if (result.Succeeded)
        {
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }
        return result;
    }

    private static async Task<TechnicianAssignmentResult> AssignConcurrentlyAsync(
        MySqlTestDatabase database,
        int requestId,
        int technicianId,
        Task start)
    {
        await start;
        await using var context = database.CreateContext();
        return await new TechnicianAssignmentService(context).AssignAsync(requestId, technicianId);
    }
}
