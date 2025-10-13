using Homecare.DAL.Interfaces;

using Homecare.Models;
using Microsoft.EntityFrameworkCore;

namespace Homecare.DAL.Repositories
{
    public class AvailableSlotRepository : IAvailableSlotRepository
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AvailableSlotRepository> _logger;
        public AvailableSlotRepository(AppDbContext db, ILogger<AvailableSlotRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<AvailableSlot>> GetAllAsync()
        {
            try
            {
                return await _db.AvailableSlots.Include(s => s.Personnel).Include(s => s.Appointment)
               .OrderBy(s => s.Day).ThenBy(s => s.StartTime).ToListAsync();

            }
            catch (Exception e)
            {

                _logger.LogError(e, "[AvaibleSlotRepostory] GetAllAsync failed");
                return new List<AvailableSlot>();
            }
        }


        public async Task<AvailableSlot?> GetAsync(int id)
        {
            try
            {
                return await _db.AvailableSlots.Include(s => s.Personnel).Include(s => s.Appointment)
               .FirstOrDefaultAsync(s => s.AvailableSlotId == id);

            }
            catch (Exception e)
            {

                _logger.LogError(e, "[AvaibleSlotRepostory] GetAsync({Id}) failed", id);
                return null;
            }
        }

        public async Task<List<DateOnly>> GetFreeDaysAsync(int rangeDays = 42)
        //Returns future days (within range) that still have at least one free slot (no appointment).
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var until = today.AddDays(rangeDays);

                return await _db.AvailableSlots
                    .AsNoTracking()
                    .Where(s =>
                        s.Day >= today &&
                        s.Day <= until &&
                        s.Appointment == null)
                    .Select(s => s.Day)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[AvailableSlotRepository] GetFreeDaysAsync({Range}) failed", rangeDays);
                return new List<DateOnly>();
            }

        }

        public async Task<List<AvailableSlot>> GetFreeSlotsByDayAsync(DateOnly day)
        //Returns free slots (no appointment) for a specific day, ordered by time, with Personnel loaded.
        {
            try
            {
                return await _db.AvailableSlots
                                .AsNoTracking()
                                .Where(s => s.Day == day && s.Appointment == null)
                                .OrderBy(s => s.StartTime)
                                .Include(s => s.Personnel)
                                .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AvailableSlotRepository] GetFreeSlotsByDayAsync({Day}) failed", day);
                return new List<AvailableSlot>();
            }

        }
        public async Task AddAsync(AvailableSlot slot)
        {
            try
            {
                _db.AvailableSlots.Add(slot);
                await _db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[AvailableSlotRepository] AddAsync failed for personnel #{PersonnelId} {Day} {Start}-{End}",
                    slot.PersonnelId, slot.Day, slot.StartTime, slot.EndTime);

            }

        }
        public async Task AddRangeAsync(IEnumerable<AvailableSlot> slots)
        {
            try
            {
                _db.AvailableSlots.AddRange(slots);
                await _db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[AvailableSlotRepository] AddRangeAsync failed");
                throw;
            }

        }
        public async Task UpdateAsync(AvailableSlot slot)
        {
            try
            {
                _db.AvailableSlots.Update(slot);
                await _db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[AvailableSlotRepository] UpdateAsync failed for #{Id}", slot.AvailableSlotId);
                throw;
            }

        }
        public async Task DeleteAsync(AvailableSlot slot)
        {
            try
            {
                _db.AvailableSlots.Remove(slot);
                await _db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[AvailableSlotRepository] DeleteAsync failed for #{Id}", slot.AvailableSlotId);
                throw;
            }

        }
        public async Task<List<DateOnly>> GetWorkDaysAsync(int personnelId, DateOnly from, DateOnly to)
        // Returns distinct work days for the given personnel in [from, to].
        {
            try
            {
                return await _db.AvailableSlots
                .Where(s => s.PersonnelId == personnelId &&
                            s.Day >= from && s.Day <= to)
                .Select(s => s.Day)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[AvailableSlotRepository] GetWorkDaysAsync({PersonnelId},{From},{To}) failed",
                    personnelId, from, to);
                return new List<DateOnly>();
            }

        }

        public async Task<List<DateOnly>> GetLockedDaysAsync(int personnelId, DateOnly from, DateOnly to)
        // Returns days in [from, to] for the personnel that already have at least one booked appointment.
        {
            try
            {
                return await _db.AvailableSlots
                .Where(s => s.PersonnelId == personnelId &&
                            s.Day >= from && s.Day <= to &&
                            s.Appointment != null)
                .Select(s => s.Day)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[AvailableSlotRepository] GetLockedDaysAsync({PersonnelId},{From},{To}) failed",
                    personnelId, from, to);
                return new List<DateOnly>();
            }

        }

        public async Task<List<AvailableSlot>> GetSlotsForPersonnelOnDayAsync(int personnelId, DateOnly day)
        // Returns all slots for a personnel on a specific day.
        {
            try
            {
                return await _db.AvailableSlots
                .Where(s => s.PersonnelId == personnelId && s.Day == day)
                .ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[AvailableSlotRepository] GetSlotsForPersonnelOnDayAsync({PersonnelId},{Day}) failed",
                    personnelId, day);
                return new List<AvailableSlot>();
            }

        }



        public async Task RemoveRangeAsync(IEnumerable<AvailableSlot> slots)
        {
            try
            {
                _db.AvailableSlots.RemoveRange(slots);
                await _db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[AvailableSlotRepository] RemoveRangeAsync failed");
                throw;
            }

        }

        public async Task<bool> ExistsAsync(int personnelId, DateOnly day, TimeOnly start, TimeOnly end)
        // Returns true if an identical slot (personnel, day, start, end) already exists.
        // Conservative: returns true on unexpected errors to avoid creating duplicates
        {
            try
            {
                return await _db.AvailableSlots.AnyAsync(s =>
                            s.PersonnelId == personnelId &&
                            s.Day == day &&
                            s.StartTime == start &&
                            s.EndTime == end);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[AvailableSlotRepository] ExistsAsync({PersonnelId},{Day},{Start},{End}) failed",
                    personnelId, day, start, end);
                // Be conservative: if we cannot check, assume it exists (prevents duplicates).
                return true;
            }
        }

    }
}
