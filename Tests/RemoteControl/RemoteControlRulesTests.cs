// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Services.RemoteControl;

namespace ConferenceApp.Tests.RemoteControl;

/// <summary>
/// The two rules the whole switch stands on, without an HTTP request anywhere
/// in sight: what counts as an answer, and what happens when the answers stop.
/// <para>
/// The silence rules are measured in tens of minutes and in hours, so the clock
/// is a fake one. The same rules are also checked end to end with a real server
/// and real seconds in <see cref="RemoteControlSwitchTests"/> — this file is
/// about being exact, that one is about being real.
/// </para>
/// </summary>
public class RemoteControlRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 14, 2, 15, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hour = TimeSpan.FromMinutes(60);

    /// <summary>A clock that only moves when a test moves it.</summary>
    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    // ── The default ───────────────────────────────────────────────────

    [Fact]
    public void Без_нито_един_отговор_сайтът_е_видим()
    {
        var state = new RemoteControlState(new FixedClock(Start));

        // Nothing has ever been heard from the control server — which is also
        // the first second after every restart.
        Assert.False(state.Decide(Hour).Hide);
        Assert.Null(state.Current);
    }

    [Fact]
    public void Отговор_видим_не_крие_нищо()
    {
        var state = new RemoteControlState(new FixedClock(Start));
        state.Report(visible: true, "maintenance", "бг", "en");

        Assert.False(state.Decide(Hour).Hide);
    }

    // ── Hiding, and the code it hides with ────────────────────────────

    [Fact]
    public void Режим_maintenance_връща_503()
    {
        var state = new RemoteControlState(new FixedClock(Start));
        state.Report(visible: false, "maintenance", "бг", "en");

        var decision = state.Decide(Hour);

        Assert.True(decision.Hide);
        // 503 and not 500: a planned stop, so search engines keep the site.
        Assert.Equal(503, decision.StatusCode);
        Assert.Equal("бг", decision.MessageBg);
        Assert.Equal("en", decision.MessageEn);
    }

    [Fact]
    public void Режим_error_връща_500()
    {
        var state = new RemoteControlState(new FixedClock(Start));
        state.Report(visible: false, "error", "бг", "en");

        Assert.Equal(500, state.Decide(Hour).StatusCode);
    }

    [Fact]
    public void Режим_notfound_връща_404()
    {
        var state = new RemoteControlState(new FixedClock(Start));
        state.Report(visible: false, "notfound", "бг", "en");

        var decision = state.Decide(Hour);

        Assert.True(decision.Hide);
        Assert.Equal(404, decision.StatusCode);

        // The texts still travel this far — the page is where they are dropped.
        Assert.Equal("бг", decision.MessageBg);
        Assert.Equal("en", decision.MessageEn);
    }

    [Theory]
    [InlineData("maintenance", 503)]
    [InlineData("error",       500)]
    [InlineData("notfound",    404)]
    // The panel may write the mode however it likes.
    [InlineData("NotFound",    404)]
    [InlineData("MAINTENANCE", 503)]
    public void Режимът_решава_кода(string mode, int expected)
    {
        Assert.True(RemoteControlModes.IsKnown(mode));
        Assert.Equal(expected, RemoteControlModes.StatusCodeFor(mode));
    }

    // ── Silence ───────────────────────────────────────────────────────

    [Fact]
    public void Десет_минути_мълчание_пазят_последното_състояние()
    {
        var clock = new FixedClock(Start);
        var state = new RemoteControlState(clock);

        state.Report(visible: false, "maintenance", "бг", "en");
        clock.Advance(TimeSpan.FromMinutes(10));

        // Well inside the hour: the last thing the server said still counts.
        Assert.True(state.Decide(Hour).Hide);
    }

    [Fact]
    public void Час_и_половина_мълчание_показват_сайта()
    {
        var clock = new FixedClock(Start);
        var state = new RemoteControlState(clock);

        state.Report(visible: false, "maintenance", "бг", "en");
        clock.Advance(TimeSpan.FromMinutes(90));

        // Deliberately: it is far likelier that the control server has fallen
        // over than that somebody wants the conference hidden and cannot reach
        // the machine to say so.
        Assert.False(state.Decide(Hour).Hide);

        // The state itself is not thrown away — a server that comes back and
        // says "hidden" again is believed immediately.
        Assert.NotNull(state.Current);
        Assert.False(state.Current!.Visible);
    }

    [Fact]
    public void Точно_на_прага_състоянието_още_държи()
    {
        var clock = new FixedClock(Start);
        var state = new RemoteControlState(clock);

        state.Report(visible: false, "maintenance", "бг", "en");
        clock.Advance(Hour);

        // "Over StaleAfterMinutes" means over, not exactly at.
        Assert.True(state.Decide(Hour).Hide);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(state.Decide(Hour).Hide);
    }

    [Fact]
    public void Нов_отговор_мести_прага_напред()
    {
        var clock = new FixedClock(Start);
        var state = new RemoteControlState(clock);

        state.Report(visible: false, "maintenance", "бг", "en");
        clock.Advance(TimeSpan.FromMinutes(59));
        state.Report(visible: false, "maintenance", "бг", "en");

        // An hour after the FIRST answer, but a minute after the last one.
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(state.Decide(Hour).Hide);
    }

    // ── What counts as an answer ──────────────────────────────────────

    [Fact]
    public void Пълният_отговор_от_спецификацията_се_чете()
    {
        var body = """
            {
              "visible": false,
              "mode": "maintenance",
              "messageBg": "Извършваме кратка поддръжка. Ще се върнем скоро.",
              "messageEn": "We are performing brief maintenance. We will be back shortly.",
              "changedAt": "2026-09-14T02:15:00Z",
              "changedBy": "viktor"
            }
            """;

        Assert.True(RemoteControlResponse.TryParse(
            body, out var visible, out var mode, out var bg, out var en, out _));

        Assert.False(visible);
        Assert.Equal("maintenance", mode);
        Assert.StartsWith("Извършваме", bg);
        Assert.StartsWith("We are", en);
    }

    [Fact]
    public void Режим_notfound_е_годен_отговор()
    {
        var body = """
            {
              "visible": false,
              "mode": "NotFound",
              "messageBg": "Спряхме сайта.",
              "messageEn": "We took the site down."
            }
            """;

        Assert.True(RemoteControlResponse.TryParse(
            body, out var visible, out var mode, out var bg, out var en, out _));

        Assert.False(visible);
        // Written back in lower case, whatever the panel sent.
        Assert.Equal("notfound", mode);

        // Read and carried, even though the page will not show them.
        Assert.Equal("Спряхме сайта.", bg);
        Assert.Equal("We took the site down.", en);
    }

    [Theory]
    // Nothing at all.
    [InlineData("")]
    [InlineData("   ")]
    // Not JSON.
    [InlineData("не е JSON")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    // JSON, but not the shape promised.
    [InlineData("[]")]
    [InlineData("\"hidden\"")]
    [InlineData("null")]
    // Incomplete: no state, or no mode to answer with.
    [InlineData("{}")]
    [InlineData("""{"mode":"maintenance"}""")]
    [InlineData("""{"visible":false}""")]
    // The right fields with the wrong types.
    [InlineData("""{"visible":"false","mode":"maintenance"}""")]
    [InlineData("""{"visible":0,"mode":"maintenance"}""")]
    [InlineData("""{"visible":false,"mode":503}""")]
    [InlineData("""{"visible":false,"mode":"maintenance","messageBg":42}""")]
    // A mode that says something else entirely.
    [InlineData("""{"visible":false,"mode":"forbidden"}""")]
    [InlineData("""{"visible":false,"mode":"404"}""")]
    // A field this contract has never heard of.
    [InlineData("""{"visible":false,"mode":"maintenance","surprise":1}""")]
    [InlineData("""{"status":"hidden"}""")]
    public void Повреден_отговор_не_може_да_спре_сайта(string body)
    {
        // The default is "visible". To hide the conference somebody has to send
        // a valid answer; a mangled or substituted one is dropped.
        Assert.False(RemoteControlResponse.TryParse(body, out _, out _, out _, out _, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    // ── The address ───────────────────────────────────────────────────

    [Theory]
    [InlineData("https://94.156.92.175.nip.io",         "https://94.156.92.175.nip.io/status")]
    [InlineData("https://94.156.92.175.nip.io/",        "https://94.156.92.175.nip.io/status")]
    // A URL that already names the endpoint is left alone rather than doubled.
    [InlineData("https://94.156.92.175.nip.io/status",  "https://94.156.92.175.nip.io/status")]
    [InlineData("http://127.0.0.1:8080",                "http://127.0.0.1:8080/status")]
    public void Адресът_на_status_се_строи_от_настройката(string url, string expected)
    {
        var options = new RemoteControlOptions { Url = url };

        Assert.True(options.Enabled);
        Assert.True(options.TryGetStatusUri(out var statusUri));
        Assert.Equal(expected, statusUri!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // A typo, not an address.
    [InlineData("94.156.92.175.nip.io")]
    [InlineData("не е адрес")]
    // Schemes an HttpClient cannot fetch.
    [InlineData("file:///opt/control/control.db")]
    [InlineData("ftp://94.156.92.175")]
    public void Негоден_адрес_не_пуска_услугата(string? url)
    {
        var options = new RemoteControlOptions { Url = url };

        // An unhandled exception in a hosted service stops the host, so a bad
        // address may never become a service. It becomes a warning and nothing
        // else; the site goes on working.
        Assert.False(options.Enabled);
        Assert.False(options.TryGetStatusUri(out _));
    }

    [Theory]
    // Nonsense in an interval is not a reason to keep the conference down, so
    // the numbers are clamped rather than thrown at somebody.
    [InlineData(0,   15)]
    [InlineData(-5,  15)]
    [InlineData(0.5, 15)]
    [InlineData(30,  30)]
    public void Безсмислен_интервал_пада_на_стойността_по_подразбиране(double configured, double expected)
    {
        var options = new RemoteControlOptions { PollSeconds = configured };
        Assert.Equal(expected, options.PollInterval.TotalSeconds);
    }

    [Fact]
    public void Отрицателен_праг_не_крие_сайта_завинаги()
    {
        var options = new RemoteControlOptions { StaleAfterMinutes = -1 };
        Assert.Equal(TimeSpan.Zero, options.StaleAfter);
    }

    [Fact]
    public void Липсващите_съобщения_не_правят_отговора_невалиден()
    {
        // The page has a text of its own to fall back on, so an answer without
        // messages still counts.
        Assert.True(RemoteControlResponse.TryParse(
            """{"visible":false,"mode":"error","messageBg":null}""",
            out var visible, out var mode, out var bg, out var en, out _));

        Assert.False(visible);
        Assert.Equal("error", mode);
        Assert.Equal(string.Empty, bg);
        Assert.Equal(string.Empty, en);
    }
}
