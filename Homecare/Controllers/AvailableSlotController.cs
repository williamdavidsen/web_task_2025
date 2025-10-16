using Homecare.DAL.Interfaces;
using Homecare.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Homecare.Controllers
{
    [Authorize] // all actions require auth; we narrow by role per action
    public class AvailableSlotController : Controller
    {
        private readonly IAvailableSlotRepository _slotRepo;
        private readonly IUserRepository _userRepo;
        private readonly IAppointmentRepository _apptRepo;
        private readonly ILogger<AvailableSlotController> _logger;

        public AvailableSlotController(
            IAvailableSlotRepository slotRepo,
            IUserRepository userRepo,
            IAppointmentRepository apptRepo,
            ILogger<AvailableSlotController> logger)
        {
            _slotRepo = slotRepo;
            _userRepo = userRepo;
            _apptRepo = apptRepo;
            _logger = logger;
        }

        // Helper: map current Identity email → domain personnel id (null if not found).
        private async Task<int?> CurrentPersonnelIdAsync()
        {
            var email = User?.Identity?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email)) return null;

            var personnels = await _userRepo.GetByRoleAsync(UserRole.Personnel);
            var me = personnels.FirstOrDefault(p =>
                string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase));
            return me?.UserId;
        }

        // Helper: quick ownership check for personnel
        private async Task<bool> PersonnelOwnsAsync(AvailableSlot s)
        {
            if (!User.IsInRole("Personnel")) return false;
            var myId = await CurrentPersonnelIdAsync();
            return myId.HasValue && s.PersonnelId == myId.Value;
        }

        // ================== ADMIN LIST ==================
        // Admin can see every slot in one big table.
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Table()
        {
            var slots = await _slotRepo.GetAllAsync();
            return View(slots);
        }

        // ================== DETAILS ==================
        // Admin: any slot; Personnel: only own slot.
        [Authorize(Roles = "Admin,Personnel")]
        public async Task<IActionResult> Details(int id)
        {
            var s = await _slotRepo.GetAsync(id);
            if (s == null) return NotFound();

            if (User.IsInRole("Personnel") && !await PersonnelOwnsAsync(s))
                return Forbid();

            return View(s);
        }

        // ================== CREATE (GET) ==================
        // Admin: choose any personnel; Personnel: auto-bind to self.
        [Authorize(Roles = "Admin,Personnel")]
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            if (User.IsInRole("Admin"))
            {
                var personnels = await _userRepo.GetByRoleAsync(UserRole.Personnel);
                ViewBag.PersonnelList = new SelectList(personnels, "UserId", "Name");
            }
            else
            {
                // Personnel creating for self → we still show a disabled dropdown in the view if you want.
                var myId = await CurrentPersonnelIdAsync();
                if (!myId.HasValue) return Forbid();
                var me = await _userRepo.GetAsync(myId.Value);
                ViewBag.PersonnelList = new SelectList(new[] { me! }, "UserId", "Name", myId.Value);
                ViewBag.LockPersonnel = true; // hint for the view (optional)
            }

            // simple defaults
            return View(new AvailableSlot
            {
                Day = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(11, 0)
            });
        }

        // ================== CREATE (POST) ==================
        [Authorize(Roles = "Admin,Personnel")]
        [HttpPost]
        public async Task<IActionResult> Create(AvailableSlot model)
        {
            // Rebuild the dropdown
            if (User.IsInRole("Admin"))
            {
                var personnels = await _userRepo.GetByRoleAsync(UserRole.Personnel);
                ViewBag.PersonnelList = new SelectList(personnels, "UserId", "Name", model.PersonnelId);
            }
            else
            {
                // Force personnel to create only for self (ignore tampering)
                var myId = await CurrentPersonnelIdAsync();
                if (!myId.HasValue) return Forbid();
                model.PersonnelId = myId.Value;

                var me = await _userRepo.GetAsync(myId.Value);
                ViewBag.PersonnelList = new SelectList(new[] { me! }, "UserId", "Name", myId.Value);
                ViewBag.LockPersonnel = true;
            }

            // Basic validation
            if (model.EndTime <= model.StartTime)
                ModelState.AddModelError(nameof(model.EndTime), "End time must be after start time.");

            // Prevent exact duplicates
            if (await _slotRepo.ExistsAsync(model.PersonnelId, model.Day, model.StartTime, model.EndTime))
                ModelState.AddModelError(string.Empty, "This exact slot already exists for the personnel.");

            if (!ModelState.IsValid) return View(model);

            await _slotRepo.AddAsync(model);
            TempData["Message"] = "Slot created.";
            return User.IsInRole("Admin")
                ? RedirectToAction(nameof(Table))
                : RedirectToAction("Dashboard", "Personnel", new { personnelId = model.PersonnelId });
        }

        // ================== EDIT (GET) ==================
        // Admin: any; Personnel: only own.
        [Authorize(Roles = "Admin,Personnel")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var s = await _slotRepo.GetAsync(id);
            if (s == null) return NotFound();

            if (User.IsInRole("Personnel") && !await PersonnelOwnsAsync(s))
                return Forbid();

            // Admin can switch personnel; Personnel locked to self
            if (User.IsInRole("Admin"))
            {
                var personnels = await _userRepo.GetByRoleAsync(UserRole.Personnel);
                ViewBag.PersonnelList = new SelectList(personnels, "UserId", "Name", s.PersonnelId);
            }
            else
            {
                var me = await _userRepo.GetAsync(s.PersonnelId);
                ViewBag.PersonnelList = new SelectList(new[] { me! }, "UserId", "Name", s.PersonnelId);
                ViewBag.LockPersonnel = true;
            }

            return View(s);
        }

        // ================== EDIT (POST) ==================
        [Authorize(Roles = "Admin,Personnel")]
        [HttpPost]
        public async Task<IActionResult> Edit(AvailableSlot model)
        {
            var existing = await _slotRepo.GetAsync(model.AvailableSlotId);
            if (existing == null) return NotFound();

            if (User.IsInRole("Personnel") && !await PersonnelOwnsAsync(existing))
                return Forbid();

            // Keep personnel fixed for Personnel role
            if (User.IsInRole("Personnel"))
                model.PersonnelId = existing.PersonnelId;

            // Rebind dropdowns
            if (User.IsInRole("Admin"))
            {
                var personnels = await _userRepo.GetByRoleAsync(UserRole.Personnel);
                ViewBag.PersonnelList = new SelectList(personnels, "UserId", "Name", model.PersonnelId);
            }
            else
            {
                var me = await _userRepo.GetAsync(model.PersonnelId);
                ViewBag.PersonnelList = new SelectList(new[] { me! }, "UserId", "Name", model.PersonnelId);
                ViewBag.LockPersonnel = true;
            }

            if (model.EndTime <= model.StartTime)
                ModelState.AddModelError(nameof(model.EndTime), "End time must be after start time.");

            // If another slot with same (personnel, day, start, end) exists, block.
            // Note: repo should ignore the same record id if you implement it that way; DB unique index will also help.
            if (await _slotRepo.ExistsAsync(model.PersonnelId, model.Day, model.StartTime, model.EndTime)
                && (existing.Day != model.Day || existing.StartTime != model.StartTime || existing.EndTime != model.EndTime))
            {
                ModelState.AddModelError(string.Empty, "Another slot with same time exists for this personnel.");
                return View(model);
            }

            await _slotRepo.UpdateAsync(model);
            TempData["Message"] = "Slot updated.";
            return User.IsInRole("Admin")
                ? RedirectToAction(nameof(Table))
                : RedirectToAction("Dashboard", "Personnel", new { personnelId = model.PersonnelId });
        }

        // ================== DELETE (GET) ==================
        [Authorize(Roles = "Admin,Personnel")]
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var s = await _slotRepo.GetAsync(id);
            if (s == null) return NotFound();

            if (User.IsInRole("Personnel") && !await PersonnelOwnsAsync(s))
                return Forbid();

            return View(s);
        }

        // ================== DELETE (POST) ==================
        [Authorize(Roles = "Admin,Personnel")]
        [HttpPost]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var s = await _slotRepo.GetAsync(id);
            if (s == null) return NotFound();

            if (User.IsInRole("Personnel") && !await PersonnelOwnsAsync(s))
                return Forbid();

            // Do not allow deleting a booked slot
            if (await _apptRepo.SlotIsBookedAsync(id))
            {
                TempData["Error"] = "This slot has an appointment and cannot be deleted.";
                return User.IsInRole("Admin")
                    ? RedirectToAction(nameof(Table))
                    : RedirectToAction("Dashboard", "Personnel", new { personnelId = s.PersonnelId });
            }

            await _slotRepo.DeleteAsync(s);
            TempData["Message"] = "Slot deleted.";
            return User.IsInRole("Admin")
                ? RedirectToAction(nameof(Table))
                : RedirectToAction("Dashboard", "Personnel", new { personnelId = s.PersonnelId });
        }
    }
}
