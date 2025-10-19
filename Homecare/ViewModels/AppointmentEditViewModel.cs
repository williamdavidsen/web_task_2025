using Homecare.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Homecare.ViewModels
{
    public class AppointmentEditViewModel
    {
        // Appointment being edited (form-bound)
        public Appointment Appointment { get; set; } = new Appointment();

        // Optional single-select task on edit
        public int? SelectedTaskId { get; set; }

        // Dropdown items for tasks
        public IEnumerable<SelectListItem> TaskSelectList { get; set; } = new List<SelectListItem>();
    }
}
