using HomeServeIT.Web.Models;

namespace HomeServeIT.Web.Areas.Admin.Models;

public class AdminArchiveViewModel
{
    public IReadOnlyList<ServiceRequest> ServiceRequests { get; init; } = [];
    public IReadOnlyList<ApplicationUser> Users { get; init; } = [];
}
