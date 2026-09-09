using HomeServeIT.Web.Services;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Tests;

public sealed class TechnicianAssignmentServiceTests
{
    [Fact]
    public async Task Assign_RejectsAnotherActiveJobOnTheSameDay()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var customer = await TestDataBuilder.AddCustomerAsync(context);
        var technician = await TestDataBuilder.AddTechnicianAsync(context);
        var scheduled = new DateTime(2030, 1, 15, 9, 0, 0, DateTimeKind.Utc);
        await TestDataBuilder.AddRequestAsync(context, customer.CustomerID, scheduled, technician.TechID, "In Progress");
        var target = await TestDataBuilder.AddRequestAsync(context, customer.CustomerID, scheduled.AddHours(4));

        var result = await new TechnicianAssignmentService(context).AssignAsync(target.RequestID, technician.TechID);

        Assert.Equal(TechnicianAssignmentStatus.ScheduleConflict, result.Status);
        context.ChangeTracker.Clear();
        Assert.Null(await context.ServiceRequests.Where(request => request.RequestID == target.RequestID).Select(request => request.TechID).SingleAsync());
    }

    [Fact]
    public async Task Assign_IgnoresCompletedCancelledAndArchivedJobs()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var customer = await TestDataBuilder.AddCustomerAsync(context);
        var technician = await TestDataBuilder.AddTechnicianAsync(context);
        var scheduled = new DateTime(2020, 1, 16, 9, 0, 0, DateTimeKind.Utc);
        await TestDataBuilder.AddRequestAsync(context, customer.CustomerID, scheduled, technician.TechID, "Completed");
        await TestDataBuilder.AddRequestAsync(context, customer.CustomerID, scheduled.AddHours(1), technician.TechID, "Cancelled");
        await TestDataBuilder.AddRequestAsync(context, customer.CustomerID, scheduled.AddHours(2), technician.TechID, "Pending", true);
        var target = await TestDataBuilder.AddRequestAsync(context, customer.CustomerID, scheduled.AddHours(3));

        var result = await new TechnicianAssignmentService(context).AssignAsync(target.RequestID, technician.TechID);

        Assert.Equal(TechnicianAssignmentStatus.Assigned, result.Status);
        context.ChangeTracker.Clear();
        Assert.Equal(technician.TechID, await context.ServiceRequests.Where(request => request.RequestID == target.RequestID).Select(request => request.TechID).SingleAsync());
    }

    [Fact]
    public async Task Assign_RejectsUnavailableTechnician()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var customer = await TestDataBuilder.AddCustomerAsync(context);
        var technician = await TestDataBuilder.AddTechnicianAsync(context, isAvailable: false);
        var target = await TestDataBuilder.AddRequestAsync(context, customer.CustomerID, DateTime.UtcNow.AddDays(1));

        var result = await new TechnicianAssignmentService(context).AssignAsync(target.RequestID, technician.TechID);

        Assert.Equal(TechnicianAssignmentStatus.TechnicianUnavailable, result.Status);
    }
}
