using Homecare.DAL.Interfaces;
using Homecare.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Homecare.DAL.Repositories
{

    public class AppointmentRepository : IAppointmentRepository
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AppointmentRepository> _logger;

        public AppointmentRepository(AppDbContext db, ILogger<AppointmentRepository> logger)
        {
            _db = db;
            _logger = logger;
        }


        public async Task<List<Appointment>> GetAllAsync()
        {
            try
            {
                return await _db.Appointments
                    .Include(a => a.AvailableSlot)!.ThenInclude(s => s.Personnel)
                    .Include(a => a.Client)
                    .OrderBy(a => a.AvailableSlot!.Day)
                    .ThenBy(a => a.AvailableSlot!.StartTime)
                    .AsNoTracking()
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentRepository] GetAllAsync failed");
                return new List<Appointment>();
            }
        }




        public async Task<Appointment?> GetAsync(int id)
        // Returns a single appointment by id (or null) with related data loaded.
        {
            try
            {
                return await _db.Appointments
                    .Include(a => a.AvailableSlot)!.ThenInclude(s => s.Personnel)
                    .Include(a => a.Client)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.AppointmentId == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentRepository] GetAsync({Id}) failed", id);
                return null;
            }
        }


        public async Task<List<Appointment>> GetByClientAsync(int clientId)
        {
            try
            {
                return await _db.Appointments
                    .Include(a => a.AvailableSlot)!.ThenInclude(s => s.Personnel)
                    .Where(a => a.ClientId == clientId)
                    .AsNoTracking()
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentRepository] GetByClientAsync({ClientId}) failed", clientId);
                return new List<Appointment>();
            }
        }


        public async Task<List<Appointment>> GetByPersonnelAsync(int personnelId)
        {
            try
            {
                return await _db.Appointments
                    .Include(a => a.AvailableSlot)!.ThenInclude(s => s.Personnel)
                    .Where(a => a.AvailableSlot!.PersonnelId == personnelId)
                    .AsNoTracking()
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentRepository] GetByPersonnelAsync({PersonnelId}) failed", personnelId);
                return new List<Appointment>();
            }
        }



        public async Task<bool> SlotIsBookedAsync(int availableSlotId, int? ignoreId = null)
        // Checks if a slot already has an appointment. Optionally ignores a specific appointment id.

        {
            try
            {
                return await _db.Appointments.AnyAsync(a =>
                    a.AvailableSlotId == availableSlotId &&
                    (ignoreId == null || a.AppointmentId != ignoreId.Value));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentRepository] SlotIsBookedAsync({SlotId},{Ignore}) failed",
                    availableSlotId, ignoreId);
                // Be conservative: if we cannot verify, assume booked to avoid double-booking.
                return true;
            }
        }


        public async Task AddAsync(Appointment a)
        {
            try
            {
                _db.Appointments.Add(a);
                await _db.SaveChangesAsync();
                _logger.LogInformation("[AppointmentRepository] Added appointment #{Id} for client #{ClientId}",
                    a.AppointmentId, a.ClientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentRepository] AddAsync failed for client #{ClientId}", a.ClientId);
                throw;
            }
        }


        public async Task UpdateAsync(Appointment a)
        {
            try
            {
                _db.Appointments.Update(a);
                await _db.SaveChangesAsync();
                _logger.LogInformation("[AppointmentRepository] Updated appointment #{Id}", a.AppointmentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentRepository] UpdateAsync failed for #{Id}", a.AppointmentId);
                throw;
            }
        }


        public async Task DeleteAsync(Appointment a)
        {
            try
            {
                _db.Appointments.Remove(a);
                await _db.SaveChangesAsync();
                _logger.LogInformation("[AppointmentRepository] Deleted appointment #{Id}", a.AppointmentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentRepository] DeleteAsync failed for #{Id}", a.AppointmentId);
                throw;
            }
        }



        public async Task<int[]> GetTaskIdsAsync(int appointmentId)
        // Returns CareTask ids currently attached to an appointment.
        {
            try
            {
                return await _db.TaskLists
                    .Where(t => t.AppointmentId == appointmentId)
                    .Select(t => t.CareTaskId)
                    .ToArrayAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentRepository] GetTaskIdsAsync({Id}) failed", appointmentId);
                return Array.Empty<int>();
            }
        }




        public async Task ReplaceTasksAsync(int appointmentId, IEnumerable<int> careTaskIds)
        // Replaces all tasks of an appointment with the given set (clears then inserts).
        {
            try
            {
                var existing = _db.TaskLists.Where(x => x.AppointmentId == appointmentId);
                _db.TaskLists.RemoveRange(existing);

                var add = careTaskIds
                    .Distinct()
                    .Select(id => new TaskList
                    {
                        AppointmentId = appointmentId,
                        CareTaskId = id
                    });

                await _db.TaskLists.AddRangeAsync(add);
                await _db.SaveChangesAsync();

                _logger.LogInformation("[AppointmentRepository] Replaced tasks for appointment #{Id}", appointmentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AppointmentRepository] ReplaceTasksAsync({Id}) failed", appointmentId);
                throw;
            }
        }
    }
}
