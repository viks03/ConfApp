// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Services.Email
{
    public interface IEmailNotificationSettings
    {
        /// <summary>Whether this kind of mail is switched on. OTP always
        /// returns true.</summary>
        Task<bool> IsEnabledAsync(EmailTemplate template, CancellationToken ct = default);

        /// <summary>The current state of every switchable kind.</summary>
        Task<Dictionary<string, bool>> GetAllAsync(CancellationToken ct = default);

        Task SetAsync(EmailTemplate template, bool enabled, string? changedBy,
                      CancellationToken ct = default);
    }

    public sealed class EmailNotificationSettings : IEmailNotificationSettings
    {
        // MailComposer is a singleton and ApplicationDbContext is scoped, so
        // what is injected here is a scope factory rather than a context: every
        // read opens a short scope of its own. Injecting the context directly
        // would throw "Cannot consume scoped service from singleton" at
        // startup.
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<EmailNotificationSettings> _logger;

        // The settings change rarely and are read on every mail, so they are
        // cached and the cache is dropped on write.
        private Dictionary<string, bool>? _cache;
        private readonly SemaphoreSlim _lock = new(1, 1);

        /// <summary>
        /// The kinds an administrator is allowed to switch off.
        /// <para>
        /// <see cref="EmailTemplate.Otp"/> is deliberately NOT here — that is
        /// the code for registration and sign-in. Switching it off would make
        /// the site unusable.
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
            // Mandatory kinds are not looked up: they cannot be switched off.
            if (!Switchable.Contains(template)) return true;

            try
            {
                var all = await GetAllAsync(ct);
                return !all.TryGetValue(template.ToString(), out var enabled) || enabled;
            }
            catch (Exception ex)
            {
                // With the database unreachable it is better for the mail to go
                // out than for someone who has paid to get no confirmation.
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

                // Missing rows are created here, switched on by default, so that
                // a new kind of mail needs neither a migration nor a seed
                // script.
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
                    }

                    try
                    {
                        await db.SaveChangesAsync(ct);
                        _logger.LogInformation(
                            "Създадени настройки по подразбиране за: {Keys}", string.Join(", ", missing));
                    }
                    catch (DbUpdateException ex)
                    {
                        // [E-08] The unique index on TemplateKey no longer allows
                        // two rows for one key. If another process (a second
                        // instance, a concurrent request) inserted the same key
                        // between the read and the write, the insert fails — which
                        // is no reason for the admin panel to return 500. Read
                        // again and carry on.
                        _logger.LogWarning(ex,
                            "Друг процес вече е създал настройките за: {Keys}. Прочитам наново.",
                            string.Join(", ", missing));
                    }

                    db.ChangeTracker.Clear();
                    rows = await db.EmailNotificationSettings.ToListAsync(ct);
                }

                _cache = BuildMap(rows);
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

            // [E-08] The same semaphore GetAllAsync holds. The write used to
            // happen outside it: two concurrent writers — or a writer and a
            // reader busy creating the missing rows — each inserted a row for
            // the same key, and every read afterwards failed.
            await _lock.WaitAsync(ct);
            try
            {
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
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation("Имейл известие {Template} → {State} (от {Who})",
                template, enabled ? "включено" : "изключено", changedBy ?? "—");
        }

        /// <summary>
        /// [E-08] This used to be a plain <c>ToDictionary</c> — two rows for one
        /// key threw <see cref="ArgumentException"/> and /Admin returned 500 on
        /// every open, permanently. The unique index makes a duplicate
        /// impossible from now on; this stays for databases filled before it.
        /// On a duplicate the most recently changed row wins.
        /// </summary>
        private Dictionary<string, bool> BuildMap(List<EmailNotificationSetting> rows)
        {
            var duplicates = rows.GroupBy(r => r.TemplateKey, StringComparer.Ordinal)
                                 .Where(g => g.Count() > 1)
                                 .Select(g => g.Key)
                                 .ToList();

            if (duplicates.Count > 0)
                _logger.LogWarning(
                    "Дублирани редове в EmailNotificationSettings за: {Keys}. " +
                    "Ползвам последно променения за всеки ключ.", string.Join(", ", duplicates));

            return rows
                .GroupBy(r => r.TemplateKey, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(r => r.LastChangedAt ?? DateTime.MinValue)
                          .ThenByDescending(r => r.Id)
                          .First().IsEnabled,
                    StringComparer.Ordinal);
        }

        private void InvalidateCache() => _cache = null;
    }
}
