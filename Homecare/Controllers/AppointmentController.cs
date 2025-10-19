using Homecare.DAL.Interfaces;
using Homecare.Models;
using Homecare.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Homecare.Controllers
{
    // Admin: full CRUD
    // Client: can view/edit/delete ONLY own appointments
    public class AppointmentController : Controller
    {
        private readonly IAppointmentRepository _apptRepo;
        private readonly IAvailableSlotRepository _slotRepo;
        private readonly IUserRepository _userRepo;
        private readonly ICareTaskRepository _taskRepo;
        private readonly ILogger<AppointmentController> _logger;

        public AppointmentController(
            IAppointmentRepository apptRepo,
            IAvailableSlotRepository slotRepo,
            IUserRepository userRepo,
            ICareTaskRepository taskRepo,
            ILogger<AppointmentController> logger)
        {
            _apptRepo = apptRepo;
            _slotRepo = slotRepo;
            _userRepo = userRepo;
            _taskRepo = taskRepo;
            _logger = logger;
        }

        // Small helper: map Identity email -> domain ClientId
        private async Task<int?> CurrentClientIdAsync()
        {
            var email = User.Identity?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email)) return null;

            var me = (await _userRepo.GetByRoleAsync(UserRole.Client))
                .FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
            return me?.UserId;
        }

        // ================== LIST / CREATE (Admin only) ==================

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Table()
        {
            try
            {
                var list = await _apptRepo.GetAllAsync();
                return View(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] Table failed");
                TempData["Error"] = "Could not load appointments.";
                return View(Enumerable.Empty<Appointment>());
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                ViewBag.Clients = new SelectList(
                    await _userRepo.GetByRoleAsync(UserRole.Client), "UserId", "Name");

                var freeDays = await _slotRepo.GetFreeDaysAsync();
                var firstDay = freeDays.FirstOrDefault();
                var freeSlots = (firstDay == default)
                    ? new List<AvailableSlot>()
                    : await _slotRepo.GetFreeSlotsByDayAsync(firstDay);

                ViewBag.FreeSlots = new SelectList(
                    freeSlots.Select(s => new
                    {
                        s.AvailableSlotId,
                        Label = $"{s.Day:yyyy-MM-dd} {s.StartTime}-{s.EndTime} ({s.Personnel?.Name})"
                    }),
                    "AvailableSlotId", "Label");

                return View(new Appointment { Status = AppointmentStatus.Scheduled });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] Create(GET) failed");
                TempData["Error"] = "Create page could not be loaded.";
                return RedirectToAction(nameof(Table));
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Appointment model)
        {
            try
            {
                if (await _apptRepo.SlotIsBookedAsync(model.AvailableSlotId))
                    ModelState.AddModelError(nameof(model.AvailableSlotId), "This slot is already booked.");

                if (!ModelState.IsValid)
                {
                    ViewBag.Clients = new SelectList(
                        await _userRepo.GetByRoleAsync(UserRole.Client), "UserId", "Name", model.ClientId);
                    _logger.LogWarning("[Appointment] Create(POST) invalid model");
                    return View(model);
                }

                await _apptRepo.AddAsync(model);
                TempData["Message"] = "Appointment created.";
                return RedirectToAction(nameof(Table));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] Create(POST) failed");
                TempData["Error"] = "Could not create appointment.";
                return RedirectToAction(nameof(Table));
            }
        }

        // ================== DETAILS (Admin or owning Client) ==================

        [Authorize(Roles = "Admin,Client, Personnel")]
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var a = await _apptRepo.GetAsync(id);
                if (a == null) return NotFound();

                // If Client, enforce ownership
                if (User.IsInRole("Client"))
                {
                    var myId = await CurrentClientIdAsync();
                    if (myId == null || a.ClientId != myId.Value) return Forbid();
                }

                ViewBag.ReturnTo = Request.Headers["Referer"].ToString();
                return View(a);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] Details({Id}) failed", id);
                return StatusCode(500);
            }
        }

        // ================== EDIT (Admin or owning Client) ==================

        [Authorize(Roles = "Admin,Client")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var a = await _apptRepo.GetAsync(id);
                if (a == null) return NotFound();

                // If Client, must own it
                if (User.IsInRole("Client"))
                {
                    var myId = await CurrentClientIdAsync();
                    if (myId == null || a.ClientId != myId.Value) return Forbid();
                }

                // Calendar free days
                var freeDays = await _slotRepo.GetFreeDaysAsync();
                ViewBag.FreeDays = freeDays.Select(d => d.ToString("yyyy-MM-dd")).ToList();

                // Single-selected task (if any)
                int? selectedTaskId = a.Tasks?.Select(t => t.CareTaskId).FirstOrDefault();

                var tasks = await _taskRepo.GetAllAsync();
                var selectList = tasks.Select(t => new SelectListItem
                {
                    Value = t.CareTaskId.ToString(),
                    Text = t.Description,
                    Selected = selectedTaskId == t.CareTaskId
                }).ToList();

                var vm = new AppointmentEditViewModel
                {
                    Appointment = a,
                    SelectedTaskId = selectedTaskId,
                    TaskSelectList = selectList
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] Edit(GET {Id}) failed", id);
                TempData["Error"] = "Could not load edit page.";
                return RedirectToAction(nameof(Table));
            }
        }

        [Authorize(Roles = "Admin,Client")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(AppointmentEditViewModel vm)
        {
            try
            {
                var m = vm.Appointment;

                // If Client, enforce ownership and prevent tampering with ClientId
                if (User.IsInRole("Client"))
                {
                    var myId = await CurrentClientIdAsync();
                    if (myId == null || m.ClientId != myId.Value) return Forbid();
                    m.ClientId = myId.Value; // harden against form tampering
                }

                // Slot must be free (except current appt)
                if (await _apptRepo.SlotIsBookedAsync(m.AvailableSlotId, m.AppointmentId))
                    ModelState.AddModelError(nameof(vm.Appointment.AvailableSlotId), "This slot is already booked.");

                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("[Appointment] Edit(POST #{Id}) invalid model", m.AppointmentId);
                    return await RefillEditFormVM(vm);
                }

                await _apptRepo.UpdateAsync(m);
                await _apptRepo.ReplaceTasksAsync(
                    m.AppointmentId,
                    vm.SelectedTaskId.HasValue ? new[] { vm.SelectedTaskId.Value } : Array.Empty<int>());

                TempData["Message"] = "Appointment updated.";

                // Redirect according to role
                if (User.IsInRole("Client"))
                    return RedirectToAction("Dashboard", "Client", new { clientId = m.ClientId });

                return RedirectToAction(nameof(Table));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] Edit(POST #{Id}) failed", vm.Appointment?.AppointmentId);
                TempData["Error"] = "Could not update appointment.";
                return User.IsInRole("Client")
                    ? RedirectToAction("Dashboard", "Client", new { clientId = vm.Appointment?.ClientId })
                    : RedirectToAction(nameof(Table));
            }
        }

        // ================== DELETE (Admin or owning Client) ==================

        [Authorize(Roles = "Admin,Client")]
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var a = await _apptRepo.GetAsync(id);
                if (a == null) return NotFound();

                if (User.IsInRole("Client"))
                {
                    var myId = await CurrentClientIdAsync();
                    if (myId == null || a.ClientId != myId.Value) return Forbid();
                }

                return View(a);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] Delete(GET {Id}) failed", id);
                TempData["Error"] = "Could not load delete page.";
                return RedirectToAction(nameof(Table));
            }
        }

        [Authorize(Roles = "Admin,Client")]
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var a = await _apptRepo.GetAsync(id);
                if (a == null) return NotFound();

                if (User.IsInRole("Client"))
                {
                    var myId = await CurrentClientIdAsync();
                    if (myId == null || a.ClientId != myId.Value) return Forbid();
                }

                await _apptRepo.DeleteAsync(a);
                TempData["Message"] = "Appointment deleted.";

                if (User.IsInRole("Client"))
                    return RedirectToAction("Dashboard", "Client", new { clientId = a.ClientId });

                return RedirectToAction(nameof(Table));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] DeleteConfirmed({Id}) failed", id);
                TempData["Error"] = "Could not delete appointment.";
                return RedirectToAction(nameof(Table));
            }
        }

        // Refill ViewModel on validation errors
        private async Task<IActionResult> RefillEditFormVM(AppointmentEditViewModel vm)
        {
            var freeDays = await _slotRepo.GetFreeDaysAsync();
            ViewBag.FreeDays = freeDays.Select(d => d.ToString("yyyy-MM-dd")).ToList();

            var tasks = await _taskRepo.GetAllAsync();
            vm.TaskSelectList = tasks.Select(t => new SelectListItem
            {
                Value = t.CareTaskId.ToString(),
                Text = t.Description,
                Selected = vm.SelectedTaskId == t.CareTaskId
            }).ToList();

            return View("Edit", vm);
        }
    }
}
