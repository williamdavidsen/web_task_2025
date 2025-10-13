using Homecare.Models;

namespace Homecare.DAL.Interfaces
{
    public interface IAvailableSlotRepository
    {
        Task<List<AvailableSlot>> GetAllAsync();
        Task<AvailableSlot?> GetAsync(int id);
        Task<List<DateOnly>> GetFreeDaysAsync(int rangeDays = 42);

        Task<List<AvailableSlot>> GetFreeSlotsByDayAsync(DateOnly day);
        Task AddAsync(AvailableSlot slot);
        Task AddRangeAsync(IEnumerable<AvailableSlot> slots);
        Task UpdateAsync(AvailableSlot slot);
        Task DeleteAsync(AvailableSlot slot);
        Task<bool> ExistsAsync(int personnelId, DateOnly day, TimeOnly start, TimeOnly end);
        // Belirli hemşirenin belirtilen aralıkta slotu bulunan günleri (distinct)
        Task<List<DateOnly>> GetWorkDaysAsync(int personnelId, DateOnly from, DateOnly to);

        // Belirli hemşirenin belirtilen aralıkta randevusu bulunan günleri (distinct, kilitli)
        Task<List<DateOnly>> GetLockedDaysAsync(int personnelId, DateOnly from, DateOnly to);

        Task<List<AvailableSlot>> GetSlotsForPersonnelOnDayAsync(int personnelId, DateOnly day);

        Task RemoveRangeAsync(IEnumerable<AvailableSlot> slots);
    }
}
