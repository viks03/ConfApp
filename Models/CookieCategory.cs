using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // Една категория в cookie preferences модала (виж Pages/Shared/_DataNotice.cshtml).
    // 4-те стандартни категории (Necessary/Analytics/Marketing/Preferences) са
    // seed-нати с IsBuiltIn = true, но админът може да добавя произволно нови.
    public class CookieCategory
    {
        [Key]
        public int Id { get; set; }

        // Стабилен slug, използван от JS/логиката за референция — "necessary",
        // "analytics" и т.н. Никога не се показва на посетителя директно.
        [Required]
        public string Key { get; set; } = string.Empty;

        [Required]
        public string NameEn { get; set; } = string.Empty;
        [Required]
        public string NameBg { get; set; } = string.Empty;

        public string DescriptionEn { get; set; } = string.Empty;
        public string DescriptionBg { get; set; } = string.Empty;

        // Дали категорията изобщо се показва в preferences модала.
        public bool IsVisible { get; set; } = true;

        // Дали посетителят може сам да я switch-не — "Strictly Necessary" е
        // единствената, за която това ВИНАГИ трябва да е false (виж server-side
        // guard в Index.cshtml.cs — Key == "necessary" force-ва това дори ако
        // някой сгреши/пипне през DevTools).
        public bool IsToggleable { get; set; } = true;

        // Стойност по подразбиране, когато посетителят все още не е избрал
        // нищо (или когато категорията не е toggleable — тогава това е
        // фиксираната ѝ стойност).
        public bool DefaultOn { get; set; } = false;

        // Разграничава 4-те seed-нати категории от custom добавени от админа —
        // предимно за да пазим "necessary" от изтриване.
        public bool IsBuiltIn { get; set; } = false;

        public int DisplayOrder { get; set; }
    }
}