// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.RemoteControl;

/// <summary>
/// The switch end to end: a real control server on a real socket, a real
/// application with the real pipeline, and real seconds.
/// </summary>
[Collection(RemoteControlCollection.Name)]
public class RemoteControlSwitchTests
{
    private readonly RemoteControlRun _run;

    public RemoteControlSwitchTests(RemoteControlRun run) => _run = run;

    // ── Switched off ──────────────────────────────────────────────────

    [Fact]
    public async Task Празен_адрес_не_пуска_нищо()
    {
        Assert.Null(_run.Off.StartupError);

        // The other instance is polling its own server right now, so the counter
        // below is measuring silence rather than a stub nobody could reach.
        await WaitUntilAsync(
            () => _run.Control.StatusRequests > 0,
            RemoteControlRun.ReactionWindow,
            "контролният сървър на включеното копие да бъде запитан");

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Zero requests, zero load, zero risk.
        Assert.Equal(0, _run.OffControl.StatusRequests);

        // And the background service does not even say hello.
        Assert.DoesNotContain("Отдалеченият превключвател е включен", _run.Off.ReadLog());

        // The site works, as it did before any of this existed.
        using var client = _run.Off.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Сгрешен_адрес_не_сваля_сайта()
    {
        // A hosted service that throws stops the host. A typo in a setting would
        // then take the conference site down — which is the exact opposite of
        // what a switch like this is for. It has to be a warning and nothing
        // more. This one start is its own, because it is the START that is
        // being asserted on.
        var probe = await StartupProbe.StartAsync("remote-bad-url", settings =>
        {
            settings["RemoteControl:Url"] = "94.156.92.175.nip.io";
            settings["RemoteControl:Key"] = ControlStub.Key;
        });

        Assert.Null(probe.StartupError);

        using var client = probe.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);

        // And it is readable rather than silent: the switch is not running.
        Assert.NotNull(await probe.WaitForLogLineAsync(
            "RemoteControl:Url не е годен адрес", TimeSpan.FromSeconds(20)));
    }

    // ── Switched on ───────────────────────────────────────────────────

    [Fact]
    public async Task Тайната_пътува_в_заглавен_ред()
    {
        await WaitUntilAsync(
            () => _run.Control.StatusRequests > 0,
            RemoteControlRun.ReactionWindow,
            "първата заявка към контролния сървър");

        Assert.Equal(ControlStub.Key, _run.Control.LastKeyHeader);

        // Not one request went out without it — a 401 on every cycle would mean
        // the switch is wired up and permanently deaf.
        Assert.Equal(0, _run.Control.UnauthorizedRequests);
    }

    [Fact]
    public async Task Скрит_сайт_връща_503_на_всяка_страница()
    {
        _run.Control.SpeakAgain();
        _run.Control.Hide("maintenance");

        using var client = _run.On.NewClient();

        await WaitForStatusAsync(client, "/", HttpStatusCode.ServiceUnavailable);

        // Not just the home page: nothing past the middleware runs at all.
        foreach (var path in new[] { "/", "/Register", "/Payment", "/Program", "/Admin", "/Login", "/no-such-page" })
        {
            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("300", response.Headers.GetValues("Retry-After").Single());

            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Извършваме кратка поддръжка", html);

            // The page is built from nothing: not the layout, not the database,
            // not a stylesheet somewhere else.
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<link",   html, StringComparison.OrdinalIgnoreCase);
        }

        _run.Control.Show();
        await WaitForStatusAsync(client, "/", HttpStatusCode.OK);
    }

    [Fact]
    public async Task Режим_error_връща_500()
    {
        _run.Control.SpeakAgain();
        _run.Control.Hide("error");

        using var client = _run.On.NewClient();

        try
        {
            await WaitForStatusAsync(client, "/", HttpStatusCode.InternalServerError);
        }
        finally
        {
            _run.Control.Hide("maintenance");
            _run.Control.Show();
            await WaitForStatusAsync(client, "/", HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Режим_notfound_връща_гол_404()
    {
        _run.Control.SpeakAgain();
        _run.Control.SetMessages("Спряхме сайта до понеделник.", "We took the site down until Monday.");
        _run.Control.Hide("notfound");

        using var client = _run.On.NewClient();

        try
        {
            await WaitForStatusAsync(client, "/", HttpStatusCode.NotFound);

            // A browser that has asked for English in every way it can. The 404
            // is the same for it as for everybody else.
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-GB,en;q=0.9");

            foreach (var path in new[] { "/", "/Register", "/Program", "/Admin", "/no-such-page" })
            {
                var response = await client.GetAsync(path);

                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

                // No Retry-After: an address that does not exist has no business
                // knowing when to come back.
                Assert.False(response.Headers.Contains("Retry-After"));

                var html = await response.Content.ReadAsStringAsync();

                Assert.Equal(
                    "<html><head><title>404 Not Found</title></head>\n" +
                    "<body><h1>Not Found</h1><p>The requested URL was not found on this server.</p></body></html>",
                    html);

                // Neither text reaches the visitor, and nothing of ours does.
                Assert.DoesNotContain("понеделник", html);
                Assert.DoesNotContain("Monday",     html);
                Assert.DoesNotContain("robots",     html, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("<style",     html, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            _run.Control.SetMessages(
                "Извършваме кратка поддръжка. Ще се върнем скоро.",
                "We are performing brief maintenance. We will be back shortly.");
            _run.Control.Hide("maintenance");
            _run.Control.Show();
            await WaitForStatusAsync(client, "/", HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Webhook_минава_и_при_notfound()
    {
        _run.Control.SpeakAgain();
        _run.Control.Hide("notfound");

        using var client = _run.On.NewClient();
        await WaitForStatusAsync(client, "/", HttpStatusCode.NotFound);

        try
        {
            // The exception holds under every mode: a payment already on its way
            // back has to be able to be confirmed.
            var stripe = await client.PostAsync("/api/stripe/webhook",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, stripe.StatusCode);

            var crypto = await client.PostAsync("/api/crypto/webhook",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.NotEqual(HttpStatusCode.NotFound, crypto.StatusCode);
        }
        finally
        {
            _run.Control.Hide("maintenance");
            _run.Control.Show();
            await WaitForStatusAsync(client, "/", HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Скрит_сайт_пак_приема_webhook_за_плащане()
    {
        _run.Control.SpeakAgain();
        _run.Control.Hide("maintenance");

        using var client = _run.On.NewClient();
        await WaitForStatusAsync(client, "/", HttpStatusCode.ServiceUnavailable);

        try
        {
            // A payment that set off before the switch was thrown has to be able
            // to come back. 400 is the controller answering "no signature" — it
            // is the controller being REACHED that is the point here.
            var stripe = await client.PostAsync("/api/stripe/webhook",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, stripe.StatusCode);

            var crypto = await client.PostAsync("/api/crypto/webhook",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.NotEqual(HttpStatusCode.ServiceUnavailable, crypto.StatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, crypto.StatusCode);
        }
        finally
        {
            _run.Control.Show();
            await WaitForStatusAsync(client, "/", HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Смяната_се_отразява_до_един_цикъл()
    {
        _run.Control.SpeakAgain();
        _run.Control.Show();

        using var client = _run.On.NewClient();
        await WaitForStatusAsync(client, "/", HttpStatusCode.OK);

        var thrown = DateTime.UtcNow;
        _run.Control.Hide("maintenance");
        await WaitForStatusAsync(client, "/", HttpStatusCode.ServiceUnavailable);
        var hiddenAfter = DateTime.UtcNow - thrown;

        _run.Control.Show();
        await WaitForStatusAsync(client, "/", HttpStatusCode.OK);

        // One poll plus one timeout, with room for a loaded machine — the point
        // is that a change lands within a cycle and not on the next restart.
        Assert.True(hiddenAfter < RemoteControlRun.ReactionWindow,
            $"Смяната се отрази чак след {hiddenAfter.TotalSeconds:F1}s.");
    }

    [Fact]
    public async Task Езикът_на_посетителя_решава_кой_текст_вижда()
    {
        _run.Control.SpeakAgain();
        _run.Control.SetMessages("Спряхме за малко.", "We are down for a moment.");
        _run.Control.Hide("maintenance");

        using var client = _run.On.NewClient();
        await WaitForStatusAsync(client, "/", HttpStatusCode.ServiceUnavailable);

        try
        {
            using var english = new HttpRequestMessage(HttpMethod.Get, "/");
            english.Headers.Add("Accept-Language", "en-GB,en;q=0.9");
            var englishPage = await (await client.SendAsync(english)).Content.ReadAsStringAsync();
            Assert.Contains("We are down for a moment.", englishPage);

            // No preference at all: Bulgarian.
            var defaultPage = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
            Assert.Contains("Спряхме за малко.", defaultPage);
        }
        finally
        {
            _run.Control.SetMessages(
                "Извършваме кратка поддръжка. Ще се върнем скоро.",
                "We are performing brief maintenance. We will be back shortly.");
            _run.Control.Show();
            await WaitForStatusAsync(client, "/", HttpStatusCode.OK);
        }
    }

    // ── Silence ───────────────────────────────────────────────────────

    [Fact]
    public async Task Мълчание_под_прага_пази_последното_състояние()
    {
        _run.Control.SpeakAgain();
        _run.Control.Hide("maintenance");

        using var client = _run.On.NewClient();
        await WaitForStatusAsync(client, "/", HttpStatusCode.ServiceUnavailable);

        try
        {
            // The control server falls off the network. The window on this
            // instance is an hour, so nothing about the site changes.
            _run.Control.GoSilent();
            _run.Control.ResetCounters();

            await WaitUntilAsync(
                () => _run.Control.StatusRequests >= 3,
                RemoteControlRun.ReactionWindow,
                "три неуспешни цикъла");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/")).StatusCode);
        }
        finally
        {
            _run.Control.SpeakAgain();
            _run.Control.Show();
            await WaitForStatusAsync(client, "/", HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Дълго_мълчание_показва_сайта()
    {
        _run.StaleControl.SpeakAgain();
        _run.StaleControl.Hide("maintenance");

        using var client = _run.Stale.NewClient();
        await WaitForStatusAsync(client, "/", HttpStatusCode.ServiceUnavailable);

        // And now the control server is gone for longer than the window.
        // Deliberately: it is likelier that it has fallen over than that
        // somebody wants the conference hidden and cannot reach it to say so.
        _run.StaleControl.GoSilent();

        await WaitForStatusAsync(client, "/", HttpStatusCode.OK,
            TimeSpan.FromMinutes(RemoteControlRun.ShortStaleMinutes) + RemoteControlRun.ReactionWindow);

        // The site coming back on its own is a change nobody asked for, so the
        // log has to say it happened and why.
        Assert.NotNull(await _run.Stale.WaitForLogLineAsync(
            "Контролният сървър мълчи от", TimeSpan.FromSeconds(10)));

        // It comes back the moment the server does.
        _run.StaleControl.SpeakAgain();
        await WaitForStatusAsync(client, "/", HttpStatusCode.ServiceUnavailable);

        _run.StaleControl.Show();
        await WaitForStatusAsync(client, "/", HttpStatusCode.OK);
    }

    // ── An answer that cannot be trusted ──────────────────────────────

    [Fact]
    public async Task Непознато_поле_не_спира_сайта()
    {
        // This instance has been told "hidden" since it started — but in a body
        // carrying a field the contract has never heard of. It has therefore
        // never been hidden, and must not be.
        using var client = _run.Corrupt.NewClient();

        await WaitUntilAsync(
            () => _run.CorruptControl.StatusRequests >= 3,
            RemoteControlRun.ReactionWindow,
            "три цикъла с неразбираем отговор");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Theory]
    // A valid state, but not with a 200.
    [InlineData("""{"visible":false,"mode":"maintenance"}""", 500)]
    [InlineData("""{"visible":false,"mode":"maintenance"}""", 404)]
    [InlineData("""{"visible":false,"mode":"maintenance"}""", 302)]
    // A 200 carrying something else.
    [InlineData("<html>502 Bad Gateway</html>", 200)]
    [InlineData("", 200)]
    [InlineData("""{"visible":"false"}""", 200)]
    [InlineData("""{"visible":false,"mode":"forbidden"}""", 200)]
    public async Task Отговор_който_не_става_не_спира_сайта(string body, int statusCode)
    {
        _run.CorruptControl.AnswerWith(body, statusCode);
        _run.CorruptControl.ResetCounters();

        await WaitUntilAsync(
            () => _run.CorruptControl.StatusRequests >= 3,
            RemoteControlRun.ReactionWindow,
            "три цикъла с негоден отговор");

        using var client = _run.Corrupt.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    // ── Waiting ───────────────────────────────────────────────────────

    private async Task WaitForStatusAsync(
        HttpClient client, string path, HttpStatusCode expected, TimeSpan? timeout = null)
    {
        var window   = timeout ?? RemoteControlRun.ReactionWindow;
        var deadline = DateTime.UtcNow + window;
        HttpStatusCode last = 0;

        while (DateTime.UtcNow < deadline)
        {
            last = (await client.GetAsync(path)).StatusCode;
            if (last == expected) return;
            await Task.Delay(200);
        }

        Assert.Fail($"{path} остана {(int)last} вместо {(int)expected} до {window.TotalSeconds:F0}s.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(200);
        }

        Assert.Fail($"Не се стигна до: {what} (до {timeout.TotalSeconds:F0}s).");
    }
}
