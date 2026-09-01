using HomeServeIT.Web.Models;

namespace HomeServeIT.Web.Areas.Technician.Models
{
    public class TechnicianDashboardViewModel
    {
        public int TotalAssigned { get; set; }
        public int TotalPending { get; set; }
        public int TotalCompleted { get; set; }
        public List<ServiceRequest> TodaysJobs { get; set; } = new();
        public List<ServiceRequest> UpcomingJobs { get; set; } = new();
    }

    public class TechnicianAssignedJobsViewModel
    {
        public List<ServiceRequest> ActiveJobs { get; set; } = new();
        public List<ServiceRequest> CompletedJobs { get; set; } = new();
    }

    public class TechnicianScheduleViewModel
    {
        public List<ServiceRequest> ScheduledRequests { get; set; } = new();
    }
}
