// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace ConferenceApp.Services.Files
{
    /// <summary>
    /// The single place that turns "relative path to an uploaded file" into a
    /// physical path and back. Until [F-05] every call site had its own version:
    /// three rules on write, five on read, and only one of the eight checked
    /// where the result actually pointed.
    /// </summary>
    public interface IUploadPaths
    {
        /// <summary>
        /// The relative path to store in the database: never with a leading
        /// slash, always with <c>/</c> as the separator.
        /// </summary>
        string ToRelative(params string[] segments);

        /// <summary>
        /// Relative path → physical path. Resolved against wwwroot, or against
        /// the private root for the folders holding personal data (see [F-02]).
        /// Returns <c>null</c> if the result falls outside the chosen root.
        /// </summary>
        string? ToPhysical(string? relative);

        /// <summary>
        /// Creates the target directory if it does not exist and returns its
        /// physical path.
        /// </summary>
        string EnsureDirectory(params string[] segments);

        /// <summary>The root the folders with personal data live under.</summary>
        string PrivateRoot { get; }
    }

    public sealed class UploadPathService : IUploadPaths
    {
        /// <summary>
        /// The two folders that hold personal data and therefore live outside
        /// wwwroot — participants' papers and scanned verification documents
        /// ([F-02]). The relative paths in the database do not change; only the
        /// root they are resolved against does.
        /// </summary>
        private static readonly string[] PrivatePrefixes =
        {
            "uploads/papers26/",
            "uploads/submitted-documents/"
        };

        private readonly string _webRoot;

        public UploadPathService(IWebHostEnvironment env, IConfiguration config)
        {
            _webRoot = env.WebRootPath;

            var configured = config["Uploads:PrivateRoot"];
            PrivateRoot = !string.IsNullOrWhiteSpace(configured)
                ? configured
                : Path.Combine(env.ContentRootPath, "App_Data");
        }

        public string PrivateRoot { get; }

        public string ToRelative(params string[] segments) =>
            string.Join('/', segments
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Replace('\\', '/').Trim('/')));

        public string EnsureDirectory(params string[] segments)
        {
            var relative = ToRelative(segments);
            var dir = ToPhysical(relative)
                      ?? throw new InvalidOperationException(
                          $"Невалидна директория за качване: '{relative}'.");

            Directory.CreateDirectory(dir);
            return dir;
        }

        public string? ToPhysical(string? relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return null;

            // Leading '~' and slashes are stripped so that Path.Combine does not
            // treat the path as absolute and drop the root entirely; the
            // separator is normalised at the same time.
            var normalized = relative.Replace('\\', '/').TrimStart('~', '/');
            if (normalized.Length == 0) return null;

            var root = IsPrivate(normalized) ? PrivateRoot : _webRoot;

            // The same pattern as in CleanupService: GetFullPath resolves any
            // "..", and only then is the result checked to be under the root.
            // Checking before resolving would accept "uploads/../../etc/passwd".
            var fullPath = Path.GetFullPath(
                Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));

            var rootFull = Path.GetFullPath(root);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
                rootFull += Path.DirectorySeparatorChar;

            return fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;
        }

        // Matches both the folder itself ("uploads/papers26") and a file inside
        // it — EnsureDirectory passes the former, a read passes the latter.
        private static bool IsPrivate(string normalizedRelative) =>
            PrivatePrefixes.Any(p =>
                normalizedRelative.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                || normalizedRelative.Equals(p.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }
}
