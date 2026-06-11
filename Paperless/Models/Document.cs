using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Paperless.Models.Enums;

namespace Paperless.Models
{
    public class Document
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Введіть назву документа")]
        [StringLength(200, ErrorMessage = "Назва не може перевищувати 200 символів")]
        [Display(Name = "Назва документа")]
        public string Title { get; set; } = string.Empty;

        [Display(Name = "Оригінальна назва файлу")]
        public string? OriginalFileName { get; set; }

        [Display(Name = "Короткий опис")]
        public string? Description { get; set; }

        [Display(Name = "Файл")]
        public string? FilePath { get; set; }

        [Display(Name = "Статус")]
        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        [Display(Name = "Дата створення")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Display(Name = "Дата оновлення")]
        public DateTime? UpdatedAt { get; set; }

        [Required]
        public string AuthorId { get; set; } = string.Empty;

        [ForeignKey("AuthorId")]
        [Display(Name = "Автор")]
        public virtual ApplicationUser? Author { get; set; }
    }
}