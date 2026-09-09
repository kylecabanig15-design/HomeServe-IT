using System.Data;
using HomeServeIT.Web.Data;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace HomeServeIT.Web.Services;

public enum TechnicianAssignmentStatus
{
    Assigned,
    AlreadyAssigned,
    RequestNotFound,
    TechnicianNotFound,
    TechnicianUnavailable,
    ScheduleConflict,
    ConcurrencyConflict
}

public sealed record TechnicianAssignmentResult(
    TechnicianAssignmentStatus Status,
    string Message)
{
    public bool Succeeded => Status is TechnicianAssignmentStatus.Assigned
        or TechnicianAssignmentStatus.AlreadyAssigned;
}

public sealed class TechnicianAssignmentService(ApplicationDbContext context)
{
    private const int MaxAttempts = 3;

    public async Task<TechnicianAssignmentResult> AssignAsync(
        int requestId,
        int technicianId,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction != null)
            return await AssignWithinCurrentTransactionAsync(requestId, technicianId, cancellationToken);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await AssignOnceAsync(requestId, technicianId, cancellationToken);
            }
            catch (Exception exception) when (IsTransientWriteConflict(exception) && attempt < MaxAttempts)
            {
                context.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
            catch (Exception exception) when (IsTransientWriteConflict(exception))
            {
                return new TechnicianAssignmentResult(
                    TechnicianAssignmentStatus.ConcurrencyConflict,
                    "The technician schedule changed concurrently. Please retry the assignment.");
            }
        }

        return new TechnicianAssignmentResult(
            TechnicianAssignmentStatus.ConcurrencyConflict,
            "The technician schedule changed concurrently. Please retry the assignment.");
    }

    private async Task<TechnicianAssignmentResult> AssignOnceAsync(
        int requestId,
        int technicianId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var result = await AssignWithinCurrentTransactionAsync(requestId, technicianId, cancellationToken);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return result;
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<TechnicianAssignmentResult> AssignWithinCurrentTransactionAsync(
        int requestId,
        int technicianId,
        CancellationToken cancellationToken)
    {
        var request = await context.ServiceRequests
            .SingleOrDefaultAsync(candidate => candidate.RequestID == requestId, cancellationToken);
        if (request == null)
        {
            return new TechnicianAssignmentResult(
                TechnicianAssignmentStatus.RequestNotFound,
                $"Service Request #{requestId} does not exist.");
        }

        var technician = await context.Technicians
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(candidate => candidate.TechID == technicianId, cancellationToken);
        if (technician == null || technician.User.IsArchived)
        {
            return new TechnicianAssignmentResult(
                TechnicianAssignmentStatus.TechnicianNotFound,
                $"Technician #{technicianId} does not exist or is archived.");
        }

        if (!technician.IsAvailable || technician.User.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return new TechnicianAssignmentResult(
                TechnicianAssignmentStatus.TechnicianUnavailable,
                $"Technician #{technicianId} is not available for assignment.");
        }

        if (request.TechID == technicianId)
        {
            return new TechnicianAssignmentResult(
                TechnicianAssignmentStatus.AlreadyAssigned,
                $"Technician #{technicianId} is already assigned to Service Request #{requestId}.");
        }

        var dayStart = request.ScheduledDate.Date;
        var dayEnd = dayStart.AddDays(1);
        var conflict = await context.ServiceRequests
            .AsNoTracking()
            .AnyAsync(candidate => candidate.TechID == technicianId
                && candidate.RequestID != requestId
                && !candidate.IsArchived
                && candidate.Status != "Completed"
                && candidate.Status != "Cancelled"
                && candidate.ScheduledDate >= dayStart
                && candidate.ScheduledDate < dayEnd,
                cancellationToken);

        if (conflict)
        {
            return new TechnicianAssignmentResult(
                TechnicianAssignmentStatus.ScheduleConflict,
                $"Technician #{technicianId} is already assigned to another active job on {dayStart:MMM d, yyyy}.");
        }

        request.TechID = technicianId;
        await context.SaveChangesAsync(cancellationToken);

        return new TechnicianAssignmentResult(
            TechnicianAssignmentStatus.Assigned,
            $"Technician assigned to Service Request #{requestId}.");
    }

    private static bool IsTransientWriteConflict(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException!)
        {
            if (current is MySqlException { Number: 1205 or 1213 })
                return true;

            if (current.InnerException == null)
                break;
        }

        return false;
    }
}
