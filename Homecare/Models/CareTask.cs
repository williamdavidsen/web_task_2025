using System.ComponentModel.DataAnnotations;

namespace Homecare.Models
{

    public class CareTask
    {
        public int CareTaskId { get; set; }

        [Required, StringLength(300)]
        public string Description { get; set; } = string.Empty;

        // nav
        public ICollection<TaskList> TaskLinks { get; set; } = new List<TaskList>();
    }
}
