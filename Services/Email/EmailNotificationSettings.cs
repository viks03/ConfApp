using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Services.Email
{
    public interface IEmailNotificationSettings
    {
        /// <summary>Включен ли е този вид имейл. OTP винаги връща true.</summary>
        Task<bool> IsEnabledAsync(EmailTemplate template, CancellationToken ct = default);

        /// <summary>Текущото състояние на всички превключваеми видове.</summary>
        Task<Dictionary<string, bool>> GetAllAsync(CancellationToken ct = default);

        Task SetAsync(EmailTemplate template, bool enabled, string? changedBy,
                      CancellationToken ct = default);
    }

    public sealed class EmailNotificationSettings : IEmailNotificationSettings
    {
        // MailComposer е Singleton, а ApplicationDbContext е Scoped. Затова тук
        // не се инжектира контекст, а фабрика за scope — при всяко четене се
        // отваря собствен кратък scope. Директното инжектиране би хвърлило
        // "Cannot consume scoped service from singleton" при стартиране.
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<EmailNotificationSettings> _logger;

        // Настройките се пипат рядко, а се четат при всеки имейл — кешираме ги
        // и изхвърляме кеша при запис.
        private Dictionary<string, bool>? _cache;
        private readonly SemaphoreSlim _lock = new(1, 1);

        /// <summary>
        /// Видовете, които администраторът може да изключва.
        /// <para>
        /// <see cref="EmailTemplate.Otp"/> НЕ е тук нарочно — това е кодът за
        /// регистрация и вход. Изключването му би направило сайта неизползваем.
        /// </para>
        /// </summary>
        public static readonly EmailTemplate[] Switchable =
        {
            EmailTemplate.PaymentConfirmed,
            EmailTemplate.PaymentPending,
            EmailTemplate.VerificationApproved,
            EmailTemplate.VerificationRejected,
            EmailTemplate.StatusChanged
        };

        public EmailNotificationSettings(
            IServiceScopeFactory scopes,
            ILogger<EmailNotificationSettings> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        public async Task<bool> IsEnabledAsync(EmailTemplate template, CancellationToken ct = default)
        {
            // Задължителните не се проверяват — няма как да бъдат изключени.
            if (!Switchable.Contains(template)) return true;

            try
            {
                var all = await GetAllAsync(ct);
                return !all.TryGetValue(template.ToString(), out var enabled) || enabled;
            }
            catch (Exception ex)
            {
                // Ако базата е недостъпна, по-добре имейлът да тръгне, отколкото
                // потребителят да остане без потвърждение за платена такса.
                _logger.LogError(ex,
                    "Не можах да прочета настройката за {Template}. Приемам, че е включена.",
                    template);
                return true;
            }
        }

        public async Task<Dictionary<string, bool>> GetAllAsync(CancellationToken ct = default)
        {
            if (_cache != null) return _cache;

            await _lock.WaitAsync(ct);
            try
            {
                if (_cache != null) return _cache;

                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var rows = await db.EmailNotificationSettings.ToListAsync(ct);

                // Липсващите редове се създават сега, включени по подразбиране.
                // Така нов вид имейл не изисква миграция или seed скрипт.
                var missing = Switchable
                    .Select(t => t.ToString())
                    .Where(k => !rows.Any(r => r.TemplateKey == k))
                    .ToList();

                if (missing.Count > 0)
                {
                    foreach (var key in missing)
                    {
                        var row = new EmailNotificationSetting { TemplateKey = key, IsEnabled = true };
                        db.EmailNotificationSettings.Add(row);
                        rows.Add(row);
                    }
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "Създадени настройки по подразбиране за: {Keys}", string.Join(", ", missing));
                }

                _cache = rows.ToDictionary(r => r.TemplateKey, r => r.IsEnabled, StringComparer.Ordinal);
                return _cache;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SetAsync(EmailTemplate template, bool enabled, string? changedBy,
                                   CancellationToken ct = default)
        {
            if (!Switchable.Contains(template))
                throw new InvalidOperationException(
                    $"{template} е задължителен и не може да се изключва.");

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var key = template.ToString();
            var row = await db.EmailNotificationSettings
                              .FirstOrDefaultAsync(r => r.TemplateKey == key, ct);

            if (row == null)
            {
                row = new EmailNotificationSetting { TemplateKey = key };
                db.EmailNotificationSettings.Add(row);
            }

            row.IsEnabled     = enabled;
            row.LastChangedAt = DateTime.UtcNow;
            row.LastChangedBy = changedBy;

            await db.SaveChangesAsync(ct);

            InvalidateCache();

            _logger.LogInformation("Имейл известие {Template} → {State} (от {Who})",
                template, enabled ? "включено" : "изключено", changedBy ?? "—");
        }

        private void InvalidateCache() => _cache = null;
    }
}
