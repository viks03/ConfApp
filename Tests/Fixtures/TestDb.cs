// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// The tests' access to <c>App_Data/test.db</c>, a separate copy recreated on
/// every run. The real <c>conferenceapp.db</c> is never touched.
/// <para>
/// Participants are created through the same <see cref="UserManager{TUser}"/>
/// the application uses, so the row in the database is what a real registration
/// would have left: a password hash, a normalised e-mail, a stamp. Creating a
/// participant is setup for a test rather than the behaviour under test;
/// registration from end to end is part 3.
/// </para>
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly ServiceProvider _services;

    public string ConnectionString { get; }
    public string File { get; }

    public TestDb(string dbFile)
    {
        File = dbFile;
        ConnectionString = $"Data Source={dbFile}";

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(ConnectionString));
        services.AddIdentityCore<ApplicationUser>(o =>
                {
                    o.User.RequireUniqueEmail = true;
                    o.Password.RequireDigit = true;
                    o.Password.RequiredLength = 8;
                    o.Password.RequireNonAlphanumeric = false;
                    o.Password.RequireUppercase = true;
                    o.Password.RequireLowercase = false;
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();

        _services = services.BuildServiceProvider();
    }

    /// <summary>Deletes the database file and its journals, so the application starts clean.</summary>
    public static void Recreate(string dbFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbFile)!);

        foreach (var path in new[] { dbFile, dbFile + "-wal", dbFile + "-shm" })
        {
            if (!System.IO.File.Exists(path)) continue;

            // If someone has pointed the tests at the real database, stopping here
            // is better than deleting it.
            if (Path.GetFullPath(path) == Path.GetFullPath(TestPaths.LiveDbFile))
                throw new InvalidOperationException(
                    "Тестовете сочат към истинската база " + TestPaths.LiveDbFile + ". Спирам.");

            System.IO.File.Delete(path);
        }
    }

    /// <summary>
    /// Applies the migrations, the same ones <c>Program.cs</c> applies at startup.
    /// <para>
    /// Part 10 needs a database that already has rows in it BEFORE the
    /// application starts: the background cleanup makes its first pass
    /// immediately after startup, so rows added afterwards have to wait an hour
    /// for the next one.
    /// </para>
    /// </summary>
    public async Task MigrateAsync()
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }

    public IServiceScope Scope() => _services.CreateScope();

    /// <summary>Reads from the database in a context of its own, with nothing cached from an earlier check.</summary>
    public async Task<T> ReadAsync<T>(Func<ApplicationDbContext, Task<T>> read)
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await read(db);
    }

    public async Task WriteAsync(Func<ApplicationDbContext, Task> write)
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await write(db);
        await db.SaveChangesAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // Participants
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A participant who signs in with a one-time code and has no password,
    /// which is what every registered user of the application looks like.
    /// </summary>
    /// <param name="partForm">
    /// "1" a lecturer, "2" a student, "3" online, "4" a journalist. This decides
    /// the fee.
    /// </param>
    public async Task<ApplicationUser> CreateParticipantAsync(
        string  email,
        string  partForm      = "1",
        string  paymentStatus = "Pending",
        string? reference     = null)
    {
        using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // The names are in Latin script and the telephone is filled in, because
        // that is what a real registration produces: /Register accepts Latin
        // script only, and /Profile demands a telephone on every save.
        var user = new ApplicationUser
        {
            UserName        = email,
            Email           = email,
            EmailConfirmed  = true,
            FirstName       = "Test",
            LastName        = "Participant",
            Age             = 33,
            AcademicTitle   = "Assoc. Prof.",
            PhoneNumber     = "+359 888 000111",
            Workplace       = "UNWE",
            PartForm        = partForm,
            HasAcceptedGdpr = true,
            GdprConsentDate = DateTime.UtcNow,
            ReferenceNumber = reference ?? NewReference(),
            PaymentStatus   = paymentStatus,
            CreatedAt       = DateTime.UtcNow
        };

        var result = await users.CreateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Не можах да създам тестов участник: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));

        return user;
    }

    public async Task DeleteParticipantAsync(string email)
    {
        using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        if (user != null) await users.DeleteAsync(user);
    }

    public Task<ApplicationUser?> FindUserAsync(string email) =>
        ReadAsync(db => db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email));

    /// <summary>The same shape as the real ones: BCE2026-12345ABC.</summary>
    public static string NewReference()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        var digits = Random.Shared.Next(10000, 100000);
        var suffix = new string(Enumerable.Range(0, 3)
            .Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
        return $"BCE2026-{digits}{suffix}";
    }

    // ════════════════════════════════════════════════════════════════════
    // One-time codes and the audit log
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The sign-in code, read from <c>OtpCodes</c>. The database is the more
    /// reliable of the two sources, because it does not depend on the template
    /// of the message.
    /// </summary>
    public async Task<OtpCode?> WaitForOtpAsync(string email, string purpose, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var code = await ReadAsync(db => db.OtpCodes
                .AsNoTracking()
                .Where(o => o.Email == email && o.Purpose == purpose && !o.IsUsed)
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync());

            if (code != null) return code;
            await Task.Delay(100);
        }

        return null;
    }

    public Task<List<AuditLog>> AuditForAsync(string userEmail) =>
        ReadAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(a => a.UserEmail == userEmail)
            .OrderByDescending(a => a.Id)
            .ToListAsync());

    public Task<List<AuditLog>> AuditSinceAsync(int lastId) =>
        ReadAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(a => a.Id > lastId)
            .OrderBy(a => a.Id)
            .ToListAsync());

    public Task<int> LastAuditIdAsync() =>
        ReadAsync(async db => await db.AuditLogs.AnyAsync()
            ? await db.AuditLogs.MaxAsync(a => a.Id)
            : 0);

    public Task<TicketTierModel?> TierAsync(string tierKey) =>
        ReadAsync(db => db.TicketTiers.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TierKey == tierKey));

    public Task<List<CryptoOrder>> CryptoOrdersAsync(string? userId = null) =>
        ReadAsync(db => db.CryptoOrders
            .AsNoTracking()
            .Where(o => userId == null || o.UserId == userId)
            .OrderBy(o => o.Id)
            .ToListAsync());

    /// <summary>
    /// A participant WITH a password, that is, one <c>/Login</c> sends down the
    /// administrator's path (<c>HasPasswordAsync</c> decides which of the two it
    /// takes).
    /// <para>
    /// The tests for the lockout after three wrong attempts use one of these
    /// rather than the real administrator: the failed attempts would lock that
    /// account for 12 hours, and every later test that needs the panel would
    /// fail because of this one.
    /// </para>
    /// </summary>
    public async Task<ApplicationUser> CreatePasswordUserAsync(string email, string password)
    {
        using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName        = email,
            Email           = email,
            EmailConfirmed  = true,
            FirstName       = "Тест",
            LastName        = "Спарола",
            Age             = 40,
            AcademicTitle   = "проф.",
            Workplace       = "УНСС",
            PartForm        = "1",
            HasAcceptedGdpr = true,
            GdprConsentDate = DateTime.UtcNow,
            ReferenceNumber = NewReference(),
            PaymentStatus   = "Pending",
            CreatedAt       = DateTime.UtcNow
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Не можах да създам участник с парола: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));

        return user;
    }

    /// <summary>How long the account is locked out for, or <c>null</c> if it is not.</summary>
    public async Task<DateTimeOffset?> LockoutEndAsync(string email)
    {
        var user = await FindUserAsync(email);
        return user?.LockoutEnd;
    }

    /// <summary>
    /// Writes a verification status straight into the row.
    /// <para>
    /// "Pending" is reached the user's way, through <c>/SubmitDocuments</c>, and
    /// the tests for submitting go through there. "Approved" and "Rejected",
    /// however, are granted by the administrator, which is part 6. Here they are
    /// setup for the question of what the profile looks like in that state, not
    /// the behaviour under test.
    /// </para>
    /// </summary>
    public Task SetVerificationAsync(
        string  email,
        string  status,
        string? documentPath    = null,
        string? rejectionReason = null) =>
        WriteAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.VerificationStatus           = status;
            user.VerificationDocumentPath     = documentPath;
            user.VerificationRejectionReason  = rejectionReason;
            user.VerificationSubmittedAt      = status == "None" ? null : DateTime.UtcNow;
        });

    /// <summary>
    /// Writes a path to a paper straight into the row.
    /// <para>
    /// Part 5 uses this for the question of what happens if a path pointing
    /// outside ends up in <c>PaperFilePath</c> — through a botched migration, an
    /// import, or a future handler. Such a path cannot be put there through the
    /// form, so there is no other way to reach that state.
    /// </para>
    /// </summary>
    public Task SetPaperPathAsync(string email, string? relativePath) =>
        WriteAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.PaperFilePath = relativePath;
        });

    /// <summary>
    /// Writes the preferred language straight into the row, for [T-29].
    /// <para>
    /// A participant gets it at registration or when switching from the top bar,
    /// but the test of the message does not want to go through either of those
    /// every time — and it deliberately uses <c>null</c> for a row that predates
    /// the field.
    /// </para>
    /// </summary>
    public Task SetPreferredLanguageAsync(string email, string? language) =>
        WriteAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.PreferredLanguage = language;
        });

    /// <summary>The same for the photograph of the identity document.</summary>
    public Task SetVerificationDocumentPathAsync(string email, string? relativePath) =>
        WriteAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.VerificationDocumentPath = relativePath;
        });

    /// <summary>Marks the participant as having chosen a bank transfer.</summary>
    public Task MarkIbanSubmittedAsync(string email) =>
        WriteAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.IbanTransferSubmittedAt = DateTime.UtcNow;
        });

    /// <summary>Whether there is a password at all, which is what /Login branches on.</summary>
    public async Task<bool> HasPasswordAsync(string email)
    {
        var user = await FindUserAsync(email);
        return user?.PasswordHash != null;
    }

    /// <summary>How many failed attempts the account has accumulated.</summary>
    public async Task<int> AccessFailedCountAsync(string email)
    {
        var user = await FindUserAsync(email);
        return user?.AccessFailedCount ?? 0;
    }

    /// <summary>Every code for an address, for assertions about how many there are and which was used.</summary>
    public Task<List<OtpCode>> OtpCodesAsync(string email, string? purpose = null) =>
        ReadAsync(db => db.OtpCodes
            .AsNoTracking()
            .Where(o => o.Email == email && (purpose == null || o.Purpose == purpose))
            .OrderBy(o => o.Id)
            .ToListAsync());

    /// <summary>
    /// Moves the expiry of the most recent code into the past.
    /// <para>
    /// The one place where a test writes directly to a row instead of going the
    /// user's way. The window is 15 minutes and cannot be waited out, while "an
    /// expired code does not work" was an explicitly requested check. Entering
    /// the code afterwards still goes through the page, as it does for a person.
    /// </para>
    /// </summary>
    public async Task ExpireLastOtpAsync(string email, string purpose)
    {
        await WriteAsync(async db =>
        {
            var otp = await db.OtpCodes
                .Where(o => o.Email == email && o.Purpose == purpose && !o.IsUsed)
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException(
                    $"Няма неизползван код за {purpose} на този адрес — няма какво да се изтече.");

            otp.ExpirationTime = DateTime.UtcNow.AddMinutes(-1);
        });
    }

    /// <summary>
    /// Moves the creation time of the codes backwards; otherwise the ceiling of
    /// three codes per 30 minutes cannot be cleared within a single run.
    /// </summary>
    public async Task AgeOtpCodesAsync(string email, TimeSpan by)
    {
        await WriteAsync(async db =>
        {
            var codes = await db.OtpCodes.Where(o => o.Email == email).ToListAsync();
            foreach (var code in codes)
            {
                code.CreatedAt      -= by;
                code.ExpirationTime -= by;
            }
        });
    }

    /// <summary>Cleans up after the test: <c>OtpCodes</c> rows do not go away with the participant.</summary>
    public Task DeleteOtpCodesAsync(string email) =>
        WriteAsync(async db =>
        {
            var codes = await db.OtpCodes.Where(o => o.Email == email).ToListAsync();
            db.OtpCodes.RemoveRange(codes);
        });

    public void Dispose() => _services.Dispose();
}
