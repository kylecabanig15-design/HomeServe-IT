using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using System.Security.Claims;
using HomeServeIT.Web.Services;

namespace HomeServeIT.Web.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ApplicationDbContext _context;

    public ChatHub(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SendMessage(string requestId, string message)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return;

        if (string.IsNullOrWhiteSpace(message)) return;
        if (!int.TryParse(requestId, out int reqId)) return;

        if (!await IsAuthorizedForRequest(userId, reqId)) return;
        if (!await IsMemberOfRequestGroup(reqId)) return;

        var sender = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var senderName = sender?.FullName ?? "User";

        // Save to DB
        var msg = new ServiceMessage
        {
            RequestID = reqId,
            SenderID = userId,
            Content = message,
            Timestamp = DateTime.UtcNow,
            IsRead = false
        };

        _context.ServiceMessages.Add(msg);
        await _context.SaveChangesAsync();

        // Broadcast to group
        await Clients.Group(requestId).SendAsync("ReceiveMessage", new
        {
            messageId = msg.MessageID,
            senderId = userId,
            senderName,
            content = message,
            timestamp = msg.Timestamp.ToString("O")
        });
    }

    public async Task MarkAsRead(string requestId, int messageId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return;

        if (!int.TryParse(requestId, out int reqId)) return;

        if (!await IsAuthorizedForRequest(userId, reqId)) return;

        var msg = await _context.ServiceMessages.FindAsync(messageId);
        if (msg != null && msg.RequestID == reqId && msg.SenderID != userId)
        {
            msg.IsRead = true;
            await _context.SaveChangesAsync();
            await Clients.Group(requestId).SendAsync("MessageRead", messageId);
        }
    }

    public async Task JoinRequestGroup(string requestId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return;

        if (!int.TryParse(requestId, out int reqId)) return;

        if (!await IsAuthorizedForRequest(userId, reqId)) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, requestId);
    }

    public async Task LeaveRequestGroup(string requestId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, requestId);
    }

    private async Task<bool> IsAuthorizedForRequest(string userId, int requestId)
    {
        // Admins can join any request
        if (Context.User!.IsInRole(Roles.Administrator))
            return true;

        var isCustomerOwner = await _context.Customers
            .AnyAsync(c => c.UserID == userId
                           && _context.ServiceRequests.Any(r => r.RequestID == requestId && r.CustomerID == c.CustomerID));
        if (isCustomerOwner)
            return true;

        var isAssignedTech = await _context.Technicians
            .Where(t => t.UserID == userId)
            .Select(t => t.TechID)
            .FirstOrDefaultAsync();
        return isAssignedTech > 0
            && await _context.ServiceRequests
                .ActionableTechnicianAssignments(isAssignedTech)
                .AnyAsync(request => request.RequestID == requestId);
    }

    private async Task<bool> IsMemberOfRequestGroup(int requestId)
    {
        var exists = await _context.ServiceRequests.AnyAsync(r => r.RequestID == requestId);
        return exists;
    }
}
