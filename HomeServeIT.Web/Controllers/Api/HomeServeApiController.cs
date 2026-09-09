using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Services;

namespace HomeServeIT.Web.Controllers.Api;

[ApiController]
[Route("api")]
[Authorize(Roles = Roles.Administrator)]
public class HomeServeApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly TechnicianAssignmentService _technicianAssignmentService;

    public HomeServeApiController(
        ApplicationDbContext context,
        TechnicianAssignmentService technicianAssignmentService)
    {
        _context = context;
        _technicianAssignmentService = technicianAssignmentService;
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

        if (!await _context.Customers.AnyAsync(customer => customer.CustomerID == dto.CustomerID))
            return BadRequest(new { Message = "Customer does not exist." });

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var request = new ServiceRequest
        {
            CustomerID = dto.CustomerID,
            IssueDescription = dto.IssueDescription,
            ScheduledDate = dto.ScheduledDate,
            Status = "Pending"
        };
        _context.ServiceRequests.Add(request);
        await _context.SaveChangesAsync();

        if (dto.TechID.HasValue)
        {
            var assignment = await _technicianAssignmentService.AssignAsync(request.RequestID, dto.TechID.Value);
            if (!assignment.Succeeded)
            {
                await transaction.RollbackAsync();
                return Conflict(new {
                    Message = assignment.Message,
                    Algorithm = "Technician Schedule Conflict Detection"
                });
            }
        }

        await transaction.CommitAsync();

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

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var item = await _context.InventoryItems
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ItemID == itemId && !candidate.IsArchived);
        
        if (item == null)
        {
            await transaction.RollbackAsync();
            return NotFound(new { Message = "Inventory item not found." });
        }

        var deducted = await _context.InventoryItems
            .Where(candidate => candidate.ItemID == itemId
                && !candidate.IsArchived
                && candidate.StockQuantity >= dto.QuantityToDeduct)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                candidate => candidate.StockQuantity,
                candidate => candidate.StockQuantity - dto.QuantityToDeduct));

        if (deducted == 0)
        {
            await transaction.RollbackAsync();
            var available = await _context.InventoryItems
                .Where(candidate => candidate.ItemID == itemId)
                .Select(candidate => candidate.StockQuantity)
                .SingleAsync();
            return BadRequest(new { 
                Message = $"Automatic Deduction Failed: Insufficient stock. Available: {available}, Requested: {dto.QuantityToDeduct}",
                Algorithm = "Automatic Inventory Deduction"
            });
        }

        var actor = User.Identity?.Name ?? "Administrator";
        _context.StockMovements.Add(new StockMovement
        {
            ItemID = itemId,
            MovementType = "Manual Adjustment",
            Quantity = -dto.QuantityToDeduct,
            UnitCost = item.UnitCost,
            UnitPrice = item.UnitPrice,
            Timestamp = DateTime.UtcNow,
            PerformedBy = actor.Length <= 100 ? actor : actor[..100],
            DestinationOrSource = "Authenticated inventory API",
            Notes = "Atomic inventory deduction through the administrative API."
        });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var newQuantity = await _context.InventoryItems
            .Where(candidate => candidate.ItemID == itemId)
            .Select(candidate => candidate.StockQuantity)
            .SingleAsync();

        return Ok(new { 
            Message = "Inventory deducted successfully.", 
            NewQuantity = newQuantity,
            Algorithm = "Automatic Inventory Deduction"
        });
    }

    // ALGORITHM 3: Low-Stock Detection Algorithm
    [HttpGet("inventory/low-stock")]
    public async Task<IActionResult> GetLowStockItems()
    {
        // Find items where stock has fallen to or below the reorder threshold
        var lowStockItems = await _context.InventoryItems
            .Where(i => !i.IsArchived && i.StockQuantity <= i.ReorderLevel)
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
