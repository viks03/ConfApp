// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Microsoft.Extensions.Options;

namespace ConferenceApp.Services.RemoteControl
{
    /// <summary>
    /// Stops every request while the site is switched off, and answers with
    /// <see cref="MaintenancePage"/> instead.
    /// <para>
    /// It sits immediately after <c>UseStaticFiles</c> and before
    /// <c>UseRouting</c>: the files of the page itself are already served by
    /// then, and nothing beyond this point — no route, no rate limiter, no
    /// authentication, no page, no database — is reached at all.
    /// </para>
    /// <para>
    /// It reads one reference from memory and compares two timestamps. There is
    /// no network call, no lock and no I/O on the path of a request, which is
    /// what lets it sit in front of every page without costing anything while
    /// the site is up.
    /// </para>
    /// </summary>
    public sealed class MaintenanceMiddleware
    {
        /// <summary>
        /// The two paths that get through even while the site is off.
        /// <para>
        /// A payment that set off before the switch was thrown has to be able to
        /// come back and be confirmed. Blocking the webhook does not postpone the
        /// payment — the money has moved — it only leaves somebody who has paid
        /// sitting as unconfirmed, and Stripe gives up retrying after a while.
        /// </para>
        /// <para>
        /// Static files need no exception of their own: they are served before
        /// this middleware ever runs.
        /// </para>
        /// </summary>
        public static readonly string[] AlwaysAllowed =
        {
            "/api/stripe/webhook",
            "/api/crypto/webhook"
        };

        /// <summary>How long a visitor is asked to wait before coming back.</summary>
        public const string RetryAfterSeconds = "300";

        private readonly RequestDelegate _next;
        private readonly RemoteControlState _state;
        private readonly RemoteControlOptions _options;

        public MaintenanceMiddleware(
            RequestDelegate next,
            RemoteControlState state,
            IOptions<RemoteControlOptions> options)
        {
            _next    = next;
            _state   = state;
            _options = options.Value;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (IsAlwaysAllowed(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var decision = _state.Decide(_options.StaleAfter);

            if (!decision.Hide)
            {
                await _next(context);
                return;
            }

            var english = MaintenancePage.PrefersEnglish(context.Request);
            var html    = MaintenancePage.Render(decision, english);

            context.Response.StatusCode  = decision.StatusCode;
            context.Response.ContentType = "text/html; charset=utf-8";

            // Retry-After turns the 503 into "come back later" rather than
            // "gone" — but on the 404 it would be the one thing that gives the
            // switch away, an address that does not exist yet knows when to try
            // again. Under notfound the visitor gets no such hint.
            if (decision.StatusCode != StatusCodes.Status404NotFound)
                context.Response.Headers.RetryAfter = RetryAfterSeconds;

            // no-store stays on all three: it keeps a proxy from serving this
            // page on after the site is switched back on, and a 404 that outlives
            // the switch would be the worse kind of leftover.
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma       = "no-cache";

            // The body is written here on purpose: with one,
            // UseStatusCodePagesWithReExecute leaves the response alone. Without
            // one it would re-run the request through /Error — a Razor page on
            // _Layout, over the database, which is exactly what must not be
            // touched while the site is off.
            await context.Response.WriteAsync(html, context.RequestAborted);
        }

        private static bool IsAlwaysAllowed(PathString path)
        {
            foreach (var allowed in AlwaysAllowed)
            {
                if (path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
