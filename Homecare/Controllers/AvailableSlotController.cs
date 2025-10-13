using Homecare.DAL.Interfaces;
using Homecare.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Homecare.Controllers
{
    public class AvailableSlotController : Controller
    {
        private readonly IAvailableSlotRepository _slotRepo;
        private readonly IUserRepository _userRepo;
        private readonly IAppointmentRepository _apptRepo;

        public AvailableSlotController(IAvailableSlotRepository slotRepo, IUserRepository userRepo, IAppointmentRepository apptRepo)
        { _slotRepo = slotRepo; _userRepo = userRepo; _apptRepo = apptRepo; }

        // LIST
        public async Task<IActionResult> Table()
        {
            var slots = await _slotRepo.GetAllAsync();
            return View(slots);
        }

        // DETAILS
        public async Task<IActionResult> Details(int id)
        {
            var s = await _slotRepo.GetAsync(id);
            if (s == null) return NotFound();
            return View(s);
        }

        // CREATE GET
        public async Task<IActionResult> Create()
        {
            var personnels = await _userRepo.GetByRoleAsync(UserRole.Personnel);
            ViewBag.PersonnelList = new SelectList(personnels, "UserId", "Name");
            return View(new AvailableSlot
            {
                Day = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(11, 0)
            });
        }

        // CREATE POST
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AvailableSlot model)
        {
            var personnels = await _userRepo.GetByRoleAsync(UserRole.Personnel);
            ViewBag.PersonnelList = new SelectList(personnels, "UserId", "Name", model.PersonnelId);

            if (model.EndTime <= model.StartTime)
                ModelState.AddModelError(nameof(model.EndTime), "End time must be after start time.");

            if (await _slotRepo.ExistsAsync(model.PersonnelId, model.Day, model.StartTime, model.EndTime))
                ModelState.AddModelError("", "This exact slot already exists for the personnel.");

            if (!ModelState.IsValid) return View(model);

            await _slotRepo.AddAsync(model);
            TempData["Message"] = "Slot created.";
            return RedirectToAction(nameof(Table));
        }

        // EDIT GET
        public async Task<IActionResult> Edit(int id)
        {
            var a = await _apptRepo.GetAsync(id);
            if (a == null) return NotFound();

            var u = await _userRepo.GetAsync(a.ClientId);
            ViewBag.OwnerName = u?.Name ?? $"Client #{a.ClientId}";
            ViewBag.OwnerRole = "Client";
            ViewBag.OwnerExtra = $"Appt #{id}";

            return View(a);
        }

        // EDIT POST
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(AvailableSlot model)
        {
            var personnels = await _userRepo.GetByRoleAsync(UserRole.Personnel);
            ViewBag.PersonnelList = new SelectList(personnels, "UserId", "Name", model.PersonnelId);

            if (model.EndTime <= model.StartTime)
                ModelState.AddModelError(nameof(model.EndTime), "End time must be after start time.");

            // Eğer değişiklik aynı slotu duplike ediyorsa engelle
            if (await _slotRepo.ExistsAsync(model.PersonnelId, model.Day, model.StartTime, model.EndTime))
            {
                ModelState.AddModelError("", "Another slot with same time exists for this personnel.");
                return View(model);
            }

            await _slotRepo.UpdateAsync(model);
            TempData["Message"] = "Slot updated.";
            return RedirectToAction(nameof(Table));
        }

        // DELETE GET
        public async Task<IActionResult> Delete(int id)
        {
            var s = await _slotRepo.GetAsync(id);
            if (s == null) return NotFound();
            return View(s);
        }

        // DELETE POST
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var s = await _slotRepo.GetAsync(id);
            if (s == null) return NotFound();

            if (await _apptRepo.SlotIsBookedAsync(id))
            {
                TempData["Error"] = "This slot has an appointment and cannot be deleted.";
                return RedirectToAction(nameof(Table));
            }

            await _slotRepo.DeleteAsync(s);
            TempData["Message"] = "Slot deleted.";
            return RedirectToAction(nameof(Table));
        }
    }
}
