using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // Слайдовете в промо carousel-а на мобилното навигационно меню
    // (виж .mobile-nav-promo-slider в _Layout.cshtml/mainStyle.css).
    // DisplayOrder контролира реда в carousel-а — обновява се чрез
    // drag-and-drop в admin панела (виж OnPostReorderPromosAsync).
    public class PromoSlideModel
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string TitleEn { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string TitleBg { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string DescriptionEn { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string DescriptionBg { get; set; } = string.Empty;

        // Очаква се прозрачен PNG/SVG (виж .mobile-nav-promo-art —
        // изображението се central "contain"-ва в 3.3×3.3rem кутия, не
        // се кадрира) — препоръка за размер/формат се показва в самия
        // admin форм, не се налага тук на ниво модел.
        public string? ImagePath { get; set; }

        public int DisplayOrder { get; set; }

        // Позволява временно скриване на слайд от carousel-а без
        // изтриване на записа (и без изтриване на качения файл).
        public bool IsActive { get; set; } = true;
    }
}