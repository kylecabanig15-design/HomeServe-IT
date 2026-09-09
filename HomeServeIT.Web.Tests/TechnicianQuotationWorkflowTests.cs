using HomeServeIT.Web.Areas.Technician.Controllers;
using HomeServeIT.Web.Areas.Technician.Models;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServeIT.Web.Tests;

public sealed class TechnicianQuotationWorkflowTests
{
    [Theory]
    [InlineData(null, "Unpaid", false, false)]
    [InlineData(null, "Unpaid", false, false, true)]
    [InlineData("PendingAdmin", "Unpaid", true, false)]
    [InlineData("ApprovedByAdmin", "Unpaid", true, false)]
    [InlineData("Approved", "Paid", true, true)]
    [InlineData("Rejected", "Unpaid", false, false)]
    [InlineData("Approved", "Voided", false, false)]
    public async Task PendingJobsExposeFinancialStateAndOnlyUnquotedJobsCanSubmit(
        string? quotationStatus, string paymentStatus, bool quoted, bool paid, bool serviceOnly = false)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => database.CreateContext());
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var accounts = await TestDataBuilder.SeedRoleAccountsAsync(context);
        var job = await TestDataBuilder.AddRequestAsync(context, accounts.Customer.CustomerID, DateTime.UtcNow, accounts.Technician.TechID);
        var allocation = await TestDataBuilder.AddInventoryAllocationAsync(context, job.RequestID, 10, 1);
        if (quotationStatus != null)
        {
            context.Invoices.Add(new Invoice { RequestID = job.RequestID, IsQuotation = true,
                QuotationStatus = quotationStatus, PaymentStatus = paymentStatus, TotalAmount = 100 });
            await context.SaveChangesAsync();
        }
        var http = new DefaultHttpContext { User = TestDataBuilder.CreatePrincipal(accounts.TechnicianUser, Roles.Technician) };
        var controller = new AssignedJobsController(context, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new MemoryTempDataProvider())
        };
        var view = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<TechnicianAssignedJobsViewModel>(view.Model);
        Assert.Equal(quoted, model.QuotedRequestIds.Contains(job.RequestID));
        Assert.Equal(paid, model.PaidRequestIds.Contains(job.RequestID));
        Assert.Equal(quotationStatus == "PendingAdmin", model.PendingReviewRequestIds.Contains(job.RequestID));
        await controller.ProposeQuotation(job.RequestID, serviceOnly ? [] : [allocation.Item.ItemID], serviceOnly ? [] : [1], 50, "Diagnosis", DateTime.UtcNow.AddDays(1));
        Assert.Equal(quoted ? "Pending" : "PendingAdminApproval", job.Status);
        Assert.Equal(!quoted, controller.TempData.ContainsKey("SuccessMessage"));
        if (!quoted)
        {
            var invoice = await context.Invoices.SingleAsync(i => i.RequestID == job.RequestID && i.QuotationStatus == "PendingAdmin");
            if (serviceOnly)
            {
                Assert.Equal(50, invoice.TotalAmount);
                Assert.False(await context.JobInventoryUsages.AnyAsync(u => u.RequestID == job.RequestID));
            }
            await controller.EditQuotation(invoice.InvoiceID, [], [], 75, "Service only", DateTime.UtcNow.AddDays(1));
            Assert.Equal(75, invoice.TotalAmount);
            Assert.False(await context.JobInventoryUsages.AnyAsync(u => u.RequestID == job.RequestID));
            var count = await context.Invoices.CountAsync();
            await controller.ProposeQuotation(job.RequestID, [allocation.Item.ItemID], [1], 50, null, DateTime.UtcNow.AddDays(1));
            Assert.Equal(count, await context.Invoices.CountAsync());
        }
        var other = await TestDataBuilder.AddRequestAsync(context, accounts.Customer.CustomerID, DateTime.UtcNow.AddDays(1));
        Assert.IsType<NotFoundResult>(await controller.ProposeQuotation(other.RequestID, [allocation.Item.ItemID], [1], 50, null, DateTime.UtcNow.AddDays(2)));
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
