using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Areas.Admin.Models;

namespace HomeServeIT.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = Roles.Administrator)]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            var vm = new AdminDashboardViewModel
            {
                TotalActiveRequests = await _context.ServiceRequests
                    .CountAsync(r => !r.IsArchived && r.Status != "Completed" && r.Status != "Cancelled"),
                JobsTodayCount = await _context.ServiceRequests
                    .CountAsync(r => !r.IsArchived && r.Status != "Cancelled" && r.ScheduledDate.Date == today),
                TotalTechnicians = await _context.Technicians.CountAsync(),
                AvailableTechniciansCount = await _context.Technicians.CountAsync(t => t.IsAvailable),
                OutstandingInvoicesAmount = await _context.Invoices
                    .Where(i => i.PaymentStatus != "Paid")
                    .SumAsync(i => (decimal?)i.TotalAmount) ?? 0,
                OutstandingInvoicesCount = await _context.Invoices
                    .CountAsync(i => i.PaymentStatus != "Paid"),
                TotalRevenue = await _context.Invoices
                    .Where(i => i.PaymentStatus == "Paid")
                    .SumAsync(i => (decimal?)i.TotalAmount) ?? 0,
                LowStockItemsCount = await _context.InventoryItems
                    .CountAsync(i => i.StockQuantity <= i.ReorderLevel),
                NewCustomersThisWeekCount = await _context.Customers
                    .Include(c => c.User)
                    .CountAsync(c => c.User != null && c.User.DateCreated >= startOfWeek),
                CompletedJobsThisMonthCount = await _context.ServiceRequests
                    .CountAsync(r => r.Status == "Completed" && ((r.CompletedDate.HasValue && r.CompletedDate.Value >= startOfMonth) || r.ScheduledDate >= startOfMonth)),
                
                // Status distribution
                CompletedCount = await _context.ServiceRequests.CountAsync(r => r.Status == "Completed"),
                InProgressCount = await _context.ServiceRequests.CountAsync(r => r.Status == "In Progress"),
                ScheduledCount = await _context.ServiceRequests.CountAsync(r => r.Status == "Scheduled"),
                PendingCount = await _context.ServiceRequests.CountAsync(r => r.Status == "Pending"),
                RecentRequests = await _context.ServiceRequests
                    .Include(r => r.Customer)
                    .Include(r => r.Technician)
                    .Where(r => !r.IsArchived && r.Status != "Cancelled")
                    .OrderByDescending(r => r.ScheduledDate)
                    .Take(5)
                    .ToListAsync()
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> GetChartData()
        {
            var now = DateTime.Now;
            // Generate last 6 months data
            var months = new List<string>();
            var revenues = new List<decimal>();
            var requests = new List<int>();

            for (int i = 5; i >= 0; i--)
            {
                var monthDate = now.AddMonths(-i);
                months.Add(monthDate.ToString("MMM"));

                // Calculate revenue for the month
                var revenue = await _context.Invoices
                    .Where(inv => inv.PaymentStatus == "Paid" && inv.DateIssued.Year == monthDate.Year && inv.DateIssued.Month == monthDate.Month)
                    .SumAsync(inv => (decimal?)inv.TotalAmount) ?? 0;
                revenues.Add(revenue);

                // Calculate request volume for the month
                var requestVolume = await _context.ServiceRequests
                    .CountAsync(sr => !sr.IsArchived
                        && sr.Status != "Cancelled"
                        && sr.ScheduledDate.Year == monthDate.Year
                        && sr.ScheduledDate.Month == monthDate.Month);
                requests.Add(requestVolume);
            }

            return Json(new {
                labels = months,
                revenueData = revenues,
                requestData = requests
            });
        }
    }
}
