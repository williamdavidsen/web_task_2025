using Homecare.DAL.Interfaces;
using Homecare.Models;
using Homecare.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Homecare.Controllers
{
    // Admin-only list/create/edit/delete. Details is allowed for Admin and the owning Client.
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

        // ADMIN: list all appointments
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

        // ADMIN or owning CLIENT: details
        [Authorize(Roles = "Admin,Client")]
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var a = await _apptRepo.GetAsync(id);
                if (a == null) return NotFound();

                // If the caller is a Client, enforce ownership by email->domain mapping
                if (User.IsInRole("Client"))
                {
                    var email = User.Identity?.Name ?? string.Empty;
                    var me = (await _userRepo.GetByRoleAsync(UserRole.Client))
                             .FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                    if (me == null || a.ClientId != me.UserId) return Forbid();
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

        // ADMIN: create (GET)
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

        // ADMIN: create (POST)
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

        // ADMIN: edit (GET)
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var a = await _apptRepo.GetAsync(id);
                if (a == null) return NotFound();

                var freeDays = await _slotRepo.GetFreeDaysAsync();
                ViewBag.FreeDays = freeDays.Select(d => d.ToString("yyyy-MM-dd")).ToList();

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

        // ADMIN: edit (POST)
        [Authorize(Roles = "Admin")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(AppointmentEditViewModel vm)
        {
            try
            {
                var m = vm.Appointment;

                // If slot changed, ensure it is still free
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
                return RedirectToAction(nameof(Table));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] Edit(POST #{Id}) failed", vm.Appointment?.AppointmentId);
                TempData["Error"] = "Could not update appointment.";
                return RedirectToAction(nameof(Table));
            }
        }

        // ADMIN: delete (GET)
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var a = await _apptRepo.GetAsync(id);
                if (a == null) return NotFound();
                return View(a);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] Delete(GET {Id}) failed", id);
                TempData["Error"] = "Could not load delete page.";
                return RedirectToAction(nameof(Table));
            }
        }

        // ADMIN: delete (POST)
        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var a = await _apptRepo.GetAsync(id);
                if (a == null) return NotFound();
                await _apptRepo.DeleteAsync(a);
                TempData["Message"] = "Appointment deleted.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Appointment] DeleteConfirmed({Id}) failed", id);
                TempData["Error"] = "Could not delete appointment.";
            }
            return RedirectToAction(nameof(Table));
        }

        // helper to re-fill edit form
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
