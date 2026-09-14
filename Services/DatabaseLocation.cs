// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Microsoft.Data.Sqlite;

namespace ConferenceApp.Services
{
    /// <summary>
    /// [S-02]: the path to the database used to be written down in two
    /// independent places — the application read it from the connection string,
    /// the backup service from <c>BackupSettings:DbFileName</c> joined onto
    /// <c>ContentRootPath</c>. Nothing tied the two together, so changing only
    /// one of them stopped the backups silently:
    /// <c>PerformBackupWithAuditAsync</c> returned early with a single
    /// LogWarning.
    /// <para>
    /// From here on the source is the connection string — the same one EF Core
    /// hands to SQLite. <c>BackupSettings:DbFileName</c> stays as an explicit
    /// override for whoever deliberately wants a different file; the value in
    /// appsettings.json is now empty so that it cannot override by accident.
    /// </para>
    /// </summary>
    public static class DatabaseLocation
    {
        public const string OverrideKey = "BackupSettings:DbFileName";

        /// <summary>
        /// The absolute path to the database file, as SQLite sees it.
        /// </summary>
        /// <remarks>
        /// A relative <c>Data Source</c> is resolved against the process working
        /// directory, not against the content root — so it is resolved the same
        /// way here. With the default ASP.NET Core startup the two coincide
        /// (<c>CreateBuilder</c> takes the content root from the current
        /// directory); when they do not, the right answer is the file SQLite
        /// itself opens.
        /// </remarks>
        public static string ResolveDatabaseFile(IConfiguration configuration)
        {
            var source = configuration[OverrideKey];

            if (string.IsNullOrWhiteSpace(source))
            {
                var connectionString = configuration.GetConnectionString("DefaultConnection");

                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    try
                    {
                        source = new SqliteConnectionStringBuilder(connectionString).DataSource;
                    }
                    catch (ArgumentException)
                    {
                        // An unparsable connection string. The application will
                        // not start anyway; this only avoids throwing before it
                        // gets the chance to say so properly.
                        source = null;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(source))
                source = "conferenceapp.db";

            return Path.GetFullPath(source);
        }

        /// <summary>The folder holding the backups.</summary>
        public static string ResolveBackupFolder(IWebHostEnvironment env)
            => Path.Combine(env.ContentRootPath, "backups");

        /// <summary>The extension an unfinished copy carries while it is being
        /// written, so that a half-written file is never mistaken for a backup —
        /// see [S-01].</summary>
        public const string PartialExtension = ".tmp";

        /// <summary>
        /// The copies made by <see cref="DatabaseBackupService"/>:
        /// <c>&lt;database&gt;_yyyyMMdd_HHmm.db</c>.
        /// </summary>
        /// <remarks>
        /// [S-01]: rotation used to look at <c>"*.db"</c> across the whole
        /// folder, so a copy left by hand before a migration counted towards the
        /// limit and could be deleted. The check is on the shape of the
        /// timestamp rather than on the prefix alone — otherwise
        /// <c>conferenceapp_predeploy.db</c> would still pass.
        /// </remarks>
        public static IReadOnlyList<FileInfo> ListAutomaticBackups(string backupFolder, string databaseFilePath)
        {
            var prefix = Path.GetFileNameWithoutExtension(databaseFilePath) + "_";

            return new DirectoryInfo(backupFolder)
                .GetFiles(prefix + "*.db")
                .Where(f => IsAutomaticBackupName(f.Name, prefix))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }

        private static bool IsAutomaticBackupName(string fileName, string prefix)
        {
            // <prefix>yyyyMMdd_HHmm.db → 13 characters between the prefix and
            // the extension, the underscore at index 8 included.
            const int stampLength = 13;

            if (!fileName.StartsWith(prefix, StringComparison.Ordinal)) return false;
            if (!fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) return false;

            var stamp = fileName[prefix.Length..^3];
            if (stamp.Length != stampLength || stamp[8] != '_') return false;

            for (int i = 0; i < stampLength; i++)
            {
                if (i == 8) continue;
                if (!char.IsAsciiDigit(stamp[i])) return false;
            }

            return true;
        }
    }
}
