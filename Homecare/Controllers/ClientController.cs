using Homecare.DAL.Interfaces;
using Homecare.Models;
using Homecare.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;

namespace Homecare.Controllers
{
    public class ClientController : Controller
    {
        private readonly IAppointmentRepository _apptRepo;
        private readonly IAvailableSlotRepository _slotRepo;
        private readonly IUserRepository _userRepo;
        private readonly ICareTaskRepository _taskRepo;
        private readonly ILogger<ClientController> _logger;

        public ClientController(
            IAppointmentRepository apptRepo,
            IAvailableSlotRepository slotRepo,
            IUserRepository userRepo,
            ICareTaskRepository taskRepo,
            ILogger<ClientController> logger)
        {
            _apptRepo = apptRepo;
            _slotRepo = slotRepo;
            _userRepo = userRepo;
            _taskRepo = taskRepo;
            _logger = logger;
        }

        // Resolve a clientId (use given id or fall back to the first client in DB)
        private async Task<int> ResolveClientId(int? clientId)
        {
            if (clientId.HasValue) return clientId.Value;
            var first = (await _userRepo.GetByRoleAsync(UserRole.Client)).FirstOrDefault();
            if (first == null) throw new InvalidOperationException("No client exists in the system.");
            return first.UserId;
        }

        // Put owner info into the top bar (Shared/_OwnerBar)
        private async Task SetOwnerForClientAsync(int clientId)
        {
            var u = await _userRepo.GetAsync(clientId);
            ViewBag.OwnerName = u?.Name ?? $"Client #{clientId}";
            ViewBag.OwnerRole = "Client";
        }

        // ---------- DASHBOARD ----------
        // Lists upcoming/past appointments for this client.
        [HttpGet]
        [Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> Dashboard(int? clientId)
        {
            try
            {
                int id = await ResolveClientId(clientId);
                await SetOwnerForClientAsync(id);

                var list = await _apptRepo.GetByClientAsync(id);
                var now = DateTime.Now;

                var upcoming = list.Where(a => a.AvailableSlot != null &&
                                               a.AvailableSlot.Day.ToDateTime(a.AvailableSlot.EndTime) >= now)
                                   .OrderBy(a => a.AvailableSlot!.Day)
                                   .ThenBy(a => a.AvailableSlot!.StartTime)
                                   .ToList();

                var past = list.Where(a => a.AvailableSlot != null &&
                                           a.AvailableSlot.Day.ToDateTime(a.AvailableSlot.EndTime) < now)
                               .OrderByDescending(a => a.AvailableSlot!.Day)
                               .ThenByDescending(a => a.AvailableSlot!.StartTime)
                               .ToList();

                ViewBag.ClientId = id;
                ViewBag.Upcoming = upcoming;
                ViewBag.Past = past;
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ClientController] Dashboard failed (clientId: {Cid})", clientId);
                TempData["Error"] = "Could not load client dashboard.";
                return RedirectToAction("Table", "Appointment");
            }
        }

        // ---------- CREATE (GET) ----------
        // Shows the booking form. Calendar gets only free days.
        [HttpGet]
        [Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> Create(int clientId, string? day = null)
        {
            try
            {
                await SetOwnerForClientAsync(clientId);
                ViewBag.ClientId = clientId;

                // 1) Calendar: free days
                var freeDays = await _slotRepo.GetFreeDaysAsync();
                ViewBag.FreeDays = freeDays.Select(d => d.ToString("yyyy-MM-dd")).ToList();
                ViewBag.InitialMonth = (freeDays.Any() ? freeDays.Min() : DateOnly.FromDateTime(DateTime.Today))
                                        .ToString("yyyy-MM-01");

                // Simple fallback dropdown for days (disabled if not free)
                var freeSet = freeDays.ToHashSet();
                const int rangeDays = 14;
                var start = DateOnly.FromDateTime(DateTime.Today);
                ViewBag.DayItems = Enumerable.Range(0, rangeDays)
                    .Select(i => start.AddDays(i))
                    .Select(d => new SelectListItem
                    {
                        Text = d.ToString("yyyy-MM-dd dddd"),
                        Value = d.ToString("yyyy-MM-dd"),
                        Disabled = !freeSet.Contains(d)
                    }).ToList();

                // If a day is preselected, preload its free slots
                if (!string.IsNullOrEmpty(day) && DateOnly.TryParse(day, out var sel))
                {
                    var slots = await _slotRepo.GetFreeSlotsByDayAsync(sel);
                    ViewBag.SlotItems = slots.Select(s => new SelectListItem
                    {
                        Value = s.AvailableSlotId.ToString(),
                        Text = $"{s.StartTime:HH\\:mm}-{s.EndTime:HH\\:mm} ({s.Personnel?.Name})"
                    }).ToList();
                }
                else
                {
                    ViewBag.SlotItems = new List<SelectListItem>();
                }

                // 2) Task dropdown (single optional selection)
                var tasks = await _taskRepo.GetAllAsync();
                var vm = new AppointmentCreateViewModel
                {
                    Appointment = new Appointment
                    {
                        ClientId = clientId,
                        Status = AppointmentStatus.Scheduled
                    },
                    TaskSelectList = tasks
                        .Select(t => new SelectListItem { Value = t.CareTaskId.ToString(), Text = t.Description })
                        .ToList()
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ClientController] Create(GET) failed for {Cid}", clientId);
                TempData["Error"] = "Create page could not be loaded.";
                return RedirectToAction(nameof(Dashboard), new { clientId });
            }
        }

        // ---------- CREATE (POST) ----------
        // Validates that the selected slot is still free and saves appointment.
        [HttpPost]
        [Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> Create(int clientId, AppointmentCreateViewModel vm)
        {
            try
            {
                vm.Appointment.ClientId = clientId;

                // Make sure the chosen slot wasn’t taken meanwhile
                if (await _apptRepo.SlotIsBookedAsync(vm.Appointment.AvailableSlotId))
                {
                    ModelState.AddModelError(nameof(vm.Appointment.AvailableSlotId),
                                             "Selected slot is no longer available.");
                }

                if (!ModelState.IsValid)
                    return await RefillCreateFormVM(clientId, vm);

                await _apptRepo.AddAsync(vm.Appointment);

                // Optional: attach a single requested task
                // if (vm.SelectedTaskId.HasValue)
                //     await _apptRepo.ReplaceTasksAsync(vm.Appointment.AppointmentId, new[] { vm.SelectedTaskId.Value });

                TempData["Message"] = "Appointment booked.";
                return RedirectToAction(nameof(Dashboard), new { clientId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ClientController] Create(POST) failed for {Cid}", clientId);
                TempData["Error"] = "Could not create appointment.";
                return RedirectToAction(nameof(Dashboard), new { clientId });
            }
        }

        // Refill the form after validation errors (keeps dropdowns populated)
        private async Task<IActionResult> RefillCreateFormVM(int clientId, AppointmentCreateViewModel vm)
        {
            ViewBag.ClientId = clientId;

            var freeDays = await _slotRepo.GetFreeDaysAsync();
            ViewBag.FreeDays = freeDays.Select(d => d.ToString("yyyy-MM-dd")).ToList();
            ViewBag.InitialMonth = (freeDays.Any() ? freeDays.Min() : DateOnly.FromDateTime(DateTime.Today))
                                    .ToString("yyyy-MM-01");

            var freeSet = freeDays.ToHashSet();
            const int rangeDays = 14;
            var start = DateOnly.FromDateTime(DateTime.Today);
            ViewBag.DayItems = Enumerable.Range(0, rangeDays)
                .Select(i => start.AddDays(i))
                .Select(d => new SelectListItem
                {
                    Text = d.ToString("yyyy-MM-dd dddd"),
                    Value = d.ToString("yyyy-MM-dd"),
                    Disabled = !freeSet.Contains(d)
                }).ToList();

            var tasks = await _taskRepo.GetAllAsync();
            vm.TaskSelectList = tasks.Select(t => new SelectListItem
            {
                Value = t.CareTaskId.ToString(),
                Text = t.Description,
                Selected = vm.SelectedTaskId.HasValue && vm.SelectedTaskId.Value == t.CareTaskId
            }).ToList();

            return View("Create", vm);
        }

        // ---------- AJAX ----------
        // Returns free slots for a given day (used by the calendar UI).
        [HttpGet]
        [Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> SlotsForDay(string day)
        {
            try
            {
                if (!DateOnly.TryParse(day, out var d))
                    return Json(Array.Empty<object>());

                var slots = await _slotRepo.GetFreeSlotsByDayAsync(d);
                var data = slots.Select(s => new
                {
                    id = s.AvailableSlotId,
                    label = $"{s.StartTime:HH\\:mm}-{s.EndTime:HH\\:mm} ({s.Personnel?.Name})"
                });

                return Json(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ClientController] SlotsForDay failed for {Day}", day);
                return Json(Array.Empty<object>());
            }
        }
    }
}
