using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;

namespace HomeServeIT.Web.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = Roles.Customer)]
public class MyDevicesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public MyDevicesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user!.Id);

        if (customer == null)
            return View(new List<Device>());

        var devices = await _context.Devices
            .Where(d => d.CustomerID == customer.CustomerID)
            .OrderByDescending(d => d.DateAdded)
            .ToListAsync();

        return View(devices);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDevice(string deviceName, string deviceType, string? serialNumber)
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user!.Id);

        if (customer == null)
        {
            TempData["ErrorMessage"] = "Customer account not found.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(deviceType))
        {
            TempData["ErrorMessage"] = "Device name and type are required.";
            return RedirectToAction(nameof(Index));
        }

        var device = new Device
        {
            CustomerID = customer.CustomerID,
            Name = deviceName.Trim(),
            Type = deviceType,
            SerialNumber = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim(),
            DateAdded = DateTime.UtcNow
        };

        _context.Devices.Add(device);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Device '{device.Name}' added successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDevice(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user!.Id);
        if (customer == null) return Forbid();

        var device = await _context.Devices.FirstOrDefaultAsync(d => d.DeviceID == id && d.CustomerID == customer.CustomerID);
        if (device == null) return NotFound();

        _context.Devices.Remove(device);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Device '{device.Name}' removed.";
        return RedirectToAction(nameof(Index));
    }
}
