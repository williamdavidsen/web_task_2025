using System.ComponentModel.DataAnnotations;

namespace Homecare.Models
{
    public class User
    {
        public int UserId { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(200)]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(256)]
        public string PasswordHash { get; set; } = "changeme";

        [Required]
        public UserRole Role { get; set; } = UserRole.Client;

        // navs
        public ICollection<AvailableSlot> AvailableSlotsAsPersonnel { get; set; } = new List<AvailableSlot>();
        public ICollection<Appointment> AppointmentsAsClient { get; set; } = new List<Appointment>();
    }
}
