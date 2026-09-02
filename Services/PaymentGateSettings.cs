using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Services
{
    public interface IPaymentGateSettings
    {
        /// <summary>Текущото състояние на всичките осем ключа.</summary>
        Task<Dictionary<string, bool>> GetAllAsync(CancellationToken ct = default);

        /// <summary>Включен ли е конкретен ключ. Липсващ ключ = включено.</summary>
        Task<bool> IsEnabledAsync(string gateKey, CancellationToken ct = default);

        Task SetAsync(string gateKey, bool enabled, string? changedBy,
                      CancellationToken ct = default);
    }

    /// <summary>
    /// "Payment Control" в админ панела — общ ключ, по метод и по крипто валута.
    /// Огледално на <see cref="Email.IEmailNotificationSettings"/>: ред на ключ,
    /// не колона, кеш до първата промяна, липсващ ключ се чете като включено.
    /// </summary>
    public sealed class PaymentGateSettings : IPaymentGateSettings
    {
        // MailComposer/AdminAuditFilter аналогия — регистрираме тази услуга
        // Singleton (четена е при всяка заявка към /Payment), а
        // ApplicationDbContext е Scoped, затова тук стои фабрика за scope,
        // не директно инжектиран контекст.
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<PaymentGateSettings> _logger;

        private Dictionary<string, bool>? _cache;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public static readonly string[] AllKeys =
        {
            "all",
            "method.card", "method.crypto", "method.iban",
            "currency.BTC", "currency.ETH", "currency.EURC", "currency.USDC"
        };

        public PaymentGateSettings(IServiceScopeFactory scopes, ILogger<PaymentGateSettings> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        public async Task<bool> IsEnabledAsync(string gateKey, CancellationToken ct = default)
        {
            try
            {
                var all = await GetAllAsync(ct);
                return !all.TryGetValue(gateKey, out var enabled) || enabled;
            }
            catch (Exception ex)
            {
                // Ако базата е недостъпна, по-добре плащанията да минат,
                // отколкото участник да остане блокиран заради странична грешка.
                _logger.LogError(ex,
                    "Не можах да прочета Payment Control ключ {Key}. Приемам, че е включен.", gateKey);
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

                var rows = await db.PaymentGateSettings.ToListAsync(ct);

                var missing = AllKeys.Where(k => !rows.Any(r => r.GateKey == k)).ToList();
                if (missing.Count > 0)
                {
                    foreach (var key in missing)
                    {
                        var row = new PaymentGateSetting { GateKey = key, IsEnabled = true };
                        db.PaymentGateSettings.Add(row);
                        rows.Add(row);
                    }
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "Създадени Payment Control ключове по подразбиране за: {Keys}", string.Join(", ", missing));
                }

                _cache = rows.ToDictionary(r => r.GateKey, r => r.IsEnabled, StringComparer.Ordinal);
                return _cache;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SetAsync(string gateKey, bool enabled, string? changedBy,
                                   CancellationToken ct = default)
        {
            if (!AllKeys.Contains(gateKey, StringComparer.Ordinal))
                throw new InvalidOperationException($"Unknown payment gate key: {gateKey}");

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var row = await db.PaymentGateSettings.FirstOrDefaultAsync(r => r.GateKey == gateKey, ct);
            if (row == null)
            {
                row = new PaymentGateSetting { GateKey = gateKey };
                db.PaymentGateSettings.Add(row);
            }

            row.IsEnabled     = enabled;
            row.LastChangedAt = DateTime.UtcNow;
            row.LastChangedBy = changedBy;

            await db.SaveChangesAsync(ct);

            _cache = null;

            _logger.LogInformation("Payment gate {Key} → {State} (от {Who})",
                gateKey, enabled ? "включен" : "изключен", changedBy ?? "—");
        }
    }
}
