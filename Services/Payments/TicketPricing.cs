// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Globalization;
using System.Text.RegularExpressions;
using ConferenceApp.Models;

namespace ConferenceApp.Services.Payments
{
    // ════════════════════════════════════════════════════════════════
    // TicketPricing — the single place that answers "which tier" and "how
    // much".
    //
    // The tier used to be chosen by three different rules (slug, Id == 2,
    // partial match on the name), and the amount was pulled out of the display
    // string by two different regular expressions, with a fallback of 120 in
    // five places. From here on: the key picks the tier, the numeric column
    // gives the amount, and a missing price is a refusal rather than a fallback
    // number.
    //
    // The class is static on purpose — it keeps no state and wants nothing from
    // DI. The caller loads the tiers (usually it has them already) and passes
    // them in.
    // ════════════════════════════════════════════════════════════════
    public static class TicketPricing
    {
        // The TicketTierModel.TierKey values written by the seed.
        public const string KeyViewer    = "viewer";
        public const string KeyEarlyBird = "earlybird";
        public const string KeyStudent   = "student";

        /// <summary>
        /// Participation form (ApplicationUser.PartForm) → tier key.
        /// <para>
        /// "2" (student) and "4" (journalist) are deliberately absent — they do
        /// not pay, they are cleared through verification and end up with
        /// PaymentMethod = "Subsidised". For them <see cref="ForUser"/> returns
        /// null rather than the regular tier.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, string> PartFormToTierKey = new()
        {
            ["1"] = KeyEarlyBird,   // Lector / Academic
            ["3"] = KeyEarlyBird,   // Online participant — there is no separate tier
        };

        /// <summary>The tier by key. The key survives a rename from the admin
        /// panel; the name does not.</summary>
        public static TicketTierModel? ByKey(IEnumerable<TicketTierModel> tiers, string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return tiers.FirstOrDefault(t =>
                string.Equals(t.TierKey, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The tier from the /Payment/{slug} segment. By key first, then by a
        /// slug of the English name, so that older links keep working. There is
        /// no "Id == 2" fallback: an unknown slug returns null.
        /// </summary>
        public static TicketTierModel? BySlug(IEnumerable<TicketTierModel> tiers, string? slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return null;

            var list = tiers as IList<TicketTierModel> ?? tiers.ToList();
            return ByKey(list, slug)
                ?? list.FirstOrDefault(t => Slugify(t.NameEn) == Slugify(slug));
        }

        /// <summary>
        /// The tier a given participant owes, by participation form.
        /// null means "owes nothing", not "could not work it out".
        /// </summary>
        public static TicketTierModel? ForUser(IEnumerable<TicketTierModel> tiers, ApplicationUser user)
        {
            if (user.PartForm == null) return null;
            return PartFormToTierKey.TryGetValue(user.PartForm, out var key)
                ? ByKey(tiers, key)
                : null;
        }

        /// <summary>
        /// The amount due in euro — the promotional price when there is one,
        /// otherwise the regular one. null means the tier is not payable (free
        /// or subsidised).
        /// </summary>
        public static decimal? PriceEUR(TicketTierModel? tier)
        {
            if (tier == null) return null;
            var price = tier.PromoPriceEUR ?? tier.RegularPriceEUR;
            return price is > 0m ? price : null;
        }

        /// <summary>Whether the tier is payable at all — that is, whether
        /// /Payment makes sense for it.</summary>
        public static bool IsPayable(TicketTierModel? tier) => PriceEUR(tier).HasValue;

        /// <summary>The amount in cents, for Stripe, which works in integers
        /// only.</summary>
        public static long ToCents(decimal amountEUR) =>
            (long)Math.Round(amountEUR * 100m, MidpointRounding.AwayFromZero);

        /// <summary>
        /// The amount as it goes into a mail or a log line. Always
        /// InvariantCulture — a record of money must not change shape with the
        /// language of whoever pressed the button. A missing amount is "—".
        /// </summary>
        public static string Format(decimal? amountEUR) =>
            amountEUR.HasValue
                ? amountEUR.Value.ToString("F2", CultureInfo.InvariantCulture) + " EUR"
                : "—";

        /// <summary>
        /// The /Payment/{slug} segment for a tier. Built from the key, not the
        /// name, so that a rename in the admin panel no longer breaks the link.
        /// </summary>
        public static string SlugFor(TicketTierModel tier) =>
            !string.IsNullOrWhiteSpace(tier.TierKey) ? tier.TierKey : Slugify(tier.NameEn);

        /// <summary>English name → slug. Kept for compatibility with the older
        /// addresses.</summary>
        public static string Slugify(string? phrase)
        {
            if (string.IsNullOrEmpty(phrase)) return string.Empty;
            var str = phrase.ToLowerInvariant();
            str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
            str = Regex.Replace(str, @"\s+", " ").Trim();
            return str.Replace(" ", "-");
        }
    }
}
