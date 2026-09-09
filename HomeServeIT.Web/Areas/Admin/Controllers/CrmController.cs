using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HomeServeIT.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = Roles.Administrator)]
    public class CrmController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CrmController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Customers()
        {
            var customers = await _context.Customers
                .Include(c => c.User)
                .ToListAsync();
            return View(customers);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCustomer([FromServices] UserManager<ApplicationUser> userManager, string firstName, string lastName, string email, string phone, string address, [FromServices] HomeServeIT.Web.Services.AccountProfileService profiles)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                TempData["ErrorMessage"] = "Name and email are required.";
                return RedirectToAction(nameof(Customers));
            }

            var user = new ApplicationUser { UserName = email.Trim(), Email = email.Trim(), FullName = $"{firstName} {lastName}", PhoneNumber = phone, StreetAddress = address };
            var result = await profiles.CreateAsync(user, Roles.Customer);
            if (result.Succeeded)
            {
                return await HomeServeIT.Web.Services.InvitationResult.ShowAsync(this, userManager, user);
            }
            else
            {
                TempData["ErrorMessage"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
            return RedirectToAction(nameof(Customers));
        }

        public async Task<IActionResult> CustomerDetail(int id)
        {
            if (id == 0)
            {
                return RedirectToAction(nameof(Customers));
            }

            var customer = await _context.Customers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.CustomerID == id);
            
            if (customer == null)
            {
                return NotFound();
            }

            ViewBag.ServiceRequests = await _context.ServiceRequests
                .Include(r => r.Technician)
                .Where(r => r.CustomerID == id)
                .OrderByDescending(r => r.ScheduledDate)
                .ToListAsync();

            ViewBag.Invoices = await _context.Invoices
                .Include(i => i.ServiceRequest)
                .Where(i => i.ServiceRequest.CustomerID == id)
                .OrderByDescending(i => i.DateIssued)
                .ToListAsync();

            ViewBag.Devices = await _context.Devices
                .Where(d => d.CustomerID == id)
                .OrderByDescending(d => d.DateAdded)
                .ToListAsync();

            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCustomer([FromServices] HomeServeIT.Web.Services.AccountProfileService profiles, int customerId, string firstName, string lastName, string phone, string address)
        {
            var customer = await _context.Customers.Include(c => c.User).FirstOrDefaultAsync(c => c.CustomerID == customerId);
            if (customer?.User == null) return NotFound();
            var model = ProfileViewModel.FromUser(customer.User);
            model.FullName = $"{firstName} {lastName}";
            model.Mobile = phone;
            model.Address = address;
            model.City = null; // The CRM address input contains the complete address.
            var result = await profiles.UpdateAsync(customer.User, model);
            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Succeeded
                ? "Customer updated successfully." : string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(CustomerDetail), new { id = customerId });
        }

        public async Task<IActionResult> ServiceHistory()
        {
            var history = await _context.ServiceRequests
                .Include(r => r.Customer)
                .Include(r => r.Technician)
                .Where(r => r.Status == "Completed")
                .OrderByDescending(r => r.CompletedDate ?? r.ScheduledDate)
                .ToListAsync();

            var requestIds = history.Select(h => h.RequestID).ToList();
            var invoices = await _context.Invoices
                .Where(i => requestIds.Contains(i.RequestID))
                .ToDictionaryAsync(i => i.RequestID, i => i.TotalAmount);

            ViewBag.InvoiceAmounts = invoices;
            return View(history);
        }

        public async Task<IActionResult> Reports()
        {
            var now = DateTime.UtcNow;
            var thirtyDaysAgo = now.AddDays(-30);
            var sixtyDaysAgo = now.AddDays(-60);

            // Revenue calculation
            var invoicesLast30 = await _context.Invoices
                .Where(i => i.PaymentStatus == "Paid" && i.DateIssued >= thirtyDaysAgo)
                .ToListAsync();
            var revenueLast30 = invoicesLast30.Sum(i => i.TotalAmount);

            var invoicesPrev30 = await _context.Invoices
                .Where(i => i.PaymentStatus == "Paid" && i.DateIssued >= sixtyDaysAgo && i.DateIssued < thirtyDaysAgo)
                .ToListAsync();
            var revenuePrev30 = invoicesPrev30.Sum(i => i.TotalAmount);
            
            var revenueGrowth = revenuePrev30 == 0 ? (revenueLast30 > 0 ? 100 : 0) : (double)((revenueLast30 - revenuePrev30) / revenuePrev30) * 100;

            // Jobs completed calculation
            var jobsLast30 = await _context.ServiceRequests
                .Where(r => r.Status == "Completed" && r.CompletedDate != null && r.CompletedDate >= thirtyDaysAgo)
                .ToListAsync();
            var completedJobsLast30 = jobsLast30.Count;

            var jobsPrev30 = await _context.ServiceRequests
                .Where(r => r.Status == "Completed" && r.CompletedDate != null && r.CompletedDate >= sixtyDaysAgo && r.CompletedDate < thirtyDaysAgo)
                .ToListAsync();
            var completedJobsPrev30 = jobsPrev30.Count;

            var jobsGrowth = completedJobsPrev30 == 0 ? (completedJobsLast30 > 0 ? 100 : 0) : (double)(completedJobsLast30 - completedJobsPrev30) / completedJobsPrev30 * 100;

            // Average resolution time
            var avgResLast30 = HomeServeIT.Web.Services.CompletionTiming.AverageDays(jobsLast30);
            var avgResPrev30 = HomeServeIT.Web.Services.CompletionTiming.AverageDays(jobsPrev30);
            var resTimeChange = avgResLast30 - avgResPrev30;

            // Jobs by category (Last 30 days)
            var jobsByCategory = jobsLast30
                .GroupBy(r => r.ServiceCategory ?? "Other")
                .ToDictionary(g => g.Key, g => g.Count());

            // Revenue Trend (Last 6 months)
            var sixMonthsAgo = now.AddMonths(-6);
            var invoicesLast6Months = await _context.Invoices
                .Where(i => i.PaymentStatus == "Paid" && i.DateIssued >= sixMonthsAgo)
                .ToListAsync();

            var revenueTrend = new List<decimal>();
            var revenueLabels = new List<string>();

            for (int i = 5; i >= 0; i--)
            {
                var monthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1);
                var monthRevenue = invoicesLast6Months
                    .Where(inv => inv.DateIssued >= monthStart && inv.DateIssued < monthEnd)
                    .Sum(inv => inv.TotalAmount);
                
                revenueTrend.Add(monthRevenue);
                revenueLabels.Add(monthStart.ToString("MMM"));
            }

            var model = new ReportsViewModel
            {
                TotalJobsCompleted = completedJobsLast30,
                RevenueLast30Days = revenueLast30,
                RevenueGrowthPercentage = Math.Round(revenueGrowth, 1),
                JobsCompletedGrowthPercentage = (int)Math.Round(jobsGrowth),
                AvgResolutionTimeDays = Math.Round(avgResLast30, 1),
                ResolutionTimeChangeDays = Math.Round(resTimeChange, 1),
                JobsByCategory = jobsByCategory,
                RevenueTrend = revenueTrend,
                RevenueLabels = revenueLabels,
                RecentReports = new List<RecentReportItem>()
            };
            
            return View(model);
        }

        public async Task<IActionResult> DownloadReport(string type = "Full Summary", string? dateRange = "30", DateTime? startDate = null, DateTime? endDate = null)
        {
            var now = DateTime.UtcNow;
            DateTime currentStart, currentEnd, previousStart, previousEnd;

            if (dateRange == "custom" && startDate.HasValue && endDate.HasValue)
            {
                currentStart = startDate.Value;
                currentEnd = endDate.Value.AddDays(1).AddTicks(-1);
                var duration = currentEnd - currentStart;
                previousStart = currentStart - duration;
                previousEnd = currentStart;
            }
            else if (dateRange == "7")
            {
                currentStart = now.AddDays(-7);
                currentEnd = now;
                previousStart = now.AddDays(-14);
                previousEnd = currentStart;
            }
            else if (dateRange == "90")
            {
                currentStart = now.AddDays(-90);
                currentEnd = now;
                previousStart = now.AddDays(-180);
                previousEnd = currentStart;
            }
            else
            {
                // Default: 30 days
                currentStart = now.AddDays(-30);
                currentEnd = now;
                previousStart = now.AddDays(-60);
                previousEnd = currentStart;
            }

            // Real DB calculations
            var revCurrent = await _context.Invoices
                .Where(i => i.PaymentStatus == "Paid" && i.DateIssued >= currentStart && i.DateIssued <= currentEnd)
                .SumAsync(i => (decimal?)i.TotalAmount) ?? 0;

            var revPrev = await _context.Invoices
                .Where(i => i.PaymentStatus == "Paid" && i.DateIssued >= previousStart && i.DateIssued < previousEnd)
                .SumAsync(i => (decimal?)i.TotalAmount) ?? 0;

            var revGrowth = revPrev == 0 ? (revCurrent > 0 ? "+100%" : "0.0%") : $"{(revCurrent - revPrev) / revPrev:P1}";

            var jobsCurrent = await _context.ServiceRequests
                .CountAsync(r => r.Status == "Completed" && (r.CompletedDate.HasValue && r.CompletedDate.Value >= currentStart && r.CompletedDate.Value <= currentEnd));

            var jobsPrev = await _context.ServiceRequests
                .CountAsync(r => r.Status == "Completed" && (r.CompletedDate.HasValue && r.CompletedDate.Value >= previousStart && r.CompletedDate.Value < previousEnd));

            var jobsGrowth = jobsPrev == 0 ? (jobsCurrent > 0 ? "+100%" : "0.0%") : $"{(jobsCurrent - jobsPrev) / (double)jobsPrev:P1}";

            var completedRequests = await _context.ServiceRequests
                .Where(r => r.Status == "Completed" && r.CompletedDate.HasValue && r.CompletedDate.Value >= currentStart && r.CompletedDate.Value <= currentEnd)
                .ToListAsync();

            var prevCompletedRequests = await _context.ServiceRequests
                .Where(r => r.Status == "Completed" && r.CompletedDate.HasValue && r.CompletedDate.Value >= previousStart && r.CompletedDate.Value < previousEnd)
                .ToListAsync();

            var avgResolutionDays = HomeServeIT.Web.Services.CompletionTiming.AverageDays(completedRequests);
            var prevAvgResolutionDays = HomeServeIT.Web.Services.CompletionTiming.AverageDays(prevCompletedRequests);

            var resTrend = avgResolutionDays > 0 && prevAvgResolutionDays > 0 ? $"{(avgResolutionDays - prevAvgResolutionDays):+0.0;-0.0;0.0} Days" : "—";

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header().Element(compose => ComposeHeader(compose, type, currentStart, currentEnd));
                    page.Content().Element(compose => ComposeContent(compose, type, revCurrent, revPrev, revGrowth, jobsCurrent, jobsPrev, jobsGrowth, avgResolutionDays, prevAvgResolutionDays, resTrend));
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            });

            var pdfBytes = document.GeneratePdf();
            return File(pdfBytes, "application/pdf", $"{type.Replace(" ", "_")}_Report_{DateTime.UtcNow:yyyyMMdd}.pdf");
        }

        private void ComposeHeader(IContainer container, string type, DateTime start, DateTime end)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text("HomeServe IT").FontSize(22).Bold().FontColor("#0878f9");
                    column.Item().Text($"Report: {type}").FontSize(14).SemiBold().FontColor(Colors.Grey.Darken3);
                    column.Item().Text($"Period: {start:MMM d, yyyy} – {end:MMM d, yyyy} | Generated: {DateTime.Now:MMM d, yyyy}").FontSize(10).FontColor(Colors.Grey.Medium);
                });
            });
        }

        private void ComposeContent(IContainer container, string type, decimal revCurrent, decimal revPrev, string revGrowth, int jobsCurrent, int jobsPrev, string jobsGrowth, double avgRes, double prevAvgRes, string resTrend)
        {
            container.PaddingVertical(1, Unit.Centimetre).Column(column =>
            {
                column.Spacing(20);
                
                column.Item().Text("Executive Summary").FontSize(14).SemiBold();
                column.Item().Text($"This document contains the {type} report generated automatically from live HomeServe IT system records. It reflects actual completed services, verified billing, and operational performance metrics.");

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1.5f);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellStyle).Text("Metric");
                        header.Cell().Element(CellStyle).Text("Current Period");
                        header.Cell().Element(CellStyle).Text("Previous Period");
                        header.Cell().Element(CellStyle).Text("Trend");

                        static IContainer CellStyle(IContainer container)
                        {
                            return container.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(6).BorderBottom(1.5f).BorderColor(Colors.Grey.Darken2);
                        }
                    });

                    table.Cell().Element(CellStyle).Text("Total Revenue");
                    table.Cell().Element(CellStyle).Text(revCurrent.ToString("C2", new System.Globalization.CultureInfo("en-PH")));
                    table.Cell().Element(CellStyle).Text(revPrev.ToString("C2", new System.Globalization.CultureInfo("en-PH")));
                    table.Cell().Element(CellStyle).Text(revGrowth);

                    table.Cell().Element(CellStyle).Text("Jobs Completed");
                    table.Cell().Element(CellStyle).Text(jobsCurrent.ToString());
                    table.Cell().Element(CellStyle).Text(jobsPrev.ToString());
                    table.Cell().Element(CellStyle).Text(jobsGrowth);

                    table.Cell().Element(CellStyle).Text("Avg scheduled-to-completion days (valid dates)");
                    table.Cell().Element(CellStyle).Text(avgRes > 0 ? $"{avgRes:0.0} Days" : "—");
                    table.Cell().Element(CellStyle).Text(prevAvgRes > 0 ? $"{prevAvgRes:0.0} Days" : "—");
                    table.Cell().Element(CellStyle).Text(resTrend);

                    static IContainer CellStyle(IContainer container)
                    {
                        return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(6);
                    }
                });
            });
        }
    }
}
