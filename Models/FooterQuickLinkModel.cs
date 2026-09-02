using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // Линковете в "Quick Links" колоната на публичния footer (виж
    // .footer-quicklinks в _Layout.cshtml). Показването на реда на сайта е
    // НАПЪЛНО РАНДОМНО при всяко зареждане на страницата (разбъркват се в
    // _Layout.cshtml) — затова, за разлика от PromoSlideModel/FaqModel,
    // тук НЯМА DisplayOrder поле; drag-to-reorder в admin панела нарочно
    // не съществува за този списък.
    //
    // IconSvg пази само вътрешната SVG маркировка (елементи като <path>,
    // <circle>, <line>...), НЕ цял <svg> таг — увива се автоматично в
    // стандартната 24×24, stroke-базирана икон рамка на сайта, както на
    // публичния footer, така и в preview-то на admin панела.
    public class FooterQuickLinkModel
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(60)]
        public string LabelEn { get; set; } = string.Empty;

        [Required, MaxLength(60)]
        public string LabelBg { get; set; } = string.Empty;

        [Required, MaxLength(300)]
        public string Url { get; set; } = string.Empty;

        // Без MaxLength на самото поле по избор (виж коментара на класа) —
        // но 2000 символа е разумен таван: най-сложната от seed-натите
        // икони е ~250 символа; 2000 дава щедър марж за по-детайлна
        // икона, но пази базата от случайно вмъкнат огромен SVG/base64
        // блок вместо простичка stroke икона.
        [Required, MaxLength(2000)]
        public string IconSvg { get; set; } = string.Empty;

        // Позволява временно скриване на линк от публичния footer без
        // изтриване на записа (виж .toggle-footerlink-active-btn).
        public bool IsVisible { get; set; } = true;
    }
}