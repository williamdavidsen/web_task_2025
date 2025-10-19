namespace Homecare.Models
{
    // 1=Client, 2=Personnel, 3=Admin
    public enum UserRole { Client = 1, Personnel = 2, Admin = 3 }
    public enum AppointmentStatus { Scheduled = 0, Completed = 1, Cancelled = 2 }
}
