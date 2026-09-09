using System.Security.Claims;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Controllers;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HomeServeIT.Web.Tests;

public sealed class PrivateUploadTests
{
    private sealed class EnvironmentStub : IWebHostEnvironment, IDisposable
    {
        public string ContentRootPath { get; set; } = Directory.CreateTempSubdirectory("homeserve-upload-test-").FullName;
        public string WebRootPath { get; set; } = "";
        public string ApplicationName { get; set; } = "HomeServeIT.Web";
        public string EnvironmentName { get; set; } = "Testing";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public EnvironmentStub() => WebRootPath = Path.Combine(ContentRootPath, "wwwroot");
        public void Dispose() => Directory.Delete(ContentRootPath, recursive: true);
    }
    private static FormFile File(byte[] bytes, string filename, string contentType) => new(new MemoryStream(bytes), 0, bytes.Length, "image", filename)
    { Headers = new HeaderDictionary(), ContentType = contentType };

    [Theory]
    [InlineData("bad.png", "image/png", "not an image")]
    [InlineData("bad.pdf", "application/pdf", "%PDF-1.7 malformed %%EOF")]
    [InlineData("bad.svg", "image/svg+xml", "<svg onload='alert(1)'/>")]
    [InlineData("bad.png", "image/jpeg", "not an image")]
    public async Task RejectsMalformedOrMismatchedFiles(string filename, string mime, string data)
    {
        using var environment = new EnvironmentStub();
        var service = new PrivateUploadService(environment);
        await Assert.ThrowsAsync<UploadValidationException>(() =>
            service.StoreAsync(File(System.Text.Encoding.UTF8.GetBytes(data), filename, mime), "deliverables"));
        Assert.Empty(Directory.GetFiles(environment.ContentRootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ValidPdfIsAcceptedOnlyForDeliverables()
    {
        using var environment = new EnvironmentStub();
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        builder.AddPage(200, 200);
        var bytes = builder.Build();
        var service = new PrivateUploadService(environment);
        await using var upload = await service.StoreAsync(File(bytes, "proof.pdf", "application/pdf"), "deliverables");
        Assert.EndsWith(".pdf", upload!.Url);
        Assert.NotNull(service.Resolve("deliverables", Path.GetFileName(upload.Url)));
        await Assert.ThrowsAsync<UploadValidationException>(() => service.StoreAsync(File(bytes, "proof.pdf", "application/pdf"), "service-requests"));
        await Assert.ThrowsAsync<UploadValidationException>(() => service.StoreAsync(File(bytes, "proof.png", "image/png"), "deliverables"));
    }

    [Fact]
    public async Task RejectsOversizedFile()
    {
        using var environment = new EnvironmentStub();
        await Assert.ThrowsAsync<UploadValidationException>(() => new PrivateUploadService(environment)
            .StoreAsync(File(new byte[PrivateUploadService.MaxFileBytes + 1], "huge.png", "image/png"), "service-requests"));
    }

    [Fact]
    public async Task ValidImageIsPrivateReencodedAndRemovedIfDatabaseWriteDoesNotComplete()
    {
        using var environment = new EnvironmentStub();
        using var image = new Image<Rgba32>(2, 2);
        using var memory = new MemoryStream();
        await image.SaveAsPngAsync(memory);
        var service = new PrivateUploadService(environment);
        string url;
        await using (var upload = await service.StoreAsync(File(memory.ToArray(), "../../original.png", "image/png"), "service-requests"))
        {
            url = upload!.Url;
            Assert.StartsWith("/private-files/service-requests/", url);
            Assert.DoesNotContain("original", url);
            Assert.NotNull(service.Resolve("service-requests", Path.GetFileName(url)));
            Assert.False(Directory.Exists(environment.WebRootPath));
        }
        Assert.Null(service.Resolve("service-requests", Path.GetFileName(url)));
    }

    [Theory]
    [InlineData(Roles.Customer, true, true)]
    [InlineData(Roles.Customer, false, false)]
    [InlineData(Roles.Technician, true, true)]
    [InlineData(Roles.Technician, false, false)]
    [InlineData(Roles.Administrator, false, true)]
    public async Task DownloadChecksRoleAndOwnership(string role, bool owner, bool allowed)
    {
        using var environment = new EnvironmentStub();
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await TestDataBuilder.SeedRoleAccountsAsync(context);
        var request = await TestDataBuilder.AddRequestAsync(context, fixture.Customer.CustomerID, DateTime.UtcNow, fixture.Technician.TechID);
        using var image = new Image<Rgba32>(1, 1);
        using var memory = new MemoryStream();
        await image.SaveAsPngAsync(memory);
        var service = new PrivateUploadService(environment);
        await using var upload = await service.StoreAsync(File(memory.ToArray(), "image.png", "image/png"), "service-requests");
        request.ImagePath = upload!.Url;
        await context.SaveChangesAsync();
        var principalUser = role == Roles.Administrator ? fixture.Administrator
            : owner ? (role == Roles.Customer ? fixture.CustomerUser : fixture.TechnicianUser) : fixture.Administrator;
        var controller = new PrivateFilesController(context, service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
                { User = TestDataBuilder.CreatePrincipal(principalUser, role) } }
        };
        var result = await controller.Download("service-requests", Path.GetFileName(upload.Url));
        if (allowed) Assert.IsType<PhysicalFileResult>(result);
        else Assert.IsType<NotFoundResult>(result);
        Assert.IsType<NotFoundResult>(await controller.Download("service-requests", "../appsettings.json"));
    }
}
