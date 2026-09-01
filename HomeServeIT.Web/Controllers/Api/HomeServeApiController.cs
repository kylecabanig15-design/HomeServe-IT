using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;

namespace HomeServeIT.Web.Controllers.Api;

[ApiController]
[Route("api")]
[Authorize]
public class HomeServeApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public HomeServeApiController(ApplicationDbContext context)
    {
        _context = context;
    }

    // ALGORITHM 1: Technician Schedule Conflict Detection Algorithm
    [HttpPost("service-requests")]
    public async Task<IActionResult> CreateServiceRequest([FromBody] ServiceRequestRequestDto? dto)
    {
        if (dto == null)
            return BadRequest(new { Message = "Request body is required." });

        if (string.IsNullOrWhiteSpace(dto.IssueDescription))
            return BadRequest(new { Message = "IssueDescription is required." });

        if (dto.CustomerID <= 0)
            return BadRequest(new { Message = "A valid CustomerID is required." });

        if (dto.TechID.HasValue)
        {
            // Detect if the technician is already booked for this specific date/time
            bool hasConflict = await _context.ServiceRequests
                .AnyAsync(r => r.TechID == dto.TechID && 
                               r.ScheduledDate.Date == dto.ScheduledDate.Date);

            if (hasConflict)
            {
                return Conflict(new { 
                    Message = "Schedule Conflict Detected: The selected technician is already assigned to a job on this date.",
                    Algorithm = "Technician Schedule Conflict Detection"
                });
            }
        }

        var request = new ServiceRequest
        {
            CustomerID = dto.CustomerID,
            TechID = dto.TechID,
            IssueDescription = dto.IssueDescription,
            ScheduledDate = dto.ScheduledDate,
            Status = "Pending"
        };

        _context.ServiceRequests.Add(request);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(CreateServiceRequest), new { id = request.RequestID }, request);
    }

    // ALGORITHM 2: Automatic Inventory Deduction Algorithm
    [HttpPost("inventory/deduct/{itemId}")]
    public async Task<IActionResult> DeductInventory(int itemId, [FromBody] DeductInventoryDto? dto)
    {
        if (dto == null)
            return BadRequest(new { Message = "Request body is required." });

        if (dto.QuantityToDeduct <= 0)
            return BadRequest(new { Message = "QuantityToDeduct must be greater than zero." });

        var item = await _context.InventoryItems.FindAsync(itemId);
        
        if (item == null)
            return NotFound(new { Message = "Inventory item not found." });

        if (item.StockQuantity < dto.QuantityToDeduct)
        {
            return BadRequest(new { 
                Message = $"Automatic Deduction Failed: Insufficient stock. Available: {item.StockQuantity}, Requested: {dto.QuantityToDeduct}",
                Algorithm = "Automatic Inventory Deduction"
            });
        }

        // Safely deduct
        item.StockQuantity -= dto.QuantityToDeduct;
        await _context.SaveChangesAsync();

        return Ok(new { 
            Message = "Inventory deducted successfully.", 
            NewQuantity = item.StockQuantity,
            Algorithm = "Automatic Inventory Deduction"
        });
    }

    // ALGORITHM 3: Low-Stock Detection Algorithm
    [HttpGet("inventory/low-stock")]
    public async Task<IActionResult> GetLowStockItems()
    {
        // Find items where stock has fallen to or below the reorder threshold
        var lowStockItems = await _context.InventoryItems
            .Where(i => i.StockQuantity <= i.ReorderLevel)
            .Select(i => new {
                i.ItemID,
                i.ItemName,
                i.StockQuantity,
                i.ReorderLevel,
                Deficit = i.ReorderLevel - i.StockQuantity
            })
            .ToListAsync();

        return Ok(new {
            Count = lowStockItems.Count,
            Alerts = lowStockItems,
            Algorithm = "Low-Stock Detection"
        });
    }
}

// DTOs for API requests
public class ServiceRequestRequestDto
{
    public int CustomerID { get; set; }
    public int? TechID { get; set; }
    public string IssueDescription { get; set; } = string.Empty;
    public DateTime ScheduledDate { get; set; }
}

public class DeductInventoryDto
{
    public int QuantityToDeduct { get; set; }
}
