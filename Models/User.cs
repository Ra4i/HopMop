using System.ComponentModel.DataAnnotations;

namespace HopMop.Models
{
    public class User
    {
        public int Id { get; set; }
        [Required, EmailAddress]
        public string Email { get; set; } = null!;

        [Required]
        public string PasswordHash { get; set; } = null!;

        [Required]
        public bool IsAdmin { get; set; } = false;
    }
}
