// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using ConferenceApp.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// One real cycle of <c>CleanupService</c>, watched from the outside.
/// <para>
/// The service has no button and no endpoint: it starts with the application,
/// runs its first cycle immediately and the next one an hour later. The cycle is
/// therefore triggered the only way it happens in life — <b>by starting an
/// application</b> (<see cref="StartupProbe"/>), but against a database whose
/// rows are already sitting there waiting.
/// </para>
/// <para>
/// Every case is set up at once and checked after a single cycle, because the
/// cycle is one event for the whole database. The rows know nothing of one
/// another, though: each case is a separate participant, a separate order or a
/// separate code with an e-mail of its own, so one failing does not move the
/// others and the order of the tests does not matter.
/// </para>
/// </summary>
public sealed class CleanupRun : IAsyncLifetime
{
    // ── The participants, one per question ───────────────────────────────
    public const string Abandoned   = "cln-abandoned@example.test";   // unconfirmed, 48h old  → deleted
    public const string Fresh       = "cln-fresh@example.test";       // unconfirmed, 1h old   → kept
    public const string PaidPending = "cln-paid-status@example.test";  // unconfirmed, but paid → kept
    public const string PaidAt      = "cln-paid-at@example.test";      // unconfirmed, has PaidAt → kept
    public const string Confirmed   = "cln-confirmed@example.test";    // confirmed, 48h old    → kept
    public const string AdminLike   = "cln-admin@example.test";        // unconfirmed admin, 48h → ?
    public const string IbanPending = "cln-iban@example.test";         // declared a transfer   → ?
    public const string CryptoOpen  = "cln-crypto@example.test";       // has a live crypto order → ?

    // ── Codes that belong to nobody ──────────────────────────────────────
    public const string OldCodes    = "cln-old-codes@example.test";    // expired 8 days ago → deleted
    public const string RecentCodes = "cln-recent-codes@example.test";  // expired 2 days ago → kept

    public const string PaperRelative = "uploads/papers26/cleanup-paper.pdf";
    public const string DocRelative   = "uploads/submitted-documents/cleanup-doc.jpg";
    public const string KeptPaper     = "uploads/papers26/cleanup-kept-paper.pdf";

    public StartupProbe Probe { get; private set; } = null!;
    public TestDb       Db    { get; private set; } = null!;

    public string PrivateRoot { get; } =
        Path.Combine(TestPaths.RunScratch, "cleanup-uploads");

    public string DbFile { get; } =
        Path.Combine(TestPaths.RunScratch, "cleanup", "app.db");

    /// <summary>The id of the deleted participant; their order is looked up by it.</summary>
    public string AbandonedUserId { get; private set; } = string.Empty;
    public string IbanUserId      { get; private set; } = string.Empty;
    public string CryptoUserId    { get; private set; } = string.Empty;

    public string PaperPath => Path.Combine(PrivateRoot, "uploads", "papers26", "cleanup-paper.pdf");
    public string DocPath   => Path.Combine(PrivateRoot, "uploads", "submitted-documents", "cleanup-doc.jpg");
    public string KeptPath  => Path.Combine(PrivateRoot, "uploads", "papers26", "cleanup-kept-paper.pdf");

    public async Task InitializeAsync()
    {
        TestDb.Recreate(DbFile);
        Db = new TestDb(DbFile);
        await Db.MigrateAsync();

        await SeedUsersAsync();
        await SeedCryptoOrdersAsync();
        await SeedOtpCodesAsync();
        SeedFiles();

        Probe = await StartupProbe.StartAsync("cleanup", settings =>
        {
            settings["ConnectionStrings:DefaultConnection"] = $"Data Source={DbFile}";
            settings["BackupSettings:DbFileName"]           = DbFile;
            settings["Uploads:PrivateRoot"]                 = PrivateRoot;
        });

        if (Probe.StartupError != null)
            throw new InvalidOperationException(
                "Приложението за цикъла на чистенето не тръгна: " + Probe.StartupError.Message,
                Probe.StartupError);

        await WaitForCycleAsync(TimeSpan.FromSeconds(60));
    }

    // ════════════════════════════════════════════════════════════════════
    // Setting it all up
    // ════════════════════════════════════════════════════════════════════

    private async Task SeedUsersAsync()
    {
        var abandoned = await Db.CreateParticipantAsync(Abandoned);
        AbandonedUserId = abandoned.Id;

        await Db.CreateParticipantAsync(Fresh);
        await Db.CreateParticipantAsync(PaidPending, paymentStatus: "Confirmed");
        await Db.CreateParticipantAsync(PaidAt);
        await Db.CreateParticipantAsync(Confirmed);
        await Db.CreateParticipantAsync(AdminLike);

        var iban   = await Db.CreateParticipantAsync(IbanPending);
        var crypto = await Db.CreateParticipantAsync(CryptoOpen);

        IbanUserId   = iban.Id;
        CryptoUserId = crypto.Id;

        var old   = DateTime.UtcNow.AddHours(-48);
        var young = DateTime.UtcNow.AddHours(-1);

        await Db.WriteAsync(async db =>
        {
            foreach (var user in await db.Users.ToListAsync())
            {
                switch (user.Email)
                {
                    case Abandoned:
                        user.EmailConfirmed          = false;
                        user.CreatedAt               = old;
                        user.PaperFilePath           = PaperRelative;
                        user.VerificationDocumentPath = DocRelative;
                        break;

                    case Fresh:
                        user.EmailConfirmed = false;
                        user.CreatedAt      = young;
                        user.PaperFilePath  = KeptPaper;
                        break;

                    case PaidPending:
                        // [AD-02]: a participant who has paid but whose "e-mail
                        // confirmed" flag was cleared is not an abandoned profile.
                        user.EmailConfirmed = false;
                        user.CreatedAt      = old;
                        break;

                    case PaidAt:
                        user.EmailConfirmed = false;
                        user.CreatedAt      = old;
                        user.PaymentStatus  = "Pending";
                        user.PaidAt         = old.AddHours(2);
                        break;

                    case Confirmed:
                        user.EmailConfirmed = true;
                        user.CreatedAt      = old;
                        break;

                    case AdminLike:
                        user.EmailConfirmed = false;
                        user.CreatedAt      = old;
                        break;

                    case IbanPending:
                        // They have declared "I have made the transfer" and are waiting
                        // for the administrator to confirm it; the flag was cleared
                        // afterwards.
                        user.EmailConfirmed          = false;
                        user.CreatedAt               = old;
                        user.PaymentStatus           = "Pending";
                        user.IbanTransferSubmittedAt = old.AddHours(1);
                        break;

                    case CryptoOpen:
                        // Their money may be on the network at this very moment.
                        user.EmailConfirmed = false;
                        user.CreatedAt      = old;
                        user.PaymentStatus  = "Pending";
                        break;
                }
            }
        });

        // The role makes the last participant an administrator; the question is
        // whether the cleanup tells an administrator from an abandoned profile.
        await GiveAdminRoleAsync(AdminLike);
    }

    private async Task GiveAdminRoleAsync(string email)
    {
        using var scope = Db.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roles.RoleExistsAsync("Admin"))
            await roles.CreateAsync(new IdentityRole("Admin"));

        var user = await users.FindByEmailAsync(email)
                   ?? throw new InvalidOperationException("Няма такъв участник: " + email);

        await users.AddToRoleAsync(user, "Admin");
    }

    private async Task SeedCryptoOrdersAsync()
    {
        await Db.WriteAsync(db =>
        {
            db.CryptoOrders.AddRange(
                // Expired and unfinished: should become Expired.
                new CryptoOrder
                {
                    UserId       = null,
                    ExternalId   = "CLN-EXPIRED",
                    Go28OrderId  = 91001,
                    Currency     = "USDC",
                    Network      = "polygon",
                    AmountEUR    = 150m,
                    Status       = "InProcess",
                    CreatedAt    = DateTime.UtcNow.AddHours(-3),
                    ExpiresAt    = DateTime.UtcNow.AddHours(-1)
                },
                // Still live: left alone.
                new CryptoOrder
                {
                    UserId       = null,
                    ExternalId   = "CLN-ALIVE",
                    Go28OrderId  = 91002,
                    Currency     = "USDC",
                    Network      = "polygon",
                    AmountEUR    = 150m,
                    Status       = "InProcess",
                    CreatedAt    = DateTime.UtcNow,
                    ExpiresAt    = DateTime.UtcNow.AddHours(2)
                },
                // Paid but past its expiry time: stays confirmed.
                new CryptoOrder
                {
                    UserId       = null,
                    ExternalId   = "CLN-CONFIRMED",
                    Go28OrderId  = 91003,
                    Currency     = "USDC",
                    Network      = "polygon",
                    AmountEUR    = 150m,
                    Status       = "Confirmed",
                    CreatedAt    = DateTime.UtcNow.AddHours(-5),
                    ExpiresAt    = DateTime.UtcNow.AddHours(-4)
                },
                // Belonging to a participant who is NOT deleted: a live order
                // ([T-35]).
                new CryptoOrder
                {
                    UserId       = CryptoUserId,
                    ExternalId   = "CLN-OPEN-OF-LIVE",
                    Go28OrderId  = 91005,
                    Currency     = "USDC",
                    Network      = "polygon",
                    AmountEUR    = 150m,
                    Status       = "InProcess",
                    CreatedAt    = DateTime.UtcNow.AddMinutes(-10),
                    ExpiresAt    = DateTime.UtcNow.AddHours(1)
                },
                // Belonging to the participant who will be deleted ([D-06]). Their
                // order expired unpaid; otherwise its very existence would already
                // protect them from the cleanup ([T-35]).
                new CryptoOrder
                {
                    UserId       = AbandonedUserId,
                    ExternalId   = "CLN-OF-DELETED",
                    Go28OrderId  = 91004,
                    Currency     = "USDC",
                    Network      = "polygon",
                    AmountEUR    = 150m,
                    Status       = "Expired",
                    CreatedAt    = DateTime.UtcNow.AddHours(-6),
                    ExpiresAt    = DateTime.UtcNow.AddHours(-5)
                });

            return Task.CompletedTask;
        });
    }

    private async Task SeedOtpCodesAsync()
    {
        await Db.WriteAsync(db =>
        {
            db.Set<OtpCode>().AddRange(
                // The abandoned profile's codes: they have to go with it.
                new OtpCode
                {
                    Email          = Abandoned,
                    Code           = "111111",
                    Purpose        = "Login",
                    IsUsed         = false,
                    CreatedAt      = DateTime.UtcNow.AddMinutes(-30),
                    ExpirationTime = DateTime.UtcNow.AddMinutes(-15)
                },
                // [D-09]: a code for an e-mail that never became a profile, expired
                // eight days ago: deleted.
                new OtpCode
                {
                    Email          = OldCodes,
                    Code           = "222222",
                    Purpose        = "Registration",
                    IsUsed         = true,
                    CreatedAt      = DateTime.UtcNow.AddDays(-8).AddMinutes(-15),
                    ExpirationTime = DateTime.UtcNow.AddDays(-8)
                },
                // Expired two days ago: kept, since the retention is seven days.
                new OtpCode
                {
                    Email          = RecentCodes,
                    Code           = "333333",
                    Purpose        = "Registration",
                    IsUsed         = false,
                    CreatedAt      = DateTime.UtcNow.AddDays(-2).AddMinutes(-15),
                    ExpirationTime = DateTime.UtcNow.AddDays(-2)
                });

            return Task.CompletedTask;
        });
    }

    private void SeedFiles()
    {
        foreach (var path in new[] { PaperPath, DocPath, KeptPath })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "тестов файл за чистенето");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Waiting for the cycle
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The cycle ends with an audit row of its own ([S-04]), which is also the
    /// signal that there is something to check. The service starts along with the
    /// application, so it may well have finished before the first HTTP request.
    /// </summary>
    private async Task WaitForCycleAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await SummaryRowsAsync() > 0) return;
            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"CleanupService не приключи цикъл за {timeout.TotalSeconds:0} секунди — " +
            "в AuditLogs няма ред „Cleanup Summary“. Виж лога: " + Probe.ReadLog());
    }

    public Task<int> SummaryRowsAsync() =>
        Db.ReadAsync(db => db.AuditLogs.CountAsync(a => a.Action == "Cleanup Summary"));

    public Task<List<AuditLog>> AuditAsync(string action) =>
        Db.ReadAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(a => a.Action == action)
            .OrderBy(a => a.Id)
            .ToListAsync());

    public Task<CryptoOrder?> OrderAsync(string externalId) =>
        Db.ReadAsync(db => db.CryptoOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.ExternalId == externalId));

    public Task DisposeAsync()
    {
        Db.Dispose();
        return Task.CompletedTask;
    }
}
