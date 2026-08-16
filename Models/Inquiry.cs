using System.ComponentModel.DataAnnotations;

namespace HopMop.Models
{
    public class Inquiry
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        [Required, EmailAddress]
        public string Email { get; set; }
        public string Phone { get; set; }
        [Required]
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Resolved inquiries stay in the database — they are only hidden from
        // the active list and moved to the "completed" view.
        public bool IsResolved { get; set; } = false;
        public DateTime? ResolvedAt { get; set; }
    }
}
