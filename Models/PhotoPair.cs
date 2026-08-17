using System.ComponentModel.DataAnnotations;

namespace HopMop.Models
{
    public class PhotoPair
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [StringLength(260)]
        public string BeforeImagePath { get; set; } = string.Empty;

        [Required]
        [StringLength(260)]
        public string AfterImagePath { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
