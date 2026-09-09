using HomeServeIT.Web.Areas.Technician.Controllers;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HomeServeIT.Web.Tests;

public sealed class TechnicianHandoverTests
{
    [Theory]
    [InlineData("In Progress", true, true, true, true)]
    [InlineData("In Progress", false, true, true, false)]
    [InlineData("In Progress", true, false, true, false)]
    [InlineData("In Progress", true, true, false, false)]
    [InlineData("Pending", true, true, true, false)]
    [InlineData("Completed", true, true, true, false)]
    public async Task ProofRequiresRepairChecklistPaymentAndFileThenCustomerAcceptance(
        string status, bool checklist, bool paid, bool attach, bool succeeds)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => database.CreateContext());
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var accounts = await TestDataBuilder.SeedRoleAccountsAsync(db);
        var scheduledDate = status == "Completed" ? DateTime.UtcNow.AddDays(-1) : DateTime.UtcNow.AddDays(1);
        var job = await TestDataBuilder.AddRequestAsync(db, accounts.Customer.CustomerID, scheduledDate, accounts.Technician.TechID, status);
        job.Check1_Diagnostic = job.Check2_Hardware = job.Check3_Firmware = job.Check4_QA = job.Check5_Handover = checklist;
        db.Invoices.Add(new Invoice { RequestID = job.RequestID, PaymentStatus = paid ? "Paid" : "Unpaid" });
        await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var http = new DefaultHttpContext { User = TestDataBuilder.CreatePrincipal(accounts.TechnicianUser, Roles.Technician) };
        using var env = new EnvironmentStub();
        var controller = new AssignedJobsController(db, users, env, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new MemoryTempDataProvider())
        };
        // Neither status endpoint may bypass proof submission or customer acceptance.
        await controller.UpdateStatus(job.RequestID, "Completed");
        Assert.Equal(status, job.Status);
        await controller.UpdateStatus(job.RequestID, "PendingCustomerReview");
        Assert.Equal(status, job.Status);
        using var bytes = new MemoryStream();
        using (var picture = new Image<Rgba32>(2, 2)) await picture.SaveAsPngAsync(bytes);
        bytes.Position = 0;
        var file = new FormFile(bytes, 0, bytes.Length, "imageFile", "proof.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        var uploads = new PrivateUploadService(env);
        await controller.UploadDeliverable(job.RequestID, "FinalProof", "Repair tested", attach ? file : null, uploads);
        Assert.Equal(succeeds ? "PendingCustomerReview" : status, job.Status);
        Assert.Equal(succeeds ? 1 : 0, await db.JobDeliverables.CountAsync());
        if (!succeeds) return;
        Assert.Null(job.CompletedDate);
        Assert.IsType<BadRequestObjectResult>(await controller.SaveChecklistProgress(job.RequestID, false, false, false, false, false));
        await controller.UploadDeliverable(job.RequestID, "FinalProof", "Repeated submission", file, uploads);
        Assert.Equal(1, await db.JobDeliverables.CountAsync());
        var otherJob = await TestDataBuilder.AddRequestAsync(db, accounts.Customer.CustomerID, DateTime.UtcNow, null, "In Progress");
        Assert.IsType<NotFoundResult>(await controller.UploadDeliverable(otherJob.RequestID, "FinalProof", "Unauthorized", file, uploads));
        var customerHttp = new DefaultHttpContext { User = TestDataBuilder.CreatePrincipal(accounts.CustomerUser, Roles.Customer) };
        var customer = new HomeServeIT.Web.Areas.Customer.Controllers.ServiceRequestsController(db, users, env, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = customerHttp },
            TempData = new TempDataDictionary(customerHttp, new MemoryTempDataProvider())
        };
        await customer.ReviewDeliverables(job.RequestID, "Approve", null);
        Assert.Equal("Completed", job.Status);
        Assert.NotNull(job.CompletedDate);
        var completion = job.CompletedDate;
        await customer.ReviewDeliverables(job.RequestID, "Approve", null);
        Assert.Equal(completion, job.CompletedDate);
        await controller.UpdateStatus(job.RequestID, "In Progress");
        Assert.Equal("Completed", job.Status);
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class EnvironmentStub : IWebHostEnvironment, IDisposable
    {
        public string ContentRootPath { get; set; } = Directory.CreateTempSubdirectory("homeserve-handover-").FullName;
        public string WebRootPath { get; set; } = "";
        public string ApplicationName { get; set; } = "HomeServeIT.Web";
        public string EnvironmentName { get; set; } = "Testing";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public void Dispose() => Directory.Delete(ContentRootPath, true);
    }
}
