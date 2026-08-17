using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HopMop.Models
{
    public class Inquiry
    {
        // The contact form posts straight into this model. [BindNever] keeps the
        // bookkeeping fields out of model binding — otherwise anyone could add
        // Id/IsResolved/CreatedAt to the form body and, for example, file an
        // inquiry that is already marked resolved or collides with a real row.
        [BindNever]
        public int Id { get; set; }

        // Lengths are enforced so a script cannot post megabytes of text into
        // the inquiry table. They also become the database column limits.
        [Required(ErrorMessage = "Моля, въведете вашето име.")]
        [StringLength(100, ErrorMessage = "Името може да е до 100 символа.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Моля, въведете email адрес.")]
        [EmailAddress(ErrorMessage = "Този email адрес не изглежда валиден.")]
        [StringLength(254, ErrorMessage = "Email адресът може да е до 254 символа.")]
        public string Email { get; set; } = string.Empty;

        // Optional, and declared nullable so an empty form field binds to null —
        // [Phone] rejects an empty string, which would make the field effectively
        // required. It is written to the database as "" rather than null, because
        // databases created before this change have Phone as NOT NULL.
        [Phone(ErrorMessage = "Този телефонен номер не изглежда валиден.")]
        [StringLength(32, ErrorMessage = "Телефонният номер може да е до 32 символа.")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Моля, опишете от какво имате нужда.")]
        [StringLength(4000, ErrorMessage = "Съобщението може да е до 4000 символа.")]
        public string Message { get; set; } = string.Empty;

        [BindNever]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Resolved inquiries stay in the database — they are only hidden from
        // the active list and moved to the "completed" view.
        [BindNever]
        public bool IsResolved { get; set; } = false;

        [BindNever]
        public DateTime? ResolvedAt { get; set; }
    }
}
