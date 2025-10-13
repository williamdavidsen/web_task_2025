using Homecare.DAL.Interfaces;
using Homecare.Models;
using Homecare.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;

namespace Homecare.Controllers
{
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

        public async Task<IActionResult> Table()
        {
            try
            {
                var list = await _apptRepo.GetAllAsync();
                return View(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentController] Table failed");
                TempData["Error"] = "Could not load appointments.";
                return View(Enumerable.Empty<Appointment>());
            }
        }

        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var a = await _apptRepo.GetAsync(id);
                if (a == null)
                {
                    _logger.LogWarning("[AppointmentController] Details: appointment #{Id} not found", id);
                    return NotFound();
                }

                // Back butonu için geldiği yer bilgisi (opsiyonel)
                ViewBag.ReturnTo = Request.Headers["Referer"].ToString();
                return View(a);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentController] Details({Id}) failed", id);
                return StatusCode(500);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                ViewBag.Clients = new SelectList(await _userRepo.GetByRoleAsync(UserRole.Client), "UserId", "Name");
                var freeDays = await _slotRepo.GetFreeDaysAsync();
                var firstDay = freeDays.FirstOrDefault();
                var freeSlots = (firstDay == default)
                    ? new List<AvailableSlot>()
                    : await _slotRepo.GetFreeSlotsByDayAsync(firstDay);

                ViewBag.FreeSlots = new SelectList(
                    freeSlots.Select(s => new { s.AvailableSlotId, Label = $"{s.Day:yyyy-MM-dd} {s.StartTime}-{s.EndTime} ({s.Personnel?.Name})" }),
                    "AvailableSlotId", "Label"
                );

                return View(new Appointment { Status = AppointmentStatus.Scheduled });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentController] Create(GET) failed");
                TempData["Error"] = "Create page could not be loaded.";
                return RedirectToAction(nameof(Table));
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Appointment model)
        {
            try
            {
                if (await _apptRepo.SlotIsBookedAsync(model.AvailableSlotId))
                    ModelState.AddModelError(nameof(model.AvailableSlotId), "This slot is already booked.");

                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("[AppointmentController] Create(POST) invalid model");
                    ViewBag.Clients = new SelectList(await _userRepo.GetByRoleAsync(UserRole.Client), "UserId", "Name", model.ClientId);
                    return View(model);
                }

                await _apptRepo.AddAsync(model);
                TempData["Message"] = "Appointment created.";
                return RedirectToAction(nameof(Table));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentController] Create(POST) failed");
                TempData["Error"] = "Could not create appointment.";
                return RedirectToAction(nameof(Table));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var a = await _apptRepo.GetAsync(id);
                if (a == null) return NotFound();

                // Takvim için boş günler
                var freeDays = await _slotRepo.GetFreeDaysAsync();
                ViewBag.FreeDays = freeDays.Select(d => d.ToString("yyyy-MM-dd")).ToList();

                // Mevcut tekli görev (varsa)
                int? selectedTaskId = a.Tasks?.Select(t => t.CareTaskId).FirstOrDefault();

                // Dropdown için seçenekler
                var tasks = await _taskRepo.GetAllAsync();
                var selectList = tasks.Select(t => new SelectListItem
                {
                    Value = t.CareTaskId.ToString(),
                    Text = t.Description,
                    Selected = selectedTaskId.HasValue && selectedTaskId.Value == t.CareTaskId
                }).ToList();

                var vm = new AppointmentEditViewModel
                {
                    Appointment = a,
                    SelectedTaskId = selectedTaskId,
                    TaskSelectList = selectList
                };

                return View(vm); // <-- ViewModel
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentController] Edit(GET {Id}) failed", id);
                TempData["Error"] = "Could not load edit page.";
                return RedirectToAction(nameof(Table));
            }
        }

        // --- EDIT (POST) -> ViewModel al ---
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(AppointmentEditViewModel vm)
        {
            try
            {
                var model = vm.Appointment;

                // Seçilen slot başka biri tarafından alındı mı?
                if (await _apptRepo.SlotIsBookedAsync(model.AvailableSlotId, model.AppointmentId))
                {
                    ModelState.AddModelError(nameof(vm.Appointment.AvailableSlotId),
                        "This slot is already booked.");
                }

                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("[AppointmentController] Edit(POST #{Id}) invalid model", model.AppointmentId);
                    return await RefillEditFormVM(vm);
                }

                await _apptRepo.UpdateAsync(model);

                // Tek seçimli "Requested Task"ı güncelle
                if (vm.SelectedTaskId.HasValue)
                    await _apptRepo.ReplaceTasksAsync(model.AppointmentId, new[] { vm.SelectedTaskId.Value });
                else
                    await _apptRepo.ReplaceTasksAsync(model.AppointmentId, Array.Empty<int>());

                TempData["Message"] = "Appointment updated.";
                return RedirectToAction("Dashboard", "Client", new { clientId = model.ClientId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentController] Edit(POST #{Id}) failed", vm.Appointment?.AppointmentId);
                TempData["Error"] = "Could not update appointment.";
                return RedirectToAction(nameof(Table));
            }
        }

        // Hata durumunda formu yeniden doldur
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
                _logger.LogError(ex, "[AppointmentController] Delete(GET {Id}) failed", id);
                TempData["Error"] = "Could not load delete page.";
                return RedirectToAction(nameof(Table));
            }
        }

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
                _logger.LogError(ex, "[AppointmentController] DeleteConfirmed({Id}) failed", id);
                TempData["Error"] = "Could not delete appointment.";
            }
            return RedirectToAction(nameof(Table));
        }
    }
}
