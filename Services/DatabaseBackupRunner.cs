// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Microsoft.Data.Sqlite;

namespace ConferenceApp.Services
{
    /// <summary>Who asked for the copy. It decides only the wording of the
    /// audit row — the copy itself is identical either way.</summary>
    public enum BackupTrigger
    {
        /// <summary>The 03:00 / 15:00 UTC window.</summary>
        Scheduled,

        /// <summary>The button in the Health tab, pressed by an administrator.</summary>
        Manual
    }

    /// <summary>
    /// The result of one attempt. <see cref="Busy"/> is deliberately separate
    /// from a failure: a second press while the first copy is still running is
    /// not an error, it is the guard doing its job.
    /// </summary>
    public sealed record BackupOutcome
    {
        public bool Success  { get; private init; }
        public bool Busy     { get; private init; }
        public string FileName { get; private init; } = string.Empty;
        public double SizeMb { get; private init; }
        public string? Error { get; private init; }

        public static BackupOutcome Created(string fileName, double sizeMb)
            => new() { Success = true, FileName = fileName, SizeMb = sizeMb };

        public static BackupOutcome Failed(string error)
            => new() { Error = error };

        public static BackupOutcome AlreadyRunning()
            => new() { Busy = true, Error = "Копие вече се прави в момента." };
    }

    /// <summary>
    /// One backup of the database, on demand.
    /// <para>
    /// The copying used to live inside <see cref="DatabaseBackupService"/>,
    /// where only the scheduler could reach it. It sits here instead so that the
    /// button in the Health tab and the 03:00 / 15:00 window run <b>exactly</b>
    /// the same code — the same consistency guarantee, the same rotation, the
    /// same audit row. A second implementation for the manual case would be a
    /// second chance to get a restorable copy wrong.
    /// </para>
    /// <para>
    /// Singleton: it holds the gate that keeps two copies from running at once.
    /// </para>
    /// </summary>
    public interface IDatabaseBackupRunner
    {
        /// <summary>The database file being copied, absolute.</summary>
        string DatabaseFile { get; }

        /// <summary>The folder the copies go into, absolute.</summary>
        string BackupFolder { get; }

        /// <summary>
        /// Makes one copy, rotates the old ones and writes the audit row.
        /// Returns what happened instead of throwing — apart from cancellation,
        /// which propagates so that an application shutdown is not mistaken for
        /// a failed backup.
        /// </summary>
        /// <param name="actor">Who is recorded in the audit: <c>"System"</c> for
        /// the schedule, the administrator's name for the button.</param>
        Task<BackupOutcome> RunAsync(BackupTrigger trigger, string actor, CancellationToken ct = default);

        /// <summary>
        /// Creates the backup folder if it is not there. Called at startup so
        /// that the Health card never reports a missing folder during the hours
        /// between startup and the first window.
        /// </summary>
        void EnsureFolder();
    }

    public sealed class DatabaseBackupRunner : IDatabaseBackupRunner
    {
        private readonly ILogger<DatabaseBackupRunner> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IBackupStatus _status;

        private readonly string _dbFilePath;
        private readonly string _backupFolder;
        private readonly int _keepMaxBackups;

        // One copy at a time, process-wide.
        //
        // Two of them would write the same <name>.db.tmp from two SQLite backup
        // handles and race over the File.Move that renames it — the surviving
        // file would be neither copy in full. The button is the reason this can
        // happen at all: the schedule cannot overlap with itself, but a double
        // click, or two administrators in two browsers, can.
        //
        // The wait is zero rather than blocking: the second press is answered at
        // once with "one is already being made", instead of silently queueing a
        // copy nobody asked for twice.
        private readonly SemaphoreSlim _gate = new(1, 1);

        public string DatabaseFile => _dbFilePath;
        public string BackupFolder => _backupFolder;

        public DatabaseBackupRunner(
            ILogger<DatabaseBackupRunner> logger,
            IWebHostEnvironment env,
            IServiceScopeFactory scopeFactory,
            IBackupStatus status,
            IConfiguration configuration)
        {
            _logger       = logger;
            _scopeFactory = scopeFactory;
            _status       = status;
            // [S-02]: the path comes from the connection string, not from a
            // second setting of its own.
            _dbFilePath     = DatabaseLocation.ResolveDatabaseFile(configuration);
            _backupFolder   = DatabaseLocation.ResolveBackupFolder(env);
            _keepMaxBackups = int.TryParse(configuration["BackupSettings:KeepMaxBackups"], out int k) ? k : 14;
        }

        public void EnsureFolder()
        {
            try
            {
                Directory.CreateDirectory(_backupFolder);
            }
            catch (Exception ex)
            {
                // A folder that cannot be created must not take the service
                // down — the next window tries again.
                _logger.LogError(ex, "Could not create the backup folder at startup.");
            }
        }

        public async Task<BackupOutcome> RunAsync(
            BackupTrigger trigger, string actor, CancellationToken ct = default)
        {
            if (!await _gate.WaitAsync(0, ct))
                return BackupOutcome.AlreadyRunning();

            try
            {
                return await CopyAsync(trigger, actor, ct);
            }
            catch (OperationCanceledException)
            {
                // A shutdown, or the administrator closing the tab. Not a failed
                // backup, and not something to record as one.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while backing up the database.");
                _status.RecordFailure(ex);
                return BackupOutcome.Failed($"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<BackupOutcome> CopyAsync(
            BackupTrigger trigger, string actor, CancellationToken ct)
        {
            string sourcePath = _dbFilePath;

            if (!File.Exists(sourcePath))
            {
                // [S-02]: this used to be a LogWarning. A missing database file
                // here means no backups are being made at all — that is an
                // error, not a note.
                _logger.LogError(
                    "Database file not found at {Path}. Backup skipped — от този момент няма нови копия. " +
                    "Пътят идва от ConnectionStrings:DefaultConnection (или от {OverrideKey}, ако е зададена).",
                    sourcePath, DatabaseLocation.OverrideKey);

                var missing = $"Файлът на базата не е намерен: {sourcePath}";
                _status.RecordFailure(missing);
                return BackupOutcome.Failed(missing);
            }

            Directory.CreateDirectory(_backupFolder);

            // The timestamp is UTC so that a file name lines up with the audit
            // row written for it.
            string timestamp      = DateTime.UtcNow.ToString("yyyyMMdd_HHmm");
            string backupFileName = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{timestamp}.db";
            string destPath       = Path.Combine(_backupFolder, backupFileName);

            // [S-01]: the copy used to be written straight under its final
            // name. With the process killed or the disk full, the unfinished file
            // stayed in the folder, and being the newest it was reported as the
            // latest backup by every check, the Health tab included. It is now
            // written as .tmp and renamed only on success; an unfinished file is
            // a valid backup to nobody, because it does not match `*.db`.
            string tempPath = destPath + DatabaseLocation.PartialExtension;

            // Leftovers from an earlier interrupted copy. Deleted before the new
            // one so they do not pile up — and because File.Move below needs a
            // clear path.
            CleanupPartials();

            // ── SQLite online backup API ─────────────────────────────────────────
            // File.Copy over a live SQLite file can produce a corrupt backup. The
            // backup API waits for a quiet moment and copies consistently.
            string connectionString = $"Data Source={sourcePath}";
            try
            {
                // Pooling=False on purpose: otherwise Dispose returns the
                // connection to the pool, the handle to the temporary file stays
                // open and the File.Move below fails. It is disabled for this one
                // copy only; the application's live connections are untouched.
                using (var source = new SqliteConnection(connectionString))
                using (var dest   = new SqliteConnection($"Data Source={tempPath};Pooling=False"))
                {
                    await source.OpenAsync(ct);
                    await dest.OpenAsync(ct);
                    source.BackupDatabase(dest);
                }

                File.Move(tempPath, destPath, overwrite: true);
            }
            catch
            {
                TryDeletePartial(tempPath);
                throw;
            }

            var sizeMb = new FileInfo(destPath).Length / 1024.0 / 1024;
            _logger.LogInformation("Database backup created: {FileName} ({SizeMb:0.0} MB)", backupFileName, sizeMb);

            _status.RecordSuccess(backupFileName);

            await WriteAuditAsync(trigger, actor, backupFileName, sizeMb);
            Rotate(sourcePath);

            return BackupOutcome.Created(backupFileName, sizeMb);
        }

        /// <summary>
        /// Every backup leaves a row, whoever asked for it — the schedule under
        /// "System", the button under the administrator's name, with the file in
        /// both cases. Restoring later starts from this row: it says which copy
        /// exists and who caused it to exist.
        /// </summary>
        private async Task WriteAuditAsync(BackupTrigger trigger, string actor, string fileName, double sizeMb)
        {
            // A scope of its own: the runner is a singleton and AuditService is
            // scoped. Under a request the IP still resolves, because
            // IHttpContextAccessor follows the async flow rather than the scope.
            using var scope = _scopeFactory.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

            if (trigger == BackupTrigger.Manual)
            {
                await audit.LogAsync(null, actor, "Database Backup",
                    $"Ръчно копие от панела: {fileName} ({sizeMb:0.0} MB)");
                return;
            }

            await audit.LogAsync(null, "System", "Database Backup",
                $"Automatic backup created: {fileName} ({sizeMb:0.0} MB)", "System");
        }

        // ── Rotation ─────────────────────────────────────────────────────────
        // [S-01]: the pattern used to be "*.db" across the whole folder, so a
        // copy left by hand before a migration (conferenceapp_predeploy.db)
        // counted towards the limit and could be deleted too. Only the files
        // this service made itself are considered now; they are ordered by
        // LastWriteTime, which is reliable on every OS and file system.
        private void Rotate(string sourcePath)
        {
            var allBackups = DatabaseLocation.ListAutomaticBackups(_backupFolder, sourcePath);

            if (allBackups.Count <= _keepMaxBackups) return;

            foreach (var old in allBackups.Skip(_keepMaxBackups))
            {
                // One try/catch per file: a locked file must not stop the
                // rest of the rotation.
                try
                {
                    old.Delete();
                    _logger.LogInformation("Old backup deleted: {FileName}", old.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete old backup: {FileName}", old.Name);
                }
            }
        }

        /// <summary>
        /// Deletes the <c>.db.tmp</c> files left behind by an interrupted copy.
        /// They are not valid backups, but they take up as much room as the ones
        /// that are.
        /// </summary>
        private void CleanupPartials()
        {
            try
            {
                foreach (var partial in Directory.GetFiles(_backupFolder, "*.db" + DatabaseLocation.PartialExtension))
                {
                    if (TryDeletePartial(partial))
                        _logger.LogWarning(
                            "Намерено недовършено копие от предишен опит: {FileName}. Изтрито.",
                            Path.GetFileName(partial));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not scan the backup folder for partial files.");
            }
        }

        private bool TryDeletePartial(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete partial backup file: {Path}", path);
                return false;
            }
        }
    }
}
