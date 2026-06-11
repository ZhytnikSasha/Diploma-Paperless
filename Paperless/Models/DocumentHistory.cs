using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Paperless.Models.Enums;

namespace Paperless.Models
{
    public class DocumentHistory
    {
        [Key]
        public int Id { get; set; }

        public int? DocumentId { get; set; }
        [Required]
        [Display(Name = "Назва документа")]
        public string DocumentTitle { get; set; } = string.Empty;

        [ForeignKey("DocumentId")]
        public virtual Document? Document { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [Display(Name = "Попередній статус")]
        public DocumentStatus? OldStatus { get; set; }

        [Required]
        [Display(Name = "Новий статус")]
        public DocumentStatus NewStatus { get; set; }

        [Display(Name = "Дата та час зміни")]
        public DateTime ChangedAt { get; set; } = DateTime.Now;
    }
}