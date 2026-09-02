using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity;

namespace ConferenceApp.Data
{
    public static class DbInitializer
    {
        // Добавяме IConfiguration като параметър, за да четем тайните
        public static async Task SeedUsersAsync(IServiceProvider services, IConfiguration configuration)
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

            var adminEmail = "sys.auth_7x9b@conference.unwe.bg";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            // Взимаме паролата от конфигурацията (локално от Secret Manager, на сървъра от Environment Variables)
            var adminPassword = configuration["AdminSettings:SystemAdminPassword"];

            if (adminUser == null)
            {
                if (!await roleManager.RoleExistsAsync("Admin"))
                {
                    await roleManager.CreateAsync(new IdentityRole("Admin"));
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

                // Проверяваме дали паролата е намерена в конфигурацията
                if (!string.IsNullOrEmpty(adminPassword))
                {
                    var result = await userManager.CreateAsync(admin, adminPassword);
                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(admin, "Admin");
                    }
                }
                else
                {
                    // Добра практика е да логнем предупреждение, ако липсва парола
                    Console.WriteLine("ПРЕДУПРЕЖДЕНИЕ: AdminSettings:SystemAdminPassword не е намерена. Админът не е създаден.");
                }
            }
            else
            {
                // СИЛОВО ПОПРАВЯНЕ: Ако админът съществува, гарантираме, че е потвърден
                if (!adminUser.EmailConfirmed)
                {
                    adminUser.EmailConfirmed = true;
                    await userManager.UpdateAsync(adminUser);
                }
                
                // Обновяваме SecurityStamp, за да е валиден за логин
                await userManager.UpdateSecurityStampAsync(adminUser);
            }
        }
    }
}