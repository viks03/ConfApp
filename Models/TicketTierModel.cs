// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class TicketTierModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string NameEn { get; set; } = string.Empty;

        [Required]
        public string NameBg { get; set; } = string.Empty;

        [Required]
        public string DescriptionEn { get; set; } = string.Empty;

        [Required]
        public string DescriptionBg { get; set; } = string.Empty;

        [Required]
        public string RegularPriceEn { get; set; } = string.Empty;

        [Required]
        public string RegularPriceBg { get; set; } = string.Empty;

        public string? PromoPriceEn { get; set; }

        public string? PromoPriceBg { get; set; }

        // ── The numeric prices ────────────────────────────────────────────
        // The strings above are for display only and can say anything ("Free",
        // "Напълно субсидиран", "€100 (вкл. ДДС)"). Charging happens ONLY on the
        // numbers here — see Services/Payments/TicketPricing.cs.
        // null means the tier is not payable and /Payment is never reached for
        // it.
        public decimal? RegularPriceEUR { get; set; }

        public decimal? PromoPriceEUR { get; set; }

        // ── The stable key ────────────────────────────────────────────────
        // Ties the participation form and the /Payment/{slug} address to the
        // tier without depending on NameEn (which the panel can rename) or on
        // Id (which is just a row number).
        [Required]
        public string TierKey { get; set; } = string.Empty;

        [Required]
        public string PerksEn { get; set; } = string.Empty;

        [Required]
        public string PerksBg { get; set; } = string.Empty;
    }
}