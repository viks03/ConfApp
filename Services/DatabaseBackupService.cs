using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using ConferenceApp.Models;
using ConferenceApp.Data;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConferenceApp.Services
{
    public class DatabaseBackupService : BackgroundService
    {
        private readonly ILogger<DatabaseBackupService> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IServiceScopeFactory _scopeFactory;

        // FIX: Четем от конфигурацията вместо hardcoded стойности
        private readonly string _dbFileName;
        private readonly int _keepMaxBackups;

        // Часове за бекъп (UTC)
        private static readonly int[] BackupHoursUtc = [3, 15];

        public DatabaseBackupService(
            ILogger<DatabaseBackupService> logger,
            IWebHostEnvironment env,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration)
        {
            _logger         = logger;
            _env            = env;
            _scopeFactory   = scopeFactory;
            _dbFileName     = configuration["BackupSettings:DbFileName"]       ?? "conferenceapp.db";
            _keepMaxBackups = int.TryParse(configuration["BackupSettings:KeepMaxBackups"], out int k) ? k : 14;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Database Backup Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                // FIX: Използваме UTC навсякъде — без смесване с local time
                var now     = DateTime.UtcNow;
                var nextRun = GetNextRunTime(now);
                var delay   = nextRun - now;

                _logger.LogInformation(
                    "Next database backup scheduled in {Hours:F1}h (at {Time:HH:mm} UTC).",
                    delay.TotalHours, nextRun);

                // FIX: Task.Delay в собствен try/catch за graceful спиране
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // FIX: OperationCanceledException се хваща отделно — не е грешка
                try
                {
                    await PerformBackupWithAuditAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Database Backup Service is stopping gracefully.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while backing up the database.");
                }
            }
        }

        private async Task PerformBackupWithAuditAsync(CancellationToken stoppingToken)
        {
            string sourcePath   = Path.Combine(_env.ContentRootPath, _dbFileName);
            string backupFolder = Path.Combine(_env.ContentRootPath, "backups");

            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning("Database file not found at {Path}. Backup skipped.", sourcePath);
                return;
            }

            Directory.CreateDirectory(backupFolder); // няма нужда от предварителна проверка

            // FIX: Timestamp в UTC за да съвпада с audit лога
            string timestamp      = DateTime.UtcNow.ToString("yyyyMMdd_HHmm");
            string backupFileName = $"{Path.GetFileNameWithoutExtension(_dbFileName)}_{timestamp}.db";
            string destPath       = Path.Combine(backupFolder, backupFileName);

            // ── FIX: SQLite Online Backup API ────────────────────────────────────
            // File.Copy върху жив SQLite файл може да даде корумпиран бекъп.
            // SQLite backup API изчаква свободен момент и копира консистентно.
            string connectionString = $"Data Source={sourcePath}";
            using (var source = new SqliteConnection(connectionString))
            using (var dest   = new SqliteConnection($"Data Source={destPath}"))
            {
                await source.OpenAsync(stoppingToken);
                await dest.OpenAsync(stoppingToken);
                source.BackupDatabase(dest);
            }

            _logger.LogInformation("Database backup created: {FileName}", backupFileName);

            // ── Audit log ────────────────────────────────────────────────────────
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                context.Set<AuditLog>().Add(new AuditLog
                {
                    UserEmail = "System",
                    Action    = "Database Backup",
                    IpAddress = "System",
                    Details   = $"Automatic backup created: {backupFileName}",
                    Timestamp = DateTime.UtcNow
                });

                await context.SaveChangesAsync(stoppingToken);
            }

            // ── Изчистване на стари бекъпи ───────────────────────────────────────
            // FIX: Сортираме по LastWriteTime — надеждно при всеки OS/filesystem
            var allBackups = new DirectoryInfo(backupFolder)
                .GetFiles("*.db")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (allBackups.Count > _keepMaxBackups)
            {
                foreach (var old in allBackups.Skip(_keepMaxBackups))
                {
                    // FIX: Изолиран try/catch — заключен файл не спира останалите
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
        }

        // ── Помощен метод — следващото UTC време за бекъп ───────────────────────
        private static DateTime GetNextRunTime(DateTime utcNow)
        {
            foreach (var hour in BackupHoursUtc)
            {
                var candidate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, hour, 0, 0, DateTimeKind.Utc);
                if (candidate > utcNow)
                    return candidate;
            }

            // Следващият ден в първия час
            return new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, BackupHoursUtc[0], 0, 0, DateTimeKind.Utc)
                .AddDays(1);
        }
    }
}