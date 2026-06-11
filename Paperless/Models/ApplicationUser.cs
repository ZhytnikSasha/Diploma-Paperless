using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Paperless.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required(ErrorMessage = "Поле 'Ім'я' є обов'язковим")]
        [Display(Name = "Ім'я")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Поле 'Прізвище' є обов'язковим")]
        [Display(Name = "Прізвище")]
        public string LastName { get; set; } = string.Empty;

        [Display(Name = "Посада")]
        public string? Position { get; set; }

        [Display(Name = "Відділ")]
        public string? Department { get; set; }
        public string FullName => $"{LastName} {FirstName}";
        public string? AvatarPath { get; set; }
    }
}