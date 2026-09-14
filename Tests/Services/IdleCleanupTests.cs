// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// The empty cycle: [S-04] in its purest form.
/// <para>
/// The application is started against a database with absolutely nothing to clean
/// up. Such a cycle used to leave no trace anywhere, and "last cleanup" in the
/// admin panel and the ninth check in Health read exactly that trace — so a
/// stopped service and a quiet week looked the same.
/// </para>
/// </summary>
public class IdleCleanupTests : IClassFixture<IdleCleanupTests.IdleRun>
{
    private readonly IdleRun _run;

    public IdleCleanupTests(IdleRun run) => _run = run;

    [Fact]
    public async Task Цикъл_без_работа_пак_оставя_ред()
    {
        var rows = await _run.Db.ReadAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(a => a.Action == "Cleanup Summary")
            .ToListAsync());

        var row = Assert.Single(rows);

        Assert.Contains("Removed 0 abandoned accounts", row.Details);
        Assert.Contains("Auto-expired 0 crypto orders", row.Details);
        Assert.Contains("Deleted 0 expired OTP codes", row.Details);
    }

    /// <summary>
    /// The empty cycle must NOT reach the log at Information level; otherwise the
    /// file collects 24 useless lines a day. The trace lives in the audit log
    /// rather than in the log file.
    /// </summary>
    [Fact]
    public void Празният_цикъл_не_залива_лога()
    {
        var log = _run.Probe.ReadLog();

        Assert.DoesNotContain("System Cleanup cycle completed successfully. Accounts removed:", log);
    }

    // ════════════════════════════════════════════════════════════════════

    public sealed class IdleRun : IAsyncLifetime
    {
        public StartupProbe Probe { get; private set; } = null!;
        public TestDb       Db    { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Probe = await StartupProbe.StartAsync("cleanup-idle");

            if (Probe.StartupError != null)
                throw new InvalidOperationException(
                    "Пробното копие за празния цикъл не тръгна: " + Probe.StartupError.Message,
                    Probe.StartupError);

            Db = Probe.Db();

            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                var rows = await Db.ReadAsync(db =>
                    db.AuditLogs.CountAsync(a => a.Action == "Cleanup Summary"));

                if (rows > 0) return;
                await Task.Delay(250);
            }

            throw new TimeoutException(
                "Празният цикъл не остави ред „Cleanup Summary“ за 60 секунди.");
        }

        public Task DisposeAsync()
        {
            Db.Dispose();
            return Task.CompletedTask;
        }
    }
}
