using Homecare.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Homecare.ViewModels
{
    public class AppointmentCreateViewModel
    {
        // The appointment fields bound to the form
        public Appointment Appointment { get; set; } = new Appointment();

        // Optional single-select “Requested Task”
        public int? SelectedTaskId { get; set; }

        // Items for the tasks dropdown
        public IEnumerable<SelectListItem> TaskSelectList { get; set; } = new List<SelectListItem>();
    }
}
