using System.ComponentModel.DataAnnotations;

namespace Paperless.Models.ViewModels
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Електронна пошта є обов'язковою")]
        [EmailAddress(ErrorMessage = "Некоректний формат email")]
        [Display(Name = "Електронна пошта (Логін)")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ім'я є обов'язковим")]
        [Display(Name = "Ім'я")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Прізвище є обов'язковим")]
        [Display(Name = "Прізвище")]
        public string LastName { get; set; } = string.Empty;

        [Display(Name = "Посада")]
        public string? Position { get; set; }

        [Display(Name = "Відділ")]
        public string? Department { get; set; }

        [Required(ErrorMessage = "Пароль є обов'язковим")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Підтвердження пароля є обов'язковим")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Паролі не збігаються")]
        [Display(Name = "Підтвердження пароля")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}