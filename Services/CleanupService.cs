using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

namespace ConferenceApp.Services
{
    public class CleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CleanupService> _logger;

        public CleanupService(IServiceProvider serviceProvider, ILogger<CleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("System Cleanup Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCleanupTaskAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Нормално спиране на приложението — не е грешка
                    _logger.LogInformation("Cleanup Service is stopping gracefully.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during system cleanup.");
                }

                try
                {
                    // Променено на 1 час (беше 24 часа), за да поддържа админ панела 
                    // чист и да отразява изтеклите крипто поръчки по-бързо.
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Приложението спря докато е чакало — изход без грешка
                    break;
                }
            }
        }

        private async Task RunCleanupTaskAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();

            var context     = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var env         = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

            // ════════════════════════════════════════════════════════════════
            // 1. ПОЧИСТВАНЕ НА ИЗТЕКЛИ КРИПТО ПОРЪЧКИ
            // ════════════════════════════════════════════════════════════════
            var expiredCryptoOrders = await context.CryptoOrders
                .Where(o => o.Status == "InProcess" && o.ExpiresAt.HasValue && o.ExpiresAt.Value < DateTime.UtcNow)
                .ToListAsync(stoppingToken);

            if (expiredCryptoOrders.Count > 0)
            {
                foreach (var order in expiredCryptoOrders)
                {
                    order.Status = "Expired";
                }

                context.Set<AuditLog>().Add(new AuditLog
                {
                    UserId    = null,
                    UserEmail = "System",
                    Action    = "Crypto Cleanup",
                    IpAddress = "System",
                    Details   = $"Auto-expired {expiredCryptoOrders.Count} abandoned crypto order(s).",
                    Timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("System Cleanup: Marked {Count} crypto orders as Expired.", expiredCryptoOrders.Count);
            }

            // ════════════════════════════════════════════════════════════════
            // 2. ПОЧИСТВАНЕ НА НЕПОТВЪРДЕНИ И ИЗОСТАВЕНИ ПРОФИЛИ (> 24 часа)
            // ════════════════════════════════════════════════════════════════
            var deadline = DateTime.UtcNow.AddHours(-24);

            var unverifiedUsers = await context.Users
                .Where(u => !u.EmailConfirmed && u.CreatedAt < deadline)
                .ToListAsync(stoppingToken);

            int deletedCount = 0;

            if (unverifiedUsers.Count > 0)
            {
                _logger.LogInformation("Found {Count} abandoned accounts. Starting cleanup...", unverifiedUsers.Count);

                foreach (var user in unverifiedUsers)
                {
                    // ── Изтриване на качения файл ─────────────────────────────────
                    bool fileDeleted = false;
                    if (!string.IsNullOrEmpty(user.PaperFilePath))
                    {
                        try
                        {
                            var relativePath = user.PaperFilePath.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar);
                            var physicalPath = Path.Combine(env.WebRootPath, relativePath);

                            var fullPath    = Path.GetFullPath(physicalPath);
                            var webRootFull = Path.GetFullPath(env.WebRootPath);

                            if (fullPath.StartsWith(webRootFull, StringComparison.OrdinalIgnoreCase)
                                && File.Exists(fullPath))
                            {
                                File.Delete(fullPath);
                                fileDeleted = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not delete file for user {UserId}. Continuing cleanup.", user.Id);
                        }
                    }

                    // ── Изтриване на OTP кодовете ─────────────────────────────────
                    var userOtps = await context.Set<OtpCode>()
                        .Where(o => o.Email == user.Email)
                        .ToListAsync(stoppingToken);

                    if (userOtps.Count > 0)
                    {
                        context.RemoveRange(userOtps);
                        await context.SaveChangesAsync(stoppingToken);
                    }

                    // ── Audit log за изтрития потребител ─────────────────────────
                    var refNumber = string.IsNullOrEmpty(user.ReferenceNumber)
                        ? "N/A"
                        : user.ReferenceNumber;

                    context.Set<AuditLog>().Add(new AuditLog
                    {
                        UserId    = null,
                        UserEmail = user.Email ?? "Unknown",
                        Action    = "System Cleanup",
                        IpAddress = "System",
                        Details   = $"Deleted abandoned account ({user.FirstName} {user.LastName}). Ref: {refNumber} | File removed: {(fileDeleted ? "Yes" : "No")}",
                        Timestamp = DateTime.UtcNow
                    });

                    // ── Изтриване на потребителя ──────────────────────────────────
                    var result = await userManager.DeleteAsync(user);

                    if (result.Succeeded)
                    {
                        deletedCount++;
                    }
                    else
                    {
                        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                        _logger.LogWarning("Failed to delete user {UserId}: {Errors}", user.Id, errors);
                    }
                }
            }

            // ════════════════════════════════════════════════════════════════
            // 3. ФИНАЛИЗИРАНЕ И ЗАПАЗВАНЕ В БАЗАТА
            // ════════════════════════════════════════════════════════════════
            
            // Записваме обобщаващ лог само ако нещо реално е било изтрито/променено
            if (deletedCount > 0 || expiredCryptoOrders.Count > 0)
            {
                context.Set<AuditLog>().Add(new AuditLog
                {
                    UserId    = null,
                    UserEmail = "System",
                    Action    = "Cleanup Summary",
                    IpAddress = "System",
                    Details   = $"Cleanup cycle finished. Removed {deletedCount} abandoned accounts. Auto-expired {expiredCryptoOrders.Count} crypto orders.",
                    Timestamp = DateTime.UtcNow
                });
            }

            // Запазваме всички промени (включително сменените статуси на крипто поръчките)
            await context.SaveChangesAsync(stoppingToken);

            if (deletedCount > 0 || expiredCryptoOrders.Count > 0)
            {
                _logger.LogInformation("System Cleanup cycle completed successfully.");
            }
        }
    }
}