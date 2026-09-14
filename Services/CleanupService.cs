// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services.Files;
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
        // One hour rather than the original 24: an expired crypto order should
        // disappear from the admin panel within the hour, not the next day.
        //
        // [S-04]: the number is published through ICleanupStatus.Interval so
        // that the health check can judge when a missed cycle is a problem,
        // instead of carrying a copy of it.
        public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CleanupService> _logger;
        private readonly ICleanupStatus _status;

        public CleanupService(IServiceProvider serviceProvider, ILogger<CleanupService> logger, ICleanupStatus status)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _status = status;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "System Cleanup Service started. Interval: {Hours}h.", Interval.TotalHours);
            _status.MarkStarted(Interval);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await RunCleanupTaskAsync(stoppingToken);
                    }
                    // [S-09] The `when` clause matters: without it a cancellation
                    // from any other source (a nested token, an HTTP client
                    // timeout) left the loop for good, while the log line claimed
                    // the service had stopped "gracefully". Together with [S-04]
                    // that was invisible.
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // A normal application shutdown, not an error.
                        _logger.LogInformation("Cleanup Service is stopping gracefully.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred during system cleanup.");
                        _status.RecordFailure(ex);
                    }

                    try
                    {
                        await Task.Delay(Interval, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // The application stopped while waiting — leave without
                        // an error.
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected cancellation while waiting for the next cleanup cycle.");
                        _status.RecordFailure(ex);
                    }
                }
            }
            finally
            {
                // Even on an unforeseen exit the Health tab has to show
                // "stopped" rather than the service's last good state.
                _status.MarkStopped();
                _logger.LogInformation("System Cleanup Service stopped.");
            }
        }

        private async Task RunCleanupTaskAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();

            var context     = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var env         = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
            var uploadPaths = scope.ServiceProvider.GetRequiredService<IUploadPaths>();
            // The same scope, therefore the same ApplicationDbContext as
            // `context` above — the audit rows go out with the single
            // SaveChangesAsync at the end.
            var audit       = scope.ServiceProvider.GetRequiredService<AuditService>();

            // ════════════════════════════════════════════════════════════════
            // 1. EXPIRED CRYPTO ORDERS
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

                audit.Add(null, "System", "Crypto Cleanup",
                    $"Auto-expired {expiredCryptoOrders.Count} abandoned crypto order(s).", "System");

                _logger.LogInformation("System Cleanup: Marked {Count} crypto orders as Expired.", expiredCryptoOrders.Count);
            }

            // ════════════════════════════════════════════════════════════════
            // 2. UNCONFIRMED, ABANDONED ACCOUNTS (older than 24 hours)
            // ════════════════════════════════════════════════════════════════
            var deadline = DateTime.UtcNow.AddHours(-24);

            // [AD-02] The condition used to be EmailConfirmed and CreatedAt
            // alone. The "Email address verified" checkbox in the panel writes
            // straight into EmailConfirmed, so an administrator clearing it to
            // make someone confirm their address again sent a participant who
            // had paid — along with their paper and every trace of it — to
            // irreversible deletion on the next cycle. Someone who has paid is
            // not an "abandoned account".
            //
            // [T-35] Those two conditions catch COMPLETED payment only. Someone
            // who has clicked "I have made the transfer" and is waiting for an
            // administrator has PaymentStatus = "Pending" and an empty PaidAt —
            // and so went the same way. The same for a crypto order whose money
            // is on the network at that very moment: the webhook arrives minutes
            // later and there is no longer anyone to credit. A payment in flight
            // is not an "abandoned account" either.
            //
            // The protection is not permanent: an expired, unpaid order becomes
            // "Expired" in step 1 above, so the account behind it comes back into
            // scope on the next cycle — an hour later, not never.
            var unverifiedUsers = await context.Users
                .Where(u => !u.EmailConfirmed
                         && u.CreatedAt < deadline
                         && u.PaymentStatus != "Confirmed"
                         && u.PaidAt == null
                         && u.IbanTransferSubmittedAt == null
                         && !context.CryptoOrders.Any(o =>
                                o.UserId == u.Id
                                && (o.Status == "InProcess" || o.Status == "Confirmed")))
                .ToListAsync(stoppingToken);

            int deletedCount = 0;

            if (unverifiedUsers.Count > 0)
            {
                _logger.LogInformation("Found {Count} abandoned accounts. Starting cleanup...", unverifiedUsers.Count);

                foreach (var user in unverifiedUsers)
                {
                    // ── Uploaded files ───────────────────────────────────────────
                    // [F-06]: only the paper used to be deleted. The verification
                    // document stayed on disk with no row in the database
                    // pointing at it — that is, nobody could find it any more.
                    int filesDeleted = 0;

                    foreach (var relative in new[] { user.PaperFilePath, user.VerificationDocumentPath })
                    {
                        if (string.IsNullOrEmpty(relative)) continue;

                        try
                        {
                            var fullPath = uploadPaths.ToPhysical(relative);

                            if (fullPath != null && File.Exists(fullPath))
                            {
                                File.Delete(fullPath);
                                filesDeleted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not delete file for user {UserId}. Continuing cleanup.", user.Id);
                        }
                    }

                    // ── OTP codes ────────────────────────────────────────────────
                    // Matched by address, not by user id: the codes are issued
                    // before an account exists and carry no foreign key.
                    var userOtps = await context.Set<OtpCode>()
                        .Where(o => o.Email == user.Email)
                        .ToListAsync(stoppingToken);

                    if (userOtps.Count > 0)
                    {
                        context.RemoveRange(userOtps);
                        await context.SaveChangesAsync(stoppingToken);
                    }

                    // ── The audit row for the deleted account ────────────────────
                    // Written before the delete: afterwards the name, the address
                    // and the reference number are gone.
                    var refNumber = string.IsNullOrEmpty(user.ReferenceNumber)
                        ? "N/A"
                        : user.ReferenceNumber;

                    audit.Add(null, user.Email ?? "Unknown", "System Cleanup",
                        $"Deleted abandoned account ({user.FirstName} {user.LastName}). Ref: {refNumber} | Files removed: {filesDeleted}",
                        "System");

                    // ── The account itself ───────────────────────────────────────
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
            // 3. EXPIRED OTP CODES (more than 7 days past expiry)
            // ════════════════════════════════════════════════════════════════
            // [D-09] Codes used to be deleted only through a user who was being
            // deleted (above, and in the admin panel). Codes sent to an address
            // that never became an account — or whose account is confirmed —
            // were never deleted at all, while the table is queried on every
            // sign-in and on every visit to /Verification.
            //
            // The codes are six digits and live for 15 minutes, so seven days
            // past expiry there is nothing left to keep. The retention is not
            // zero so that there is still a trace when someone asks why they
            // could not sign in yesterday.
            var otpDeadline = DateTime.UtcNow.AddDays(-7);

            var expiredOtpCount = await context.Set<OtpCode>()
                .Where(o => o.ExpirationTime < otpDeadline)
                .ExecuteDeleteAsync(stoppingToken);

            if (expiredOtpCount > 0)
            {
                _logger.LogInformation("System Cleanup: Deleted {Count} expired OTP code(s).", expiredOtpCount);
            }

            // ════════════════════════════════════════════════════════════════
            // 4. SUMMARY AND SAVE
            // ════════════════════════════════════════════════════════════════

            // [S-04]: this row used to be written only `if (deletedCount > 0
            // || ...)`. It is, however, the only source of "Last cleanup" in the
            // panel (Areas/Admin/Pages/Index.cshtml.cs), so a stopped service and
            // a quiet week looked the same. It is now written every cycle — one
            // row an hour — and the date in the panel means what it says.
            audit.Add(null, "System", "Cleanup Summary",
                $"Cleanup cycle finished. Removed {deletedCount} abandoned accounts. Auto-expired {expiredCryptoOrders.Count} crypto orders. Deleted {expiredOtpCount} expired OTP codes.",
                "System");

            // One save for the whole cycle: the expired order statuses and both
            // audit rows.
            await context.SaveChangesAsync(stoppingToken);

            // The same in the log: an empty cycle at Debug level, so that the
            // file does not take 24 lines a day for nothing, and a cycle that did
            // something at Information.
            if (deletedCount > 0 || expiredCryptoOrders.Count > 0 || expiredOtpCount > 0)
            {
                _logger.LogInformation(
                    "System Cleanup cycle completed successfully. Accounts removed: {Accounts}, crypto orders expired: {Orders}, OTP codes deleted: {Otps}.",
                    deletedCount, expiredCryptoOrders.Count, expiredOtpCount);
            }
            else
            {
                _logger.LogDebug("System Cleanup cycle completed successfully. Nothing to do.");
            }

            _status.RecordSuccess(deletedCount, expiredCryptoOrders.Count);
        }
    }
}