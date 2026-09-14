// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.RemoteControl;

/// <summary>
/// Four starts of the application, each with a control server of its own,
/// because the four questions cannot be asked of one instance:
/// <list type="bullet">
/// <item>with no address configured nothing may ever be started;</item>
/// <item>with one, the switch has to work;</item>
/// <item>the silence rule needs a window short enough to cross in a test;</item>
/// <item>and the rule about a mangled answer only means anything on an
/// instance that has never been told to hide.</item>
/// </list>
/// <para>
/// Starting up costs seconds, so the four are shared between every test in the
/// class instead of being started per test.
/// </para>
/// </summary>
public sealed class RemoteControlRun : IAsyncLifetime
{
    /// <summary>One second between polls: the tests wait in seconds, not in quarters of a minute.</summary>
    public const double PollSeconds = 1;

    /// <summary>Two seconds of patience with a server that has stopped answering.</summary>
    public const double TimeoutSeconds = 2;

    /// <summary>
    /// Six seconds instead of an hour, for the one instance that has to outlive
    /// its own window. The rule is the same rule; only the number differs, and
    /// the number with its real value is checked in
    /// <see cref="RemoteControlRulesTests"/>.
    /// </summary>
    public const double ShortStaleMinutes = 0.1;

    /// <summary>How long a test will wait for a change to show up.</summary>
    public static readonly TimeSpan ReactionWindow =
        TimeSpan.FromSeconds(PollSeconds + TimeoutSeconds + 10);

    public ControlStub OffControl     { get; } = new();
    public ControlStub Control        { get; } = new();
    public ControlStub StaleControl   { get; } = new();
    public ControlStub CorruptControl { get; } = new();

    public StartupProbe Off     { get; private set; } = null!;
    public StartupProbe On      { get; private set; } = null!;
    public StartupProbe Stale   { get; private set; } = null!;
    public StartupProbe Corrupt { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        OffControl.Start();
        Control.Start();
        StaleControl.Start();
        CorruptControl.Start();

        // The control server is answering "hidden" from the first second, so
        // that the instance below cannot accidentally pass by never having been
        // told anything.
        CorruptControl.AnswerWith("""{"visible":false,"mode":"maintenance","surprise":1}""");

        // ── Switched off: an address is deliberately NOT given ─────────
        Off = await StartupProbe.StartAsync("remote-off", settings =>
        {
            settings["RemoteControl:Url"] = string.Empty;
            settings["RemoteControl:Key"] = ControlStub.Key;
        });

        // ── Switched on ───────────────────────────────────────────────
        On = await StartupProbe.StartAsync("remote-on", settings =>
        {
            settings["RemoteControl:Url"]               = Control.BaseUrl;
            settings["RemoteControl:Key"]               = ControlStub.Key;
            settings["RemoteControl:PollSeconds"]       = PollSeconds.ToString(Invariant);
            settings["RemoteControl:TimeoutSeconds"]    = TimeoutSeconds.ToString(Invariant);
            settings["RemoteControl:StaleAfterMinutes"] = "60";
        });

        // ── Switched on, with a window that can be outlived ────────────
        Stale = await StartupProbe.StartAsync("remote-stale", settings =>
        {
            settings["RemoteControl:Url"]               = StaleControl.BaseUrl;
            settings["RemoteControl:Key"]               = ControlStub.Key;
            settings["RemoteControl:PollSeconds"]       = PollSeconds.ToString(Invariant);
            settings["RemoteControl:TimeoutSeconds"]    = TimeoutSeconds.ToString(Invariant);
            settings["RemoteControl:StaleAfterMinutes"] = ShortStaleMinutes.ToString(Invariant);
        });

        // ── Switched on, but talking to something it cannot understand ─
        Corrupt = await StartupProbe.StartAsync("remote-corrupt", settings =>
        {
            settings["RemoteControl:Url"]               = CorruptControl.BaseUrl;
            settings["RemoteControl:Key"]               = ControlStub.Key;
            settings["RemoteControl:PollSeconds"]       = PollSeconds.ToString(Invariant);
            settings["RemoteControl:TimeoutSeconds"]    = TimeoutSeconds.ToString(Invariant);
            settings["RemoteControl:StaleAfterMinutes"] = "60";
        });
    }

    private static readonly System.Globalization.CultureInfo Invariant =
        System.Globalization.CultureInfo.InvariantCulture;

    public async Task DisposeAsync()
    {
        await OffControl.DisposeAsync();
        await Control.DisposeAsync();
        await StaleControl.DisposeAsync();
        await CorruptControl.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class RemoteControlCollection : ICollectionFixture<RemoteControlRun>
{
    public const string Name = "RemoteControl";
}
