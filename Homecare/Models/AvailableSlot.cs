using System.ComponentModel.DataAnnotations;

namespace Homecare.Models
{
    // Personel bir gün için somut saat aralığı açar (sabah/öğlen/akşam preset)
    public class AvailableSlot
    {
        public int AvailableSlotId { get; set; }

        [Required] public int PersonnelId { get; set; }    // FK -> User (Personnel)
        [Required] public DateOnly Day { get; set; }
        [Required] public TimeOnly StartTime { get; set; }
        [Required] public TimeOnly EndTime { get; set; }

        // navs
        public User? Personnel { get; set; }
        public Appointment? Appointment { get; set; }      // 1 slot = 0..1 appointment
    }
}
