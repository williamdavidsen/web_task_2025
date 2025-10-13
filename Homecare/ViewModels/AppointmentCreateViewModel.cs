using Homecare.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Homecare.ViewModels
{
    public class AppointmentCreateViewModel
    {
        // Formdaki randevu alanları
        public Appointment Appointment { get; set; } = new Appointment();

        // Tek seçimlik “Requested Task” için
        public int? SelectedTaskId { get; set; }

        // Dropdown içeriği
        public IEnumerable<SelectListItem> TaskSelectList { get; set; } = new List<SelectListItem>();
    }
}
