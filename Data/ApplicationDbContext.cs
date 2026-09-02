using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Data
{
    // ВАЖНО: Вече наследяваме с <ApplicationUser>, за да използваме разширения модел
    //
    // ОРГАНИЗАЦИЯ: този файл пази само DbSet декларациите — всеки index/seed
    // за конкретен модел живее в собствен IEntityTypeConfiguration<T> файл в
    // Data/Configurations/ (напр. FaqModelConfiguration.cs), приложени
    // накуп по-долу чрез ApplyConfigurationsFromAssembly. Нулева промяна в
    // поведение спрямо предишната версия (всичкия index/seed код просто се
    // премести, byte-for-byte същия) — само по-лесно за навигация, вместо
    // един метод от 300+ реда, който расте безконтролно с всяка нова функция.
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ── Конференция: съдържание и структура ──────────────────────────────
        public DbSet<TicketTierModel> TicketTiers => Set<TicketTierModel>();
        public DbSet<LecturerModel> Lecturers => Set<LecturerModel>();
        public DbSet<EventModel> Events => Set<EventModel>();
        public DbSet<CommitteeMemberModel> CommitteeMembers => Set<CommitteeMemberModel>();
        public DbSet<PartnerModel> Partners => Set<PartnerModel>();
        public DbSet<ScheduleModel> Schedule => Set<ScheduleModel>();
        public DbSet<HotelModel> Hotels { get; set; }
        public DbSet<LinkWatch> LinkWatches { get; set; }
        public DbSet<HomePageLogo> HomePageLogos { get; set; }
        public DbSet<ConferenceSetting> ConferenceSettings { get; set; }

        /// <summary>Включване/изключване на отделните видове имейл известия.</summary>
        public DbSet<EmailNotificationSetting> EmailNotificationSettings { get; set; }

        /// <summary>Payment Control — общ ключ, по метод на плащане и по крипто валута.</summary>
        public DbSet<PaymentGateSetting> PaymentGateSettings { get; set; }
        public DbSet<FaqModel> Faqs { get; set; }

        // ── Плащания и одит ───────────────────────────────────────────────────
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<OtpCode> OtpCodes { get; set; }
        public DbSet<CryptoOrder> CryptoOrders { get; set; }

        // ── Настройки за социални мрежи (singleton) + промо слайдове за
        //    мобилното навигационно меню (виж .mobile-nav-promo-slider) ──────
        public DbSet<SocialLinksSetting> SocialLinksSettings { get; set; }
        public DbSet<PromoSlideModel> PromoSlides { get; set; }

        // ── Footer съдържание (singleton — tagline/org note/контакти) +
        //    Quick Links (виж .footer-quicklinks в _Layout.cshtml — редът
        //    на показване на сайта е рандомен, затова моделът няма
        //    DisplayOrder поле, за разлика от PromoSlideModel/FaqModel) ────
        public DbSet<FooterContent> FooterContents { get; set; }
        public DbSet<FooterQuickLinkModel> FooterQuickLinks { get; set; }

        // ── Send Invitations — лог за всеки опит за изпращане (успешен/
        //    неуспешен), захранва History таба ──────────────────────────────
        public DbSet<InvitationSendLog> InvitationSendLogs { get; set; }

        // ── Bug Reports — подадени от плаващия widget, захранва
        //    /Admin/BugReports ────────────────────────────────────────────────
        public DbSet<BugReport> BugReports { get; set; }

        // ── Privacy Policy / GDPR — singleton, редактируем от админ панела ──
        public DbSet<PrivacyPolicyContent> PrivacyPolicyContents { get; set; }

        // ── Terms of Use — singleton, редактируем от админ панела (огледално
        //    на PrivacyPolicyContent) ──────────────────────────────────────
        public DbSet<TermsOfUseContent> TermsOfUseContents { get; set; }

        // ── Cookie Notice — категории (Necessary/Analytics/Marketing/
        //    Preferences + custom добавени) и главния текст на банера ───────
        public DbSet<CookieCategory> CookieCategories { get; set; }
        public DbSet<CookieNoticeContent> CookieNoticeContents { get; set; }
        public DbSet<CookiePolicyContent> CookiePolicyContents { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Всеки index/seed за конкретен модел живее в собствен
            // IEntityTypeConfiguration<T> файл — виж Data/Configurations/.
            builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        }
    }
}