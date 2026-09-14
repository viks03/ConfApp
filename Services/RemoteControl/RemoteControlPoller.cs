// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Microsoft.Extensions.Options;

namespace ConferenceApp.Services.RemoteControl
{
    /// <summary>
    /// Asks the control server every <c>PollSeconds</c> and keeps the answer in
    /// memory.
    /// <para>
    /// It never touches a request. A visitor's page load does not wait for this
    /// service, does not start a poll of its own and never learns that the
    /// control server is slow — if the server goes quiet, the background service
    /// waits, the site does not. That is the whole reason the state lives in
    /// memory rather than being fetched on demand.
    /// </para>
    /// <para>
    /// A failed cycle is not an event. The connection drops, the certificate
    /// expires, the VPS reboots — the service logs it, keeps the last known
    /// state and tries again on the next tick. What happens after a long silence
    /// is decided in <see cref="RemoteControlState.Decide"/>, not here.
    /// </para>
    /// </summary>
    public sealed class RemoteControlPoller : BackgroundService
    {
        /// <summary>The named client, so the timeout and the body limit sit in one place.</summary>
        public const string HttpClientName = "remote-control";

        /// <summary>The header the shared secret travels in.</summary>
        public const string KeyHeader = "X-Control-Key";

        private readonly IHttpClientFactory _clients;
        private readonly RemoteControlState _state;
        private readonly RemoteControlOptions _options;
        private readonly ILogger<RemoteControlPoller> _logger;
        private readonly Uri _statusUri;

        // Both are only about how loudly to log, and both are touched from the
        // one loop below. A server that has been down since the morning should
        // not write the same line four times a minute until somebody notices the
        // disk is full.
        private bool _lastCycleFailed;
        private bool _staleLogged;

        public RemoteControlPoller(
            IHttpClientFactory clients,
            RemoteControlState state,
            IOptions<RemoteControlOptions> options,
            ILogger<RemoteControlPoller> logger)
        {
            _clients = clients;
            _state   = state;
            _options = options.Value;
            _logger  = logger;

            // Read once, here, where a bad value is still an ordinary startup
            // error and not an exception inside a hosted service. The service is
            // only ever registered for an address that parses (see Program.cs).
            if (!_options.TryGetStatusUri(out var statusUri))
                throw new InvalidOperationException(
                    $"RemoteControl:Url не е годен адрес: „{_options.Url}\".");

            _statusUri = statusUri!;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Nothing here may hold up the start of the application: until the
            // first await returns, the host is still waiting on StartAsync.
            await Task.Yield();

            _logger.LogInformation(
                "Отдалеченият превключвател е включен. Адрес: {Url}, интервал: {Interval}s, таймаут: {Timeout}s, " +
                "състоянието се пази {Stale} минути без отговор.",
                _statusUri, _options.PollInterval.TotalSeconds,
                _options.Timeout.TotalSeconds, _options.StaleAfter.TotalMinutes);

            if (string.IsNullOrWhiteSpace(_options.Key))
            {
                // Not a reason to stop: the site works without the switch. It is
                // a reason to say so once, loudly, because every answer will be
                // a 401 and nothing will ever change the state.
                _logger.LogWarning(
                    "RemoteControl:Url е зададен, но ключът липсва. Задай го през променливата на средата " +
                    "RemoteControl__Key — иначе {Url} ще отговаря 401 и състоянието няма да се сменя.",
                    _statusUri);
            }

            using var timer = new PeriodicTimer(_options.PollInterval);

            try
            {
                // The first poll happens now rather than one interval from now:
                // a restart while the site is hidden should not show it for
                // fifteen seconds.
                do
                {
                    await PollOnceAsync(stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // A normal shutdown.
            }
            catch (Exception ex)
            {
                // Nothing below should be able to get here — PollOnceAsync
                // swallows everything it can be handed. This is the last net,
                // and it is here because an unhandled exception in a hosted
                // service stops the HOST: the switch would take the conference
                // site down with it. Better a switch that has stopped working
                // and says so, with the site up.
                _logger.LogError(ex,
                    "Отдалеченият превключвател спря заради грешка и няма да пита повече. " +
                    "Сайтът продължава да работи; състоянието остава такова, каквото беше.");
            }

            _logger.LogInformation("Отдалеченият превключвател спря.");
        }

        /// <summary>
        /// One cycle. Never throws — a background service that dies on a network
        /// error would leave the site frozen on whatever it last heard.
        /// </summary>
        internal async Task PollOnceAsync(CancellationToken stoppingToken)
        {
            try
            {
                var client = _clients.CreateClient(HttpClientName);

                using var request = new HttpRequestMessage(HttpMethod.Get, _statusUri);
                if (!string.IsNullOrWhiteSpace(_options.Key))
                    request.Headers.TryAddWithoutValidation(KeyHeader, _options.Key);

                using var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseContentRead, stoppingToken);

                if (!response.IsSuccessStatusCode)
                {
                    Failed($"отговор {(int)response.StatusCode}");
                    return;
                }

                var body = await response.Content.ReadAsStringAsync(stoppingToken);

                if (!RemoteControlResponse.TryParse(
                        body, out var visible, out var mode,
                        out var messageBg, out var messageEn, out var reason))
                {
                    Failed(reason);
                    return;
                }

                var previous = _state.Current;
                var snapshot = _state.Report(visible, mode, messageBg, messageEn);

                if (_lastCycleFailed)
                {
                    _logger.LogInformation(
                        "Контролният сървър отговаря отново. Състояние: {State}.",
                        Describe(snapshot));
                    _lastCycleFailed = false;
                    _staleLogged     = false;
                }
                else if (previous is null ||
                         previous.Visible != snapshot.Visible ||
                         !string.Equals(previous.Mode, snapshot.Mode, StringComparison.Ordinal))
                {
                    // A change of state is worth a line. Fifteen seconds of
                    // "still the same" is not.
                    _logger.LogInformation(
                        "Състоянието на сайта се смени на {State}.", Describe(snapshot));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                // The client's own timeout arrives as this, not as a timeout.
                Failed($"няма отговор до {_options.Timeout.TotalSeconds:F0}s");
            }
            catch (HttpRequestException ex)
            {
                Failed(ex.Message);
            }
            catch (Exception ex)
            {
                Failed($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// The first failure after a good cycle is a warning; the ones after it
        /// are debug. The state is left exactly as it was.
        /// </summary>
        private void Failed(string reason)
        {
            if (_lastCycleFailed)
            {
                _logger.LogDebug("Контролният сървър пак не отговори: {Reason}.", reason);
                NoteIfStateHasGoneStale();
                return;
            }

            _lastCycleFailed = true;

            var known = _state.Current;
            _logger.LogWarning(
                "Контролният сървър не отговори: {Reason}. Пази се последното известно състояние ({State}).",
                reason, known is null ? "няма такова, сайтът е видим" : Describe(known));

            NoteIfStateHasGoneStale();
        }

        /// <summary>
        /// The moment the silence outlives StaleAfterMinutes the site comes back
        /// on its own. That is by design, but it is a change nobody asked for, so
        /// it is said out loud — once.
        /// </summary>
        private void NoteIfStateHasGoneStale()
        {
            if (_staleLogged) return;

            var known = _state.Current;
            if (known is null || known.Visible) return;
            if (_state.Decide(_options.StaleAfter).Hide) return;

            _staleLogged = true;
            _logger.LogWarning(
                "Контролният сървър мълчи от {Since:u} — повече от {Stale} минути. " +
                "Последното състояние беше „{State}“, но сайтът се показва отново.",
                known.LastSuccessAt, _options.StaleAfter.TotalMinutes, Describe(known));
        }

        private static string Describe(RemoteControlSnapshot snapshot) =>
            snapshot.Visible ? "видим" : $"скрит ({snapshot.Mode})";
    }
}
