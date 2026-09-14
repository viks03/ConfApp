// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity;

namespace ConferenceApp.Data
{
    public static class DbInitializer
    {
        // Creates the single system administrator if it is not there, and
        // repairs it if it is. Runs on every startup, after the migrations.
        //
        // IConfiguration is a parameter because the password is a secret and
        // never lives in appsettings.json.
        public static async Task SeedUsersAsync(IServiceProvider services, IConfiguration configuration)
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

            // [A-12]: the only message from here used to be a Console.WriteLine.
            // Started as a service that goes to journald or the docker logs, not
            // to the application log, which is where somebody would look for
            // it.
            var logger = services.GetRequiredService<ILoggerFactory>()
                                 .CreateLogger("ConferenceApp.Data.DbInitializer");

            var adminEmail = "sys.auth_7x9b@conference.unwe.bg";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            // From user-secrets locally, from an environment variable on the
            // server.
            var adminPassword = configuration["AdminSettings:SystemAdminPassword"];

            if (adminUser == null)
            {
                if (!await roleManager.RoleExistsAsync("Admin"))
                {
                    var roleResult = await roleManager.CreateAsync(new IdentityRole("Admin"));

                    // The role is created before the user. If this fails and we
                    // carry on, the AddToRoleAsync below fails too — so it stops
                    // here, while the trail is still clear.
                    if (!roleResult.Succeeded)
                    {
                        throw new InvalidOperationException(
                            "Ролята „Admin“ не можа да бъде създадена: " +
                            Describe(roleResult));
                    }
                }

                var admin = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    FirstName = "System",
                    LastName = "Admin",
                    HasAcceptedGdpr = true,
                    CreatedAt = DateTime.UtcNow
                };

                if (!string.IsNullOrEmpty(adminPassword))
                {
                    var result = await userManager.CreateAsync(admin, adminPassword);

                    if (!result.Succeeded)
                    {
                        // [A-12]: there used to be no branch here. The "Admin"
                        // role existed, no administrator did, and the application
                        // started normally — /Admin asked for a sign-in for which
                        // there was no account. The usual cause is a password
                        // rejected by the policy in Program.cs (at least 8
                        // characters, a digit, a capital letter).
                        throw new InvalidOperationException(
                            $"Системният администратор ({adminEmail}) не можа да бъде създаден: " +
                            Describe(result) +
                            " Провери AdminSettings:SystemAdminPassword срещу политиката за пароли " +
                            "в Program.cs (минимум 8 символа, поне една цифра и поне една главна буква).");
                    }

                    var roleAssignment = await userManager.AddToRoleAsync(admin, "Admin");

                    if (!roleAssignment.Succeeded)
                    {
                        // A user without the role is even more misleading than no
                        // user at all: the sign-in works and the panel returns
                        // 403.
                        throw new InvalidOperationException(
                            $"Системният администратор ({adminEmail}) е създаден, но ролята „Admin“ " +
                            "не можа да му бъде дадена: " + Describe(roleAssignment) +
                            " Акаунтът съществува, но няма достъп до /Admin.");
                    }

                    logger.LogInformation(
                        "Системният администратор ({Email}) беше създаден и добавен в роля „Admin“.",
                        adminEmail);
                }
                else
                {
                    // A missing password is already reported at startup by the
                    // secrets check in Program.cs. Nothing is thrown here, so
                    // that a local environment without user-secrets still runs —
                    // but the line goes into the application log, not only to
                    // the console.
                    logger.LogError(
                        "AdminSettings:SystemAdminPassword не е зададена. Системният администратор " +
                        "({Email}) НЕ е създаден — /Admin ще иска вход, за който няма акаунт. " +
                        "Виж README, раздел \"Configuration & secrets\".",
                        adminEmail);
                }
            }
            else
            {
                // The administrator exists: make sure the address is confirmed,
                // because an unconfirmed one cannot sign in.
                if (!adminUser.EmailConfirmed)
                {
                    adminUser.EmailConfirmed = true;

                    var updateResult = await userManager.UpdateAsync(adminUser);

                    if (!updateResult.Succeeded)
                    {
                        logger.LogError(
                            "Не можах да потвърдя имейла на системния администратор ({Email}): {Errors}",
                            adminEmail, Describe(updateResult));
                    }
                    else
                    {
                        // [A-12]: this used to sit outside the if and ran on EVERY
                        // startup. A changed security stamp invalidates the
                        // administrator's cookie at the next check — with
                        // ValidationInterval at zero, on the very next request —
                        // so every deployment threw them out of the panel. It is
                        // now called only when something actually changed.
                        await userManager.UpdateSecurityStampAsync(adminUser);

                        logger.LogInformation(
                            "Имейлът на системния администратор ({Email}) беше потвърден.", adminEmail);
                    }
                }
            }
        }

        private static string Describe(IdentityResult result)
            => result.Errors.Any()
                ? string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
                : "(Identity не върна причина)";
    }
}
