using Homecare.DAL.Interfaces;
using Homecare.Models;
using Homecare.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Homecare.Controllers
{
    public class AdminController : Controller
    {
        private readonly IUserRepository _userRepo;
        private readonly IAppointmentRepository _apptRepo;
        private readonly IAvailableSlotRepository _slotRepo;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IUserRepository userRepo,
            IAppointmentRepository apptRepo,
            IAvailableSlotRepository slotRepo,
            ILogger<AdminController> logger)
        {
            _userRepo = userRepo;
            _apptRepo = apptRepo;
            _slotRepo = slotRepo;
            _logger = logger;
        }

        // /Admin/Dashboard
        public async Task<IActionResult> Dashboard(int up = 1, int fr = 1)
        {
            try
            {
                const int pageSize = 10; // the view slices by this size
                ViewBag.OwnerName = User?.Identity?.Name ?? "Administrator";
                ViewBag.OwnerRole = "Admin";

                // 1) Counters
                var clients = await _userRepo.GetByRoleAsync(UserRole.Client);
                var personnels = await _userRepo.GetByRoleAsync(UserRole.Personnel);
                var appts = await _apptRepo.GetAllAsync();

                ViewBag.TotalClients = clients.Count;
                ViewBag.TotalPersonnels = personnels.Count;
                ViewBag.TotalAppts = appts.Count;

                // 2) Quick jump dropdowns
                ViewBag.ClientsDD = new SelectList(clients, "UserId", "Name");
                ViewBag.PersonnelsDD = new SelectList(personnels, "UserId", "Name");

                // 3) Upcoming (all)
                var now = DateTime.Now;
                var upcomingAll = appts
                    .Where(a => a.AvailableSlot != null &&
                                a.AvailableSlot.Day.ToDateTime(a.AvailableSlot.EndTime) >= now)
                    .OrderBy(a => a.AvailableSlot!.Day)
                    .ThenBy(a => a.AvailableSlot!.StartTime)
                    .ToList();

                // 4) Free slots (all, next 14 days)
                var freeAll = new List<AvailableSlot>();
                var freeDays = await _slotRepo.GetFreeDaysAsync(14);
                foreach (var d in freeDays)
                {
                    var daySlots = await _slotRepo.GetFreeSlotsByDayAsync(d);
                    if (daySlots != null && daySlots.Any())
                        freeAll.AddRange(daySlots);
                }
                freeAll = freeAll.OrderBy(s => s.Day).ThenBy(s => s.StartTime).ToList();

                // 5) Simple paging metadata (the view needs these numbers)
                ViewBag.UpPage = Math.Max(1, up);
                ViewBag.UpTotal = Math.Max(1, (int)Math.Ceiling(upcomingAll.Count / (double)pageSize));
                ViewBag.FrPage = Math.Max(1, fr);
                ViewBag.FrTotal = Math.Max(1, (int)Math.Ceiling(freeAll.Count / (double)pageSize));
                ViewBag.PageSize = pageSize;

                // 6) Return VM with full collections; the view will slice.
                var vm = new AdminDashboardViewModel
                {
                    UpcomingAll = upcomingAll,
                    FreeAll = freeAll
                };
                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AdminController] Dashboard failed");
                TempData["Error"] = "Dashboard could not be loaded.";

                // Keep view simple with empty data
                ViewBag.TotalClients = 0;
                ViewBag.TotalPersonnels = 0;
                ViewBag.TotalAppts = 0;
                ViewBag.ClientsDD = new SelectList(Enumerable.Empty<object>());
                ViewBag.PersonnelsDD = new SelectList(Enumerable.Empty<object>());
                ViewBag.UpPage = ViewBag.FrPage = 1;
                ViewBag.UpTotal = ViewBag.FrTotal = 1;
                ViewBag.PageSize = 10;

                return View(new AdminDashboardViewModel());
            }
        }
    }
}
