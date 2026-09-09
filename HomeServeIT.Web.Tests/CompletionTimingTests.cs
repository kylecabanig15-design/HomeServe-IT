using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;
using HomeServeIT.Web.Tests.Infrastructure;

namespace HomeServeIT.Web.Tests;

public sealed class CompletionTimingTests
{
    [Fact]
    public void ResolutionTreatsEarlyCompletionAsZeroElapsedDays()
    {
        var start = DateTime.UtcNow.AddDays(-5);
        ServiceRequest Job(double days) => new() { Status = "Completed", ScheduledDate = start, CompletedDate = start.AddDays(days) };
        Assert.Equal(0, CompletionTiming.ResolutionDays(Job(-1)));
        Assert.Null(CompletionTiming.ResolutionDays(Job(10)));
        Assert.Equal(0, CompletionTiming.ResolutionDays(Job(0)));
        Assert.Equal(2d / 3d, CompletionTiming.AverageDays([Job(-1), Job(0), Job(2)]), 10);
    }

    [Fact]
    public async Task SavingEarlyCompletionIsAllowedButFutureCompletionIsRejected()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var customer = await TestDataBuilder.AddCustomerAsync(context);
        var job = await TestDataBuilder.AddRequestAsync(context, customer.CustomerID, DateTime.UtcNow.AddDays(1));
        job.Status = "Completed";
        job.CompletedDate = DateTime.UtcNow;
        await context.SaveChangesAsync();
        await using var verify = database.CreateContext();
        var saved = (await verify.ServiceRequests.FindAsync(job.RequestID))!;
        Assert.Equal("Completed", saved.Status);
        saved.CompletedDate = DateTime.UtcNow.AddMinutes(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => verify.SaveChangesAsync());
    }
}
