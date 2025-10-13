using Homecare.DAL.Interfaces;
using Homecare.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Homecare.Controllers
{
    public class PersonnelController : Controller
    {
        private readonly IAppointmentRepository _apptRepo;
        private readonly IAvailableSlotRepository _slotRepo;
        private readonly IUserRepository _userRepo;
        private readonly ILogger<PersonnelController> _logger;

        public PersonnelController(
            IAppointmentRepository apptRepo,
            IAvailableSlotRepository slotRepo,
            IUserRepository userRepo,
            ILogger<PersonnelController> logger)
        {
            _apptRepo = apptRepo;
            _slotRepo = slotRepo;
            _userRepo = userRepo;
            _logger = logger;
        }

        // /Personnel/Dashboard?personnelId=2
        public async Task<IActionResult> Dashboard(int? personnelId)
        {
            try
            {
                int id = personnelId ?? (await _userRepo.GetByRoleAsync(UserRole.Personnel)).First().UserId;
                await SetOwnerForPersonnelAsync(id);

                var list = await _apptRepo.GetByPersonnelAsync(id);
                var now = DateTime.Now;

                var upcoming = list.Where(a => a.AvailableSlot!.Day.ToDateTime(a.AvailableSlot!.EndTime) >= now)
                                   .OrderBy(a => a.AvailableSlot!.Day)
                                   .ThenBy(a => a.AvailableSlot!.StartTime)
                                   .ToList();

                var past = list.Where(a => a.AvailableSlot!.Day.ToDateTime(a.AvailableSlot!.EndTime) < now)
                               .OrderByDescending(a => a.AvailableSlot!.Day)
                               .ThenByDescending(a => a.AvailableSlot!.StartTime)
                               .ToList();

                ViewBag.PersonnelId = id;
                ViewBag.Upcoming = upcoming;
                ViewBag.Past = past;
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PersonnelController] Dashboard failed (personnelId: {Pid})", personnelId);
                TempData["Error"] = "Could not load personnel dashboard.";
                return RedirectToAction("Table", "Appointment");
            }
        }

        private async Task SetOwnerForPersonnelAsync(int personnelId)
        {
            var u = await _userRepo.GetAsync(personnelId);
            ViewBag.OwnerName = u?.Name ?? $"Personnel #{personnelId}";
            ViewBag.OwnerRole = "Personnel";
        }

        // select day
        [HttpGet]
        public async Task<IActionResult> CreateDay(int personnelId)
        {
            try
            {
                await SetOwnerForPersonnelAsync(personnelId);
                ViewBag.PersonnelId = personnelId;

                var from = DateOnly.FromDateTime(DateTime.Today);
                var to = from.AddDays(42); // 6 hafta

                var selectedDays = await _slotRepo.GetWorkDaysAsync(personnelId, from, to);
                var lockedDays = await _slotRepo.GetLockedDaysAsync(personnelId, from, to);

                ViewBag.SelectedDaysJson = System.Text.Json.JsonSerializer.Serialize(
                    selectedDays.Select(d => d.ToString("yyyy-MM-dd")));

                ViewBag.LockedDaysJson = System.Text.Json.JsonSerializer.Serialize(
                    lockedDays.Select(d => d.ToString("yyyy-MM-dd")));

                return View(); // CreateDay.cshtml
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PersonnelController] CreateDay(GET) failed for {Pid}", personnelId);
                TempData["Error"] = "Page could not be loaded.";
                return RedirectToAction(nameof(Dashboard), new { personnelId });
            }
        }

        // ----- CREATE DAY (POST) 
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDay(int personnelId, string? days)
        {
            try
            {
                var from = DateOnly.FromDateTime(DateTime.Today);
                var to = from.AddDays(42);

                var existingDays = (await _slotRepo.GetWorkDaysAsync(personnelId, from, to)
                                    ?? Enumerable.Empty<DateOnly>()).ToHashSet();

                var lockedDays = (await _slotRepo.GetLockedDaysAsync(personnelId, from, to)
                                    ?? Enumerable.Empty<DateOnly>()).ToHashSet();

                // Formdan gelen CSV
                var chosen = new HashSet<DateOnly>();
                if (!string.IsNullOrWhiteSpace(days))
                {
                    foreach (var s in days.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (DateOnly.TryParse(s, out var d)) chosen.Add(d);
                }

                var toAdd = chosen.Except(existingDays).ToList();
                var toRemove = existingDays.Except(chosen).ToList();

                var blocked = toRemove.Where(d => lockedDays.Contains(d)).ToList();
                var removable = toRemove.Where(d => !lockedDays.Contains(d)).ToList();

                // 3 preset slot
                var presets = new (TimeOnly Start, TimeOnly End)[]
                {
                    (new TimeOnly(9,  0), new TimeOnly(11, 0)),
                    (new TimeOnly(12, 0), new TimeOnly(14, 0)),
                    (new TimeOnly(16, 0), new TimeOnly(18, 0)),
                };

                if (toAdd.Count > 0)
                {
                    var newSlots = new List<AvailableSlot>(toAdd.Count * presets.Length);
                    foreach (var day in toAdd)
                        foreach (var p in presets)
                            newSlots.Add(new AvailableSlot
                            {
                                PersonnelId = personnelId,
                                Day = day,
                                StartTime = p.Start,
                                EndTime = p.End
                            });

                    await _slotRepo.AddRangeAsync(newSlots);
                }

                if (removable.Count > 0)
                {
                    foreach (var day in removable)
                    {
                        var slots = await _slotRepo.GetSlotsForPersonnelOnDayAsync(personnelId, day)
                                    ?? Enumerable.Empty<AvailableSlot>();

                        // güvenlik: randevulu slot varsa bu günü de bloke et
                        if (slots.Any(s => s.Appointment != null))
                        {
                            if (!blocked.Contains(day))
                                blocked.Add(day);
                            continue;
                        }

                        if (slots.Any())
                            await _slotRepo.RemoveRangeAsync(slots);
                    }
                }

                if (blocked.Count > 0)
                {
                    TempData["Error"] =
                        "Some days could not be removed because there are booked appointments: " +
                        string.Join(", ", blocked.OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd"))) +
                        ". Please contact admin.";
                }
                else
                {
                    var msg = (toAdd.Count, removable.Count) switch
                    {
                        ( > 0, > 0) => $"{toAdd.Count} day(s) added, {removable.Count} day(s) removed.",
                        ( > 0, 0) => $"{toAdd.Count} day(s) added.",
                        (0, > 0) => $"{removable.Count} day(s) removed.",
                        _ => "No changes."
                    };
                    TempData["Message"] = msg;
                }

                return RedirectToAction(nameof(Dashboard), new { personnelId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PersonnelController] CreateDay(POST) failed for {Pid}", personnelId);
                TempData["Error"] = "Could not update working days.";
                return RedirectToAction(nameof(Dashboard), new { personnelId });
            }
        }
    }
}
