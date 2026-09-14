// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Data
{
    // Identity is parameterised with <ApplicationUser>, which is what makes the
    // extra participant columns part of the user table.
    //
    // This file holds the DbSet declarations and nothing else: the indexes and
    // the seed data for each model live in their own
    // IEntityTypeConfiguration<T> file under Data/Configurations/ (for example
    // FaqModelConfiguration.cs) and are applied together at the bottom through
    // ApplyConfigurationsFromAssembly. The alternative was one method of 300+
    // lines that grew with every new feature.
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ── Conference content and structure ─────────────────────────────────
        public DbSet<TicketTierModel> TicketTiers => Set<TicketTierModel>();
        public DbSet<LecturerModel> Lecturers => Set<LecturerModel>();
        public DbSet<EventModel> Events => Set<EventModel>();
        public DbSet<CommitteeMemberModel> CommitteeMembers => Set<CommitteeMemberModel>();
        public DbSet<PartnerModel> Partners => Set<PartnerModel>();
        public DbSet<ScheduleModel> Schedule => Set<ScheduleModel>();
        public DbSet<HotelModel> Hotels { get; set; }
        public DbSet<LinkWatch> LinkWatches { get; set; }
        public DbSet<HomePageLogo> HomePageLogos { get; set; }

        /// <summary>Switches the individual kinds of mail notification on and
        /// off.</summary>
        public DbSet<EmailNotificationSetting> EmailNotificationSettings { get; set; }

        /// <summary>Payment Control — the master switch, one key per payment
        /// method and one per crypto currency.</summary>
        public DbSet<PaymentGateSetting> PaymentGateSettings { get; set; }
        public DbSet<FaqModel> Faqs { get; set; }

        // ── Per-page visual styles (the "Стилове" tab in the panel) ──────
        public DbSet<PageStyleSetting>  PageStyleSettings  => Set<PageStyleSetting>();
        public DbSet<CustomBackground>  CustomBackgrounds  => Set<CustomBackground>();
        public DbSet<PageStyleRevision> PageStyleRevisions => Set<PageStyleRevision>();

        /// <summary>The programme and the documents for authors, uploaded from
        /// the panel. The rows are fixed; see DownloadableFile.</summary>
        public DbSet<DownloadableFile> DownloadableFiles => Set<DownloadableFile>();

        /// <summary>The site themes. No active theme means "exactly as the CSS
        /// file says".</summary>
        public DbSet<SiteTheme> SiteThemes => Set<SiteTheme>();

        // ── Payments and audit ────────────────────────────────────────────────
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<OtpCode> OtpCodes { get; set; }
        public DbSet<CryptoOrder> CryptoOrders { get; set; }

        // ── Social links (a single row) and the promo slides for the mobile
        //    navigation menu (see .mobile-nav-promo-slider) ─────────────────
        public DbSet<SocialLinksSetting> SocialLinksSettings { get; set; }
        public DbSet<PromoSlideModel> PromoSlides { get; set; }

        // ── Footer content (a single row: tagline, org note, contacts) and
        //    the Quick Links (see .footer-quicklinks in _Layout.cshtml — they
        //    are shuffled on every page load, which is why the model has no
        //    DisplayOrder column, unlike PromoSlideModel and FaqModel) ─────
        public DbSet<FooterContent> FooterContents { get; set; }
        public DbSet<FooterQuickLinkModel> FooterQuickLinks { get; set; }

        // ── Send Invitations — one row per attempt, successful or not; this
        //    is what the History tab reads ─────────────────────────────────
        public DbSet<InvitationSendLog> InvitationSendLogs { get; set; }

        // ── Bug reports from the floating widget; this is what
        //    /Admin/BugReports reads ─────────────────────────────────────────
        public DbSet<BugReport> BugReports { get; set; }

        // ── Privacy Policy / GDPR — a single row, edited from the panel ─────
        public DbSet<PrivacyPolicyContent> PrivacyPolicyContents { get; set; }

        // ── Terms of Use — a single row, edited from the panel; mirrors
        //    PrivacyPolicyContent ─────────────────────────────────────────
        public DbSet<TermsOfUseContent> TermsOfUseContents { get; set; }

        // ── Cookie notice — the categories (Necessary, Analytics, Marketing,
        //    Preferences, plus any added by an administrator) and the texts of
        //    the banner and of the /Cookies page ────────────────────────────
        public DbSet<CookieCategory> CookieCategories { get; set; }
        public DbSet<CookieNoticeContent> CookieNoticeContents { get; set; }
        public DbSet<CookiePolicyContent> CookiePolicyContents { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // One row per page, and a unique slug: two rows for "/Index" would
            // make the read non-deterministic, and nothing else prevents them.
            builder.Entity<PageStyleSetting>().HasIndex(x => x.PageKey).IsUnique();
            builder.Entity<CustomBackground>().HasIndex(x => x.Slug).IsUnique();
            builder.Entity<DownloadableFile>().HasIndex(x => x.FileKey).IsUnique();
            builder.Entity<SiteTheme>().HasIndex(x => x.ThemeKey).IsUnique();
            builder.Entity<PageStyleRevision>().HasIndex(x => x.PageKey);

            // ApplicationUser has no IEntityTypeConfiguration file of its own.
            // The decimal is stored as TEXT: SQLite has no decimal type, and
            // REAL would round money.
            builder.Entity<ApplicationUser>().Property(u => u.PaidAmountEUR).HasColumnType("TEXT");

            // [D-03] ReferenceNumber is the key the Stripe and Go28 webhooks
            // find a person by, and in the schema it was a plain TEXT column
            // with no index. Uniqueness was enforced only by a check-then-write
            // in Register, with nothing in between: two concurrent registrations
            // that drew the same number both went through, and confirming one
            // person's payment then landed on the other.
            //
            // The index is FILTERED on purpose: accounts with an empty
            // ReferenceNumber do exist (see [A-09] — the generation has a catch
            // of its own), and an unfiltered UNIQUE would trip over them.
            builder.Entity<ApplicationUser>()
                   .HasIndex(u => u.ReferenceNumber)
                   .IsUnique()
                   .HasFilter("\"ReferenceNumber\" <> ''");

            // Everything else — one index/seed configuration per model — lives
            // in Data/Configurations/ and is applied in one call.
            builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        }
    }
}