using System.Globalization;
using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// ---------------- 1. DATABASE ----------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ---------------- 2. IDENTITY ----------------
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // OTP верификацията е в твоя код — Identity не трябва да пуска неверифицирани потребители.
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail   = false;
    options.User.RequireUniqueEmail        = true;

    options.Password.RequireDigit           = true;
    options.Password.RequiredLength         = 8;  // Минимум 8 символа
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase       = true;
    options.Password.RequireLowercase       = false; 

    options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromHours(12);
    options.Lockout.MaxFailedAccessAttempts = 3;
    options.Lockout.AllowedForNewUsers      = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// ---------------- 3. LOCALIZATION & RAZOR PAGES ----------------
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

builder.Services.AddControllersWithViews();

// ---------------- 4. SERVICES ----------------
builder.Services.AddHttpContextAccessor();

// БЪГ ФИКС: EmailSender беше Scoped, но вече се извиква и от background
// задачи (виж IBackgroundTaskQueue), които се изпълняват СЛЕД като request
// scope-ът е приключил. Singleton е коректният lifetime — класът няма
// per-request състояние, само конфигурация и logger.
builder.Services.AddSingleton<EmailSender>();

// ── Имейл система (Services/Email/) ───────────────────────────────────
builder.Services.AddSingleton<ConferenceApp.Services.Email.IEmailTemplateRenderer,
                              ConferenceApp.Services.Email.EmailTemplateRenderer>();
builder.Services.AddSingleton<ConferenceApp.Services.Email.IEmailNotificationSettings,
                              ConferenceApp.Services.Email.EmailNotificationSettings>();
// ── Payment Control (Services/PaymentGateSettings.cs) ─────────────────
builder.Services.AddSingleton<ConferenceApp.Services.IPaymentGateSettings,
                              ConferenceApp.Services.PaymentGateSettings>();
// ── Health Check (Services/Health/) ───────────────────────────────────
// Singleton: услугата няма per-request състояние, а чете конфигурация,
// диск и опашката, които също са singleton. За базата отваря собствен
// scope (виж IServiceScopeFactory вътре).
// Автоматичен одит на административните действия. Scoped, защото ползва
// ApplicationDbContext — филтърът живее колкото заявката.
builder.Services.AddScoped<ConferenceApp.Services.Audit.AdminAuditFilter>();

builder.Services.AddHttpClient();   // за проверката на Go28
builder.Services.AddSingleton<ConferenceApp.Services.Health.IHealthCheckService,
                              ConferenceApp.Services.Health.HealthCheckService>();

builder.Services.AddSingleton<ConferenceApp.Services.Email.IMailComposer,
                              ConferenceApp.Services.Email.MailComposer>();
builder.Services.AddScoped<AuditService>();

// Опашка за бавна работа (SMTP), за да не блокира HTTP отговора —
// виж коментара в Services/IBackgroundTaskQueue.cs.
builder.Services.AddSingleton<ConferenceApp.Services.IBackgroundTaskQueue, ConferenceApp.Services.BackgroundTaskQueue>();
builder.Services.AddHostedService<ConferenceApp.Services.QueuedHostedService>();

// Go28 крипто gateway — HttpClient с типизиран service (ДОБАВЕН TIMEOUT)
builder.Services.AddHttpClient<Go28Service>(client =>
{
    // Спира да чака след 5 секунди, за да не блокира зареждането на страницата, 
    // ако ИТ отделът е блокирал изходящия трафик.
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Stripe — плащания с карта, Apple Pay, Google Pay
builder.Services.AddScoped<ConferenceApp.Services.StripeService>();

// Background services
builder.Services.AddHostedService<CleanupService>();
builder.Services.AddHostedService<DatabaseBackupService>();

// ---------------- 4.5. DATA PROTECTION ----------------
// Запазва ключовете на диска — без това cookies се инвалидират при всеки рестарт.
var keysPath = builder.Environment.IsProduction()
    ? "/var/www/conferenceapp/DataProtection-Keys"
    : Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys");

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("ConferenceApp");

// ---------------- 5. COOKIES ----------------
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath       = "/Login";
    options.LogoutPath      = "/Logout";
    options.AccessDeniedPath = "/AccessDenied";

    options.ExpireTimeSpan    = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;

    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;

    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

var app = builder.Build();

// ---------------- 6. CONFIGURE LOCALIZATION OPTIONS ----------------
var supportedCultures = new[] { new CultureInfo("bg"), new CultureInfo("en") };
var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture  = new RequestCulture("bg"),
    SupportedCultures      = supportedCultures,
    SupportedUICultures    = supportedCultures
};

localizationOptions.RequestCultureProviders = new List<IRequestCultureProvider>
{
    new CookieRequestCultureProvider(),
    new QueryStringRequestCultureProvider(),
    new AcceptLanguageHeaderRequestCultureProvider()
};

// ---------------- 7. HTTP PIPELINE ----------------
// Оптимизирана и сигурна конфигурация за Forwarded Headers зад Reverse Proxy (Nginx / Docker / Cloudflare)
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};

// Изчистваме фабричните ограничения за мрежи и проксита. 
// Това позволява на ASP.NET Core да прочете реалното IP от X-Forwarded-For,
// дори когато заявката минава през вътрешна Docker мрежа или междинен контейнер.
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeadersOptions);

// --------------------------------------------------------------------
// Cloudflare Real IP Middleware
// Ако заявката идва от Cloudflare, взимаме абсолютно точното IP на клиента
// --------------------------------------------------------------------
app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cloudflareIp))
    {
        if (System.Net.IPAddress.TryParse(cloudflareIp.ToString(), out var ipAddress))
        {
            context.Connection.RemoteIpAddress = ipAddress;
        }
    }
    await next();
});
// --------------------------------------------------------------------

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error");
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization(localizationOptions);

app.UseRouting();

// ── Raw body за Stripe webhook ────────────────────────────────────────
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/stripe/webhook"))
        context.Request.EnableBuffering();
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// ---------------- 8. SEED DATA & MIGRATIONS ----------------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    
    // 1. Първо създаваме/обновяваме базата данни (Таблиците)
    var context = services.GetRequiredService<ApplicationDbContext>();
    context.Database.Migrate(); 
    
    // 2. Чак след това създаваме Админа вътре
    await DbInitializer.SeedUsersAsync(services, builder.Configuration);
}

// ---------------- 9. ENDPOINTS & REDIRECTS ----------------
app.MapGet("/Identity/Account/Login", context =>
{
    context.Response.Redirect("/Login");
    return Task.CompletedTask;
});

app.MapControllers();
app.MapRazorPages();

app.Run();