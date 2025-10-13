using Homecare.Models;
using System.Collections.Generic;

namespace Homecare.ViewModels
{
    // Holds the full collections so the view can slice for paging.
    public class AdminDashboardViewModel
    {
        public List<Appointment> UpcomingAll { get; set; } = new();
        public List<AvailableSlot> FreeAll { get; set; } = new();
    }
}
