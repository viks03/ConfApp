// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// Part 10: "the cleanup deletes unconfirmed accounts once the deadline passes".
/// <para>
/// Everything here looks at the aftermath of <b>one real cycle</b> of
/// <c>CleanupService</c>, triggered the only way it happens in life: the
/// application starts (see <see cref="CleanupRun"/>). The service has no button
/// and no endpoint; it cannot simply be called.
/// </para>
/// </summary>
[Collection(BackgroundCollection.Name)]
public class CleanupServiceTests
{
    private readonly CleanupRun _run;

    public CleanupServiceTests(CleanupRun run) => _run = run;

    // ════════════════════════════════════════════════════════════════════
    // Who is removed
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Непотвърден_профил_отпреди_срока_се_трие()
    {
        Assert.Null(await _run.Db.FindUserAsync(CleanupRun.Abandoned));
    }

    [Fact]
    public async Task Непотвърден_профил_отпреди_час_остава()
    {
        // The deadline is 24 hours. Someone who has only just registered and has
        // not opened their message yet must not disappear under it.
        Assert.NotNull(await _run.Db.FindUserAsync(CleanupRun.Fresh));
    }

    [Fact]
    public async Task Потвърден_профил_остава()
    {
        Assert.NotNull(await _run.Db.FindUserAsync(CleanupRun.Confirmed));
    }

    /// <summary>
    /// [AD-02] The "Email address verified" checkbox in the admin panel writes
    /// straight to <c>EmailConfirmed</c>. An administrator clearing it to make
    /// someone confirm their address again must not send a participant who has
    /// paid — along with their paper — to irrevocable deletion on the next
    /// cycle.
    /// </summary>
    [Fact]
    public async Task Платил_участник_с_махната_отметка_остава()
    {
        Assert.NotNull(await _run.Db.FindUserAsync(CleanupRun.PaidPending));
    }

    [Fact]
    public async Task Участник_с_попълнено_PaidAt_остава()
    {
        Assert.NotNull(await _run.Db.FindUserAsync(CleanupRun.PaidAt));
    }

    /// <summary>
    /// Someone who has pressed "I have made the transfer" and is waiting for the
    /// administrator to confirm it. Their payment is not yet "Confirmed" and
    /// <c>PaidAt</c> is empty, so neither of the two conditions from [AD-02]
    /// protects them.
    /// </summary>
    [Fact]
    public async Task Обявилият_банков_превод_остава()
    {
        Assert.NotNull(await _run.Db.FindUserAsync(CleanupRun.IbanPending));
    }

    /// <summary>
    /// Someone with an unfinished crypto order: their money may be on the network
    /// at this very moment and the webhook may arrive in a few minutes.
    /// </summary>
    [Fact]
    public async Task Участникът_с_жива_крипто_поръчка_остава()
    {
        Assert.NotNull(await _run.Db.FindUserAsync(CleanupRun.CryptoOpen));
    }

    // ════════════════════════════════════════════════════════════════════
    // What goes with the profile
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [F-06] Only the paper used to be deleted. The verification document — a
    /// photograph of an identity card or a student card — was left on disk with no
    /// row in the database leading to it.
    /// </summary>
    [Fact]
    public void Файловете_на_изтрития_профил_ги_няма_на_диска()
    {
        Assert.False(File.Exists(_run.PaperPath),  "Докладът на изтрития участник още стои: " + _run.PaperPath);
        Assert.False(File.Exists(_run.DocPath),    "Документът за верификация още стои: " + _run.DocPath);
    }

    [Fact]
    public void Файлът_на_останалия_профил_не_е_пипнат()
    {
        Assert.True(File.Exists(_run.KeptPath),
            "Чистенето е изтрило доклада на участник, който не е трябвало да бъде изтрит.");
    }

    [Fact]
    public async Task Кодовете_на_изтрития_профил_ги_няма()
    {
        Assert.Empty(await _run.Db.OtpCodesAsync(CleanupRun.Abandoned));
    }

    /// <summary>
    /// The audit row is all that is left of a deleted profile. The reference number
    /// and the count of deleted files are in it deliberately: in a dispute over "I
    /// paid and I am not there", it is the only thing on our side.
    /// </summary>
    [Fact]
    public async Task Изтриването_оставя_ред_в_одита()
    {
        var rows = await _run.AuditAsync("System Cleanup");
        var row  = Assert.Single(rows, r => r.UserEmail == CleanupRun.Abandoned);

        Assert.Equal("System", row.IpAddress);
        Assert.NotNull(row.Details);
        Assert.Contains("Ref:", row.Details);
        Assert.Contains("Files removed: 2", row.Details);
    }

    /// <summary>
    /// [D-06] The foreign key is SetNull, so a deleted participant's order survives
    /// without them. Otherwise the wallet address, the amount and the timestamps
    /// vanish instantly — exactly when a dispute arises.
    /// </summary>
    [Fact]
    public async Task Крипто_поръчката_на_изтрития_оцелява_без_него()
    {
        var order = await _run.OrderAsync("CLN-OF-DELETED");

        Assert.NotNull(order);
        Assert.Null(order!.UserId);
        Assert.Equal("Expired", order.Status);
        Assert.Equal(150m, order.AmountEUR);
        Assert.Equal("CLN-OF-DELETED", order.ExternalId);
    }

    // ════════════════════════════════════════════════════════════════════
    // Expired crypto orders
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Изтекла_незавършена_поръчка_става_Expired()
    {
        var order = await _run.OrderAsync("CLN-EXPIRED");

        Assert.NotNull(order);
        Assert.Equal("Expired", order!.Status);
    }

    [Fact]
    public async Task Жива_поръчка_не_се_пипа()
    {
        var order = await _run.OrderAsync("CLN-ALIVE");

        Assert.NotNull(order);
        Assert.Equal("InProcess", order!.Status);
    }

    [Fact]
    public async Task Потвърдена_поръчка_не_се_обявява_за_изтекла()
    {
        // What has been paid stays paid even after the order's time runs out.
        var order = await _run.OrderAsync("CLN-CONFIRMED");

        Assert.NotNull(order);
        Assert.Equal("Confirmed", order!.Status);
    }

    /// <summary>
    /// [T-35] The order of a participant who is NOT deleted stays theirs: the link
    /// is not broken just in case.
    /// </summary>
    [Fact]
    public async Task Живата_поръчка_остава_вързана_за_своя_участник()
    {
        var order = await _run.OrderAsync("CLN-OPEN-OF-LIVE");

        Assert.NotNull(order);
        Assert.Equal(_run.CryptoUserId, order!.UserId);
    }

    [Fact]
    public async Task Изтичането_на_поръчките_оставя_ред_в_одита()
    {
        var rows = await _run.AuditAsync("Crypto Cleanup");

        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r.Details != null && r.Details.Contains("Auto-expired 1"));
    }

    // ════════════════════════════════════════════════════════════════════
    // Expired codes ([D-09])
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Codes sent to an e-mail that never became a profile were never deleted at
    /// all — while the table is queried on every sign-in and every opening of
    /// /Verification.
    /// </summary>
    [Fact]
    public async Task Код_изтекъл_преди_осем_дни_се_трие()
    {
        Assert.Empty(await _run.Db.OtpCodesAsync(CleanupRun.OldCodes));
    }

    [Fact]
    public async Task Код_изтекъл_преди_два_дни_остава()
    {
        // The retention is deliberately seven days: the trace has to survive if
        // someone asks why they could not sign in yesterday.
        Assert.NotEmpty(await _run.Db.OtpCodesAsync(CleanupRun.RecentCodes));
    }

    // ════════════════════════════════════════════════════════════════════
    // The trace the cycle itself leaves ([S-04])
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The row used to be written only when there was something to delete, so a
    /// stopped service and a quiet week looked the same, both in the admin panel
    /// and in the database.
    /// </summary>
    [Fact]
    public async Task Цикълът_оставя_ред_с_равносметка()
    {
        var rows = await _run.AuditAsync("Cleanup Summary");

        Assert.NotEmpty(rows);

        var first = rows[0];
        Assert.Equal("System", first.UserEmail);
        Assert.Equal("System", first.IpAddress);

        // The summary has to agree with the individual rows for each deleted
        // profile; otherwise the figure in the admin panel means nothing.
        var deleted = await _run.AuditAsync("System Cleanup");
        Assert.Contains($"Removed {deleted.Count} abandoned accounts", first.Details);

        Assert.Contains("Auto-expired 1 crypto orders", first.Details);
        Assert.Contains("Deleted 1 expired OTP codes", first.Details);
    }

    [Fact]
    public async Task Цикълът_се_обявява_и_в_лога()
    {
        var line = await _run.Probe.WaitForLogLineAsync(
            "System Cleanup Service started", TimeSpan.FromSeconds(20));

        Assert.NotNull(line);
        // The interval goes into the line: [S-04] publishes it so that it cannot
        // drift apart from the figure Health reads.
        Assert.Contains("Interval: 1", line);
    }

    // ════════════════════════════════════════════════════════════════════
    // What it does with an administrator
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The filter is by state rather than by role: the condition looks at every row
    /// in <c>Users</c>. That is safe today only because the single administrator is
    /// seeded confirmed and reconfirmed at every startup (<c>DbInitializer</c>),
    /// and the admin panel does not list them at all, so their checkbox cannot be
    /// cleared from outside.
    /// <para>
    /// The test keeps that coincidence visible: if a second administrator appears
    /// tomorrow, one who went through an ordinary registration, they will disappear
    /// on the next cycle.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ролята_не_е_щит_срещу_чистенето()
    {
        Assert.Null(await _run.Db.FindUserAsync(CleanupRun.AdminLike));
    }

    /// <summary>
    /// The other side of the above, and the only one that really happens: the
    /// system administrator survives the cycle.
    /// </summary>
    [Fact]
    public async Task Системният_администратор_преживява_цикъла()
    {
        Assert.NotNull(await _run.Db.FindUserAsync(TestCredentials.Current.AdminEmail));
    }
}
