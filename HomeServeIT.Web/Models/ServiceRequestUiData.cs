using System.Text.Json;

namespace HomeServeIT.Web.Models;

public static class ServiceRequestUiData
{
    public static string Serialize(ServiceRequest request) => JsonSerializer.Serialize(new
    {
        id = request.RequestID.ToString(),
        code = $"SR-{request.ScheduledDate.Year}-{request.RequestID:D4}",
        jobCode = $"JOB-{request.ScheduledDate.Year}-{request.RequestID:D4}",
        desc = request.IssueDescription,
        cat = request.ServiceCategory,
        date = request.ScheduledDate.ToString("MMM d, yyyy · h:mm tt"),
        tech = request.Technician == null ? "Unassigned" : $"{request.Technician.FirstName} {request.Technician.LastName}".Trim(),
        techFirstName = request.Technician?.FirstName ?? "",
        techLastName = request.Technician?.LastName ?? "",
        techSpecialty = request.Technician?.Specialty ?? "General service",
        techPhone = request.Technician?.User?.PhoneNumber ?? "",
        customer = $"{request.Customer?.FirstName} {request.Customer?.LastName}".Trim(),
        phone = request.Customer?.PhoneNumber ?? "",
        address = request.Customer?.HomeAddress ?? "",
        prio = request.Priority, status = request.Status, image = request.ImagePath ?? "",
        isCancelReq = request.IsCancellationRequested, cancelStatus = request.CancellationStatus ?? "",
        cancelReason = request.CancellationReason ?? "", cancelRejectReason = request.CancellationRejectReason ?? "",
        isArchived = request.IsArchived, c1 = request.Check1_Diagnostic, c2 = request.Check2_Hardware,
        c3 = request.Check3_Firmware, c4 = request.Check4_QA, c5 = request.Check5_Handover, deliverable = ""
    });
}
