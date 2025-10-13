using Homecare.Models;

namespace Homecare.DAL.Interfaces
{
    public interface IAppointmentRepository
    {
        Task<List<Appointment>> GetAllAsync();
        Task<Appointment?> GetAsync(int id);
        Task<List<Appointment>> GetByClientAsync(int clientId);
        Task<List<Appointment>> GetByPersonnelAsync(int personnelId);

        // ← burada tek imza kalsın
        Task<bool> SlotIsBookedAsync(int availableSlotId, int? ignoreAppointmentId = null);

        Task AddAsync(Appointment a);
        Task UpdateAsync(Appointment a);
        Task DeleteAsync(Appointment a);

        Task<int[]> GetTaskIdsAsync(int appointmentId);
        Task ReplaceTasksAsync(int appointmentId, IEnumerable<int> careTaskIds);
    }
}
