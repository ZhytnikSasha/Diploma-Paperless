using System.ComponentModel.DataAnnotations;

namespace Paperless.Models.Enums
{
    public enum DocumentStatus
    {
        [Display(Name = "Чернетка")]
        Draft = 0,

        [Display(Name = "На розгляді")]
        PendingReview = 1,

        [Display(Name = "Затверджено")]
        Approved = 2,

        [Display(Name = "Відхилено")]
        Rejected = 3,

        [Display(Name = "Видалено")]
        Deleted = 4
    }
}