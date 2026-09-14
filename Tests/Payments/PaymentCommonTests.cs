// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Payments;

/// <summary>
/// Part 2, the shared ground: the free forms of participation, the three tiers,
/// and how a price changed in the admin panel shows up on the page.
/// </summary>
[Collection(AppCollection.Name)]
public class PaymentCommonTests : IAsyncLifetime
{
    private readonly AppFixture _app;
    private readonly List<string> _created = new();

    public PaymentCommonTests(AppFixture app) => _app = app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _created)
            await _app.Db.DeleteParticipantAsync(email);
    }

    private async Task<ConferenceApp.Models.ApplicationUser> NewParticipantAsync(string partForm = "1")
    {
        var email = $"common-{Guid.NewGuid():N}@example.test";
        _created.Add(email);
        return await _app.Db.CreateParticipantAsync(email, partForm);
    }

    // ════════════════════════════════════════════════════════════════════
    // The free forms of participation
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("2")]   // a student or doctoral candidate: subsidised
    [InlineData("4")]   // a journalist: pays nothing
    public async Task Безплатна_форма_не_вижда_бутон_за_плащане_в_профила(string partForm)
    {
        var user = await NewParticipantAsync(partForm);

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var html = await session.Client.GetStringAsync("/Profile");

        // Not a single link to the payment page. The button to /SubmitDocuments
        // uses the same class, which is why the address is what is checked.
        Assert.DoesNotContain("/Payment/", html);
        Assert.DoesNotContain("asp-page=\"/Payment\"", html);
    }

    [Fact]
    public async Task Никъде_не_се_показва_нулева_сума()
    {
        // [P-03] The free tiers used to show "Pay €0.00" and then charge €120 by
        // card or €60 in crypto.
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        foreach (var slug in new[] { "viewer", "student", "earlybird" })
        {
            var html = await session.Client.GetStringAsync($"/Payment/{slug}");
            Assert.DoesNotContain("€0.00", html);
        }
    }

    [Fact]
    public async Task Безплатните_нива_нямат_бутон_на_Attend()
    {
        using var client = _app.NewClient();
        var html = await client.GetStringAsync("/Attend");

        // Only the payable tiers lead to /Payment.
        var payable = (await _app.Db.ReadAsync(db =>
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(db.TicketTiers)))
            .Where(t => ConferenceApp.Services.Payments.TicketPricing.IsPayable(t))
            .ToList();

        foreach (var tier in payable)
            Assert.Contains($"/Payment/{tier.TierKey}", html);

        var notPayable = (await _app.Db.ReadAsync(db =>
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(db.TicketTiers)))
            .Where(t => !ConferenceApp.Services.Payments.TicketPricing.IsPayable(t))
            .ToList();

        foreach (var tier in notPayable)
            Assert.DoesNotContain($"/Payment/{tier.TierKey}", html);
    }

    [Theory]
    [InlineData("2")]   // a student or doctoral candidate: subsidised
    [InlineData("4")]   // a journalist: pays nothing
    public async Task Форма_без_дължима_сума_отива_в_профила(string partForm)
    {
        // [T-03] The tier is decided by the form of participation rather than by
        // the slug in the address. For "2" and "4" TicketPricing.ForUser returns
        // null, so there is no tier to redirect to, and the place that says what
        // comes next instead of a payment is the profile.
        var user = await NewParticipantAsync(partForm);

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        using var client = session.NoRedirectClient();
        var response = await client.GetAsync("/Payment/earlybird");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Profile", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("viewer")]          // a free tier
    [InlineData("student")]         // a subsidised tier
    [InlineData("ne-sushtestvuva")] // a slug that does not exist
    public async Task Чуждо_ниво_пренасочва_към_дължимата_тарифа(string slug)
    {
        // A lecturer, form "1", owes the earlybird tier. Whichever slug they ask
        // for, the page sends them back to their own tier rather than refusing.
        var user = await NewParticipantAsync("1");

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        using var client = session.NoRedirectClient();
        var response = await client.GetAsync($"/Payment/{slug}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var target = response.Headers.Location!.ToString();
        Assert.Contains("earlybird", target);

        // And there is only one redirect: the target opens rather than forwarding
        // again.
        var landed = await client.GetAsync(target);
        Assert.Equal(HttpStatusCode.OK, landed.StatusCode);
        Assert.Contains("id=\"btn-pay-card\"", await landed.Content.ReadAsStringAsync());
    }

    // ════════════════════════════════════════════════════════════════════
    // The three tiers
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Всяка_тарифа_дава_своята_сума_или_отказва()
    {
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var tiers = await _app.Db.ReadAsync(db =>
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(db.TicketTiers));

        Assert.NotEmpty(tiers);

        foreach (var tier in tiers)
        {
            var expected = ConferenceApp.Services.Payments.TicketPricing.PriceEUR(tier);

            using var client = session.NoRedirectClient();
            var response = await client.GetAsync($"/Payment/{tier.TierKey}");

            var owed = ConferenceApp.Services.Payments.TicketPricing.ForUser(tiers, user);

            // A participant owes one tier; every other tier redirects to it ([T-03]).
            if (expected == null || owed == null || owed.Id != tier.Id)
            {
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                continue;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var html = await response.Content.ReadAsStringAsync();
            var amount = expected.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

            Assert.Contains(amount, html);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Changing the price from the admin panel
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Смяна_на_цената_от_панела_се_отразява_на_страницата()
    {
        var user = await NewParticipantAsync();
        var tier = await _app.Db.TierAsync("earlybird");
        Assert.NotNull(tier);

        using var admin = _app.NewSession();
        await admin.LoginAdminAsync(_app.Credentials.AdminEmail, _app.Credentials.AdminPassword);

        const decimal newPrice = 137m;

        try
        {
            var response = await admin.PostHandlerAsync("/Admin", "EditTicket", new Dictionary<string, string>
            {
                ["EditTicket.Id"]              = tier!.Id.ToString(),
                ["EditTicket.TierKey"]         = tier.TierKey,
                ["EditTicket.NameEn"]          = tier.NameEn,
                ["EditTicket.NameBg"]          = tier.NameBg,
                ["EditTicket.DescriptionEn"]   = tier.DescriptionEn,
                ["EditTicket.DescriptionBg"]   = tier.DescriptionBg,
                ["EditTicket.RegularPriceEn"]  = "€137",
                ["EditTicket.RegularPriceBg"]  = "€137",
                ["EditTicket.PromoPriceEn"]    = string.Empty,
                ["EditTicket.PromoPriceBg"]    = string.Empty,
                ["EditTicket.RegularPriceEUR"] = newPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["EditTicket.PromoPriceEUR"]   = string.Empty,
                ["EditTicket.PerksEn"]         = tier.PerksEn,
                ["EditTicket.PerksBg"]         = tier.PerksBg
            });

            Assert.True((int)response.StatusCode < 400, $"Панелът върна {(int)response.StatusCode}.");

            var saved = await _app.Db.TierAsync("earlybird");
            Assert.Equal(newPrice, saved!.RegularPriceEUR);
            Assert.Null(saved.PromoPriceEUR);

            using var session = _app.NewSession();
            await session.LoginParticipantAsync(user.Email!);

            var html = await session.Client.GetStringAsync("/Payment/earlybird");
            Assert.Contains("137.00", html);
            Assert.DoesNotContain(">60.00<", html);
        }
        finally
        {
            // The test puts the tier back the way it found it.
            await _app.Db.WriteAsync(async db =>
            {
                var row = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .FirstAsync(db.TicketTiers, t => t.Id == tier!.Id);

                row.RegularPriceEn  = tier!.RegularPriceEn;
                row.RegularPriceBg  = tier.RegularPriceBg;
                row.PromoPriceEn    = tier.PromoPriceEn;
                row.PromoPriceBg    = tier.PromoPriceBg;
                row.RegularPriceEUR = tier.RegularPriceEUR;
                row.PromoPriceEUR   = tier.PromoPriceEUR;
            });
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Access
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Страницата_за_плащане_без_влизане_праща_към_входа()
    {
        using var client = _app.NewClient(followRedirects: false);

        var response = await client.GetAsync("/Payment/earlybird");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location!.ToString());
    }
}
