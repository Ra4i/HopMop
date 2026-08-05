using System.ComponentModel.DataAnnotations;

namespace HopMop.Models
{
    public class PhotoPair
    {
        public int Id { get; set; }
        [Required]
        public string Title { get; set; }
        public string Description { get; set; }
        [Required]
        public string BeforeImagePath { get; set; }
        [Required]
        public string AfterImagePath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
