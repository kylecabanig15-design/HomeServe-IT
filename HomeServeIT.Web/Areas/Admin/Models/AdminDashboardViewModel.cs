using HomeServeIT.Web.Models;

namespace HomeServeIT.Web.Areas.Admin.Models
{
    public class AdminDashboardViewModel
    {
        public int TotalActiveRequests { get; set; }
        public int JobsTodayCount { get; set; }
        public int TotalTechnicians { get; set; }
        public int AvailableTechniciansCount { get; set; }
        public decimal OutstandingInvoicesAmount { get; set; }
        public int OutstandingInvoicesCount { get; set; }
        public decimal TotalRevenue { get; set; }
        public int LowStockItemsCount { get; set; }
        public int NewCustomersThisWeekCount { get; set; }
        public int CompletedJobsThisMonthCount { get; set; }
        
        // Status Distribution
        public int CompletedCount { get; set; }
        public int InProgressCount { get; set; }
        public int ScheduledCount { get; set; }
        public int PendingCount { get; set; }
        public int TotalJobsCount => CompletedCount + InProgressCount + ScheduledCount + PendingCount;

        public List<ServiceRequest> RecentRequests { get; set; } = new List<ServiceRequest>();
    }
}
