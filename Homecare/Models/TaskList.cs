namespace Homecare.Models
{
    // Appointment ↔ CareTask çok-çok ilişkisi
    public class TaskList
    {
        public int AppointmentId { get; set; }
        public int CareTaskId { get; set; }

        // navs
        public Appointment? Appointment { get; set; }
        public CareTask? CareTask { get; set; }
    }
}
