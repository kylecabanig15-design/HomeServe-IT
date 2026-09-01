using HomeServeIT.Web.Models;

namespace HomeServeIT.Web.Services;

public static class ServiceRequestScopes
{
    public static IQueryable<ServiceRequest> VisibleTechnicianAssignments(
        this IQueryable<ServiceRequest> requests,
        int technicianId) =>
        requests.Where(request => request.TechID == technicianId
            && !request.IsArchived
            && request.Status != "Cancelled");

    public static IQueryable<ServiceRequest> ActionableTechnicianAssignments(
        this IQueryable<ServiceRequest> requests,
        int technicianId) =>
        requests.VisibleTechnicianAssignments(technicianId)
            .Where(request => !request.Customer.User.IsArchived);
}
