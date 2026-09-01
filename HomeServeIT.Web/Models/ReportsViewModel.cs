namespace HomeServeIT.Web.Models;

public class ReportsViewModel
{
    public int TotalJobsCompleted { get; set; }
    public decimal RevenueLast30Days { get; set; }
    public double RevenueGrowthPercentage { get; set; }
    public int JobsCompletedGrowthPercentage { get; set; }
    public double AvgResolutionTimeDays { get; set; }
    public double ResolutionTimeChangeDays { get; set; }
    
    // Chart Data
    public List<decimal> RevenueTrend { get; set; } = new();
    public List<string> RevenueLabels { get; set; } = new();
    public Dictionary<string, int> JobsByCategory { get; set; } = new();

    // Mock fields for charts / recent reports
    public List<RecentReportItem> RecentReports { get; set; } = new();
}

public class RecentReportItem
{
    public string ReportName { get; set; } = string.Empty;
    public string GeneratedOn { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
}
