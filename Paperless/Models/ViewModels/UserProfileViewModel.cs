using System.ComponentModel.DataAnnotations;

namespace Paperless.Models.ViewModels
{
    public class UserProfileViewModel
    {
        public string Id { get; set; } = string.Empty;

        [Display(Name = "Електронна пошта")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введіть ім'я")]
        [Display(Name = "Ім'я")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введіть прізвище")]
        [Display(Name = "Прізвище")]
        public string LastName { get; set; } = string.Empty;

        [Display(Name = "Посада")]
        public string? Position { get; set; }

        [Display(Name = "Відділ")]
        public string? Department { get; set; }

        [Display(Name = "Роль у системі")]
        public string Role { get; set; } = string.Empty;
        [Display(Name = "Змінити аватар")]
        public IFormFile? AvatarFile { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Поточний пароль")]
        public string? CurrentPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Новий пароль")]
        public string? NewPassword { get; set; }
        public string? AvatarPath { get; set; }
        public List<Document> MyDocuments { get; set; } = new List<Document>();
    }
}