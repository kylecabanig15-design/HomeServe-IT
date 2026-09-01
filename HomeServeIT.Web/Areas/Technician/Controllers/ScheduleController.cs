using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using HomeServeIT.Web.Areas.Technician.Models;
using HomeServeIT.Web.Services;

namespace HomeServeIT.Web.Areas.Technician.Controllers
{
    [Area("Technician")]
    [Authorize(Roles = Roles.Technician)]
    public class ScheduleController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ScheduleController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.UserID == user!.Id);

            if (tech == null)
                return View(new TechnicianScheduleViewModel());

            var scheduled = await _context.ServiceRequests
                .Include(r => r.Customer)
                .ActionableTechnicianAssignments(tech.TechID)
                .OrderBy(r => r.ScheduledDate)
                .ToListAsync();

            var vm = new TechnicianScheduleViewModel
            {
                ScheduledRequests = scheduled
            };

            return View(vm);
        }
    }
}
