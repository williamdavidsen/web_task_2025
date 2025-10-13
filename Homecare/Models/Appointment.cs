using System.ComponentModel.DataAnnotations;

namespace Homecare.Models
{
    public class Appointment
    {
        public int AppointmentId { get; set; }

        [Required] public int AvailableSlotId { get; set; }  // FK -> AvailableSlot
        [Required] public int ClientId { get; set; }         // FK -> User (Client)

        [Required] public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // navs
        public AvailableSlot? AvailableSlot { get; set; }
        public User? Client { get; set; }
        public ICollection<TaskList> Tasks { get; set; } = new List<TaskList>();
    }
}
