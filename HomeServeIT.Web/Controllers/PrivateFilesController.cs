using System.Security.Claims;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Controllers;

[Authorize]
public sealed class PrivateFilesController(ApplicationDbContext context, PrivateUploadService uploads) : Controller
{
    [HttpGet("/private-files/{bucket}/{filename}")]
    public async Task<IActionResult> Download(string bucket, string filename)
    {
        if (!PrivateUploadService.IsValidPath(bucket, filename)) return NotFound();
        var path = $"/private-files/{bucket}/{filename}";
        var legacyPath = $"/uploads/{bucket}/{filename}";
        IQueryable<ServiceRequest> requests = context.ServiceRequests;
        if (bucket == "deliverables")
            requests = context.JobDeliverables.Where(d => d.ImagePath == path || d.ImagePath == legacyPath).Select(d => d.ServiceRequest);
        else
            requests = requests.Where(r => r.ImagePath == path || r.ImagePath == legacyPath);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null || !await context.Users.AnyAsync(u => u.Id == userId && !u.IsArchived)) return NotFound();
        if (!User.IsInRole(Roles.Administrator))
        {
            var customer = User.IsInRole(Roles.Customer);
            var technician = User.IsInRole(Roles.Technician);
            requests = requests.Where(r => !r.IsArchived && r.Status != "Cancelled" && !r.Customer.User.IsArchived
                && ((customer && r.Customer.UserID == userId)
                    || (technician && r.Technician != null && r.Technician.UserID == userId)));
        }
        if (!await requests.AnyAsync()) return NotFound();
        var physicalPath = uploads.Resolve(bucket, filename);
        if (physicalPath == null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        var type = PrivateUploadService.ContentType(filename);
        return type == "application/pdf"
            ? PhysicalFile(physicalPath, type, "deliverable.pdf")
            : PhysicalFile(physicalPath, type);
    }
}
