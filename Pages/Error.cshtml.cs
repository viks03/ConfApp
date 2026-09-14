// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;

namespace ConferenceApp.Pages
{
    // Also the target of UseStatusCodePagesWithReExecute, so this page renders
    // for a 404 as well as for an unhandled exception.
    // No caching: an error page cached by a proxy would be served to somebody
    // whose request was fine.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [IgnoreAntiforgeryToken]
    public class ErrorModel : PageModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        private readonly ILogger<ErrorModel> _logger;

        public ErrorModel(ILogger<ErrorModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            // The id shown to the visitor is the one the log line carries, so
            // that "I got an error" can be matched to an entry in the file.
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        }
    }
}