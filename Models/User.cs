using System.ComponentModel.DataAnnotations;

namespace HopMop.Models
{
    public class User
    {
        public int Id { get; set; }

        // Stored lower-cased so "Ivan@x.bg" and "ivan@x.bg" cannot become two
        // separate accounts — PostgreSQL compares text case-sensitively, so the
        // unique index alone would not catch them.
        [Required, EmailAddress]
        [StringLength(254)]
        public string Email { get; set; } = null!;

        [Required]
        [StringLength(400)]
        public string PasswordHash { get; set; } = null!;

        [Required]
        public bool IsAdmin { get; set; } = false;
    }
}
