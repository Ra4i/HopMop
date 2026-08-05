using System.ComponentModel.DataAnnotations;

namespace HopMop.Models
{
    public class AdminUser
    {
        public int Id { get; set; }
        [Required, EmailAddress]
        public string Email { get; set; }
        public string PasswordHash { get; set; }
    }
}
