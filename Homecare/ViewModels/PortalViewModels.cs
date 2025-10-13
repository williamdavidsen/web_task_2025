namespace Homecare.ViewModels
{
    using Homecare.Models;
    using Microsoft.AspNetCore.Mvc.Rendering;

    public class ClientDashboardVM
    {
        public int ClientId { get; set; }
        public List<Appointment> Upcoming { get; set; } = new();
        public List<Appointment> Past { get; set; } = new();
    }

    public class PersonnelDashboardVM
    {
        public int PersonnelId { get; set; }
        public List<Appointment> Upcoming { get; set; } = new();
        public List<Appointment> Past { get; set; } = new();
    }

    public class CreateAppointmentVM
    {
        public int ClientId { get; set; }
        public DateOnly? Day { get; set; }
        public int? AvailableSlotId { get; set; }
        public int[] SelectedTaskIds { get; set; } = Array.Empty<int>();
        // UI
        public List<SelectListItem>? DayOptions { get; set; }
        public List<SelectListItem>? SlotOptions { get; set; }
        public List<SelectListItem>? TaskOptions { get; set; }
        public string? InfoMessage { get; set; }
    }

    public class CreateDayVM
    {
        public int PersonnelId { get; set; }
        public DateOnly Day { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        public string? InfoMessage { get; set; }
    }
}
