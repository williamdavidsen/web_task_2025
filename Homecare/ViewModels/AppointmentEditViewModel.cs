using Homecare.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Homecare.ViewModels
{
    public class AppointmentEditViewModel
    {
        public Appointment Appointment { get; set; } = new Appointment();

        // Edit’te tek seçimlik görev
        public int? SelectedTaskId { get; set; }

        // Dropdown içeriği
        public IEnumerable<SelectListItem> TaskSelectList { get; set; } = new List<SelectListItem>();
    }
}
