namespace HomeServeIT.Web.Areas.Admin.Models;

public sealed class TechnicianDirectoryViewModel
{
    public IReadOnlyList<HomeServeIT.Web.Models.Technician> ActiveTechnicians { get; init; } = [];
    public IReadOnlyList<HomeServeIT.Web.Models.Technician> SuspendedTechnicians { get; init; } = [];
}
