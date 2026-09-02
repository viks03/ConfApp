using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceApp.Pages
{
    // Добавяме атрибут за сигурност, който позволява само POST заявки за изход, 
    // за да предотвратим злонамерени опити за отписване чрез обикновен линк (CSRF защита).
    [IgnoreAntiforgeryToken] 
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LogoutModel> _logger;

        public LogoutModel(
            SignInManager<ApplicationUser> signInManager, 
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ILogger<LogoutModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
            _logger = logger;
        }

        // Този метод разрушава сесията и пренасочва към /Login
        public async Task<IActionResult> OnPostAsync()
        {
            // 1. Взимаме текущия логнат потребител преди да разрушим сесията
            var user = await _userManager.GetUserAsync(User);
            
            if (user != null)
            {
                // 2. ЗАПИСВАМЕ В ОДИТА НА АНГЛИЙСКИ ЕЗИК
                _context.Set<AuditLog>().Add(new AuditLog 
                { 
                    UserId = user.Id, 
                    UserEmail = user.Email ?? "Unknown", 
                    Action = "Logout", 
                    Details = "User successfully logged out.", 
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown" 
                });
                await _context.SaveChangesAsync();
            }

            // 3. Отписваме потребителя
            await _signInManager.SignOutAsync();
            
            // Изчистваме кеша на браузъра за тази сесия (допълнителна сигурност)
            HttpContext.Response.Cookies.Delete(".AspNetCore.Identity.Application");
            
            _logger.LogInformation("User logged out and redirected to Login.");
            
            return RedirectToPage("/Login");
        }

        // Ако потребителят достъпи /Logout директно през URL (GET), 
        // го пренасочваме към метода за изход (POST).
        public async Task<IActionResult> OnGetAsync()
        {
            return await OnPostAsync();
        }
    }
}