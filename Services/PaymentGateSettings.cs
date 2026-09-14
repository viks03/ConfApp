// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Services
{
    public interface IPaymentGateSettings
    {
        /// <summary>The current state of all eight keys.</summary>
        Task<Dictionary<string, bool>> GetAllAsync(CancellationToken ct = default);

        /// <summary>Whether one key is switched on. A missing key counts as
        /// switched on.</summary>
        Task<bool> IsEnabledAsync(string gateKey, CancellationToken ct = default);

        Task SetAsync(string gateKey, bool enabled, string? changedBy,
                      CancellationToken ct = default);
    }

    /// <summary>
    /// "Payment Control" in the admin panel — a master switch, one per method
    /// and one per crypto currency. Mirrors
    /// <see cref="Email.IEmailNotificationSettings"/>: a row per key rather than
    /// a column, cached until the first change, a missing key read as switched
    /// on.
    /// </summary>
    public sealed class PaymentGateSettings : IPaymentGateSettings
    {
        // The service is a singleton, because it is read on every request to
        // /Payment, while ApplicationDbContext is scoped — hence a scope factory
        // here rather than an injected context.
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<PaymentGateSettings> _logger;

        private Dictionary<string, bool>? _cache;
        private readonly SemaphoreSlim _lock = new(1, 1);

        // The master key is checked first and short-circuits the rest; the
        // per-currency keys are only consulted once method.crypto is on.
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
                // With the database unreachable it is better for payments to go
                // through than for a participant to be blocked by an unrelated
                // fault.
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
                    }

                    try
                    {
                        await db.SaveChangesAsync(ct);
                        _logger.LogInformation(
                            "Създадени Payment Control ключове по подразбиране за: {Keys}", string.Join(", ", missing));
                    }
                    catch (DbUpdateException ex)
                    {
                        // [D-04] The unique index on GateKey no longer allows two
                        // rows for one key. If another process inserted the same
                        // key between the read and the write, the insert fails —
                        // and that must not look like "cannot read the setting",
                        // because that is read as "payments are on". Read again
                        // and carry on.
                        _logger.LogWarning(ex,
                            "Друг процес вече е създал Payment Control ключовете за: {Keys}. Прочитам наново.",
                            string.Join(", ", missing));
                    }

                    db.ChangeTracker.Clear();
                    rows = await db.PaymentGateSettings.ToListAsync(ct);
                }

                _cache = BuildMap(rows);
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

            // [D-04] The same semaphore GetAllAsync holds. The write used to
            // happen outside it — a second writer inserted a second row for the
            // same key, every read failed from then on, and a failed read here
            // means "payments stay on".
            await _lock.WaitAsync(ct);
            try
            {
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
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation("Payment gate {Key} → {State} (от {Who})",
                gateKey, enabled ? "включен" : "изключен", changedBy ?? "—");
        }

        /// <summary>
        /// [D-04] This used to be a plain <c>ToDictionary</c> — two rows for one
        /// key threw <see cref="ArgumentException"/>, which is worse here than it
        /// is for mail: <see cref="IsEnabledAsync"/> swallows the exception and
        /// returns <c>true</c>, so payments that had been switched off switched
        /// themselves back on, silently. On a duplicate the most recently changed
        /// row wins.
        /// </summary>
        private Dictionary<string, bool> BuildMap(List<PaymentGateSetting> rows)
        {
            var duplicates = rows.GroupBy(r => r.GateKey, StringComparer.Ordinal)
                                 .Where(g => g.Count() > 1)
                                 .Select(g => g.Key)
                                 .ToList();

            if (duplicates.Count > 0)
                _logger.LogWarning(
                    "Дублирани редове в PaymentGateSettings за: {Keys}. " +
                    "Ползвам последно променения за всеки ключ.", string.Join(", ", duplicates));

            return rows
                .GroupBy(r => r.GateKey, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(r => r.LastChangedAt ?? DateTime.MinValue)
                          .ThenByDescending(r => r.Id)
                          .First().IsEnabled,
                    StringComparer.Ordinal);
        }
    }
}
