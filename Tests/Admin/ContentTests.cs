// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Globalization;
using ConferenceApp.Models;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, the content the panel manages: lecturers, the programme and the tiers.
/// <para>
/// Each of the three is checked all the way to the public page. A record that
/// reaches the database but never appears on the site is just as much a failure as
/// one that never gets there.
/// </para>
/// </summary>
public class ContentTests : AdminTestBase
{
    public ContentTests(AppFixture app) : base(app, "ad-cnt") { }

    private async Task<string> PublicPageAsync(string path, string locale = "bg")
    {
        using var client = App.NewClient();
        client.DefaultRequestHeaders.Add("Cookie",
            $".AspNetCore.Culture=c%3D{locale}%7Cuic%3D{locale}");

        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.ReadPageAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // Lecturers
    // ════════════════════════════════════════════════════════════════════

    private Dictionary<string, string> LecturerForm(string marker, int id = 0) => new()
    {
        ["Id"]             = id.ToString(),
        ["FullNameEn"]     = $"Test Lecturer {marker}",
        ["FullNameBg"]     = $"Тестов лектор {marker}",
        ["Category"]       = "Academic",
        ["RoleEn"]         = "Professor",
        ["RoleBg"]         = "Професор",
        ["OrganizationEn"] = "Test University",
        ["OrganizationBg"] = "Тестов университет",
        ["BiographyEn"]    = "Short biography.",
        ["BiographyBg"]    = "Кратка биография.",
        ["ProfileUrl"]     = "https://example.test/lecturer"
    };

    private async Task<LecturerModel> AddLecturerAsync(HttpSession admin, string marker)
    {
        var reply = await PostMultipartAsync(admin, "SaveLecturer",
            LecturerForm(marker),
            new[] { UploadFile.Png("avatarFile", "avatar.png") });

        Assert.True(reply.Success, reply.Message);

        var saved = await App.Db.ReadAsync(db => db.Lecturers.AsNoTracking()
            .FirstOrDefaultAsync(l => l.FullNameEn == $"Test Lecturer {marker}"));

        Assert.NotNull(saved);
        TrackLecturer(saved!.Id);
        TrackPublicFile(saved.AvatarImagePath);
        return saved;
    }

    [Fact]
    public async Task Лектор_се_добавя_и_излиза_на_страницата()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var lecturer = await AddLecturerAsync(admin, marker);

        Assert.Equal("Academic", lecturer.Category);
        Assert.StartsWith("/uploads/people/lecturers/", lecturer.AvatarImagePath);
        Assert.True(File.Exists(PublicPhysicalPath(lecturer.AvatarImagePath!)),
            "Снимката на лектора не е на диска.");

        var bg = await PublicPageAsync("/Lecturers", "bg");
        Assert.Contains($"Тестов лектор {marker}", bg);

        var en = await PublicPageAsync("/Lecturers", "en");
        Assert.Contains($"Test Lecturer {marker}", en);
    }

    [Fact]
    public async Task Лектор_се_редактира_без_нова_снимка()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var lecturer = await AddLecturerAsync(admin, marker);

        var form = LecturerForm(marker, lecturer.Id);
        form["RoleBg"] = "Доцент";
        form["FullNameBg"] = $"Преименуван {marker}";

        var reply = await PostAsync(admin, "SaveLecturer", form);
        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.ReadAsync(db => db.Lecturers.AsNoTracking()
            .FirstAsync(l => l.Id == lecturer.Id));

        Assert.Equal("Доцент", after.RoleBg);
        Assert.Equal($"Преименуван {marker}", after.FullNameBg);
        // The photograph stays: an edit without a file must not delete it.
        Assert.Equal(lecturer.AvatarImagePath, after.AvatarImagePath);
    }

    [Fact]
    public async Task Нов_лектор_без_снимка_се_отказва()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "SaveLecturer", LecturerForm(marker));

        Assert.False(reply.Success);
        Assert.Contains("Avatar", reply.Message, StringComparison.OrdinalIgnoreCase);

        var saved = await App.Db.ReadAsync(db => db.Lecturers.AsNoTracking()
            .AnyAsync(l => l.FullNameEn == $"Test Lecturer {marker}"));
        Assert.False(saved, "Лектор без снимка все пак влезе в базата.");
    }

    [Theory]
    [InlineData("FullNameEn")]
    [InlineData("FullNameBg")]
    [InlineData("RoleEn")]
    [InlineData("RoleBg")]
    [InlineData("OrganizationEn")]
    [InlineData("OrganizationBg")]
    public async Task Лектор_без_задължително_поле_се_отказва(string field)
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        var form = LecturerForm(marker);
        form[field] = "";

        using var admin = await SignedInAdminAsync();
        var reply = await PostMultipartAsync(admin, "SaveLecturer", form,
            new[] { UploadFile.Png("avatarFile", "avatar.png") });

        Assert.False(reply.Success);
        Assert.Contains("required", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("avatar.exe")]
    [InlineData("avatar.pdf")]
    [InlineData("avatar.svg")]
    public async Task Непозволен_формат_на_снимката_се_отказва(string name)
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var reply = await PostMultipartAsync(admin, "SaveLecturer",
            LecturerForm(marker),
            new[] { UploadFile.Png("avatarFile", name) });

        Assert.False(reply.Success);
        Assert.Contains("Invalid file format", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Твърде_голяма_снимка_се_отказва()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var reply = await PostMultipartAsync(admin, "SaveLecturer",
            LecturerForm(marker),
            new[] { UploadFile.Png("avatarFile", "avatar.png").OfSize(6 * 1024 * 1024) });

        Assert.False(reply.Success);
        Assert.Contains("too large", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Изтриването_маха_лектора_снимката_и_реда_на_страницата()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var lecturer = await AddLecturerAsync(admin, marker);
        var physical = PublicPhysicalPath(lecturer.AvatarImagePath!);

        var reply = await PostAsync(admin, "DeleteLecturer", new Dictionary<string, string>
        {
            ["id"] = lecturer.Id.ToString()
        });
        Assert.True(reply.Success, reply.Message);

        Assert.False(await App.Db.ReadAsync(db => db.Lecturers.AnyAsync(l => l.Id == lecturer.Id)));
        Assert.False(File.Exists(physical), "Снимката на изтрит лектор остана на диска.");

        var page = await PublicPageAsync("/Lecturers", "bg");
        Assert.DoesNotContain($"Тестов лектор {marker}", page);
    }

    [Fact]
    public async Task Изтриване_на_несъществуващ_лектор_казва_какво_има()
    {
        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "DeleteLecturer", new Dictionary<string, string>
        {
            ["id"] = "999999"
        });

        Assert.False(reply.Success);
        Assert.Contains("not found", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The display order. The model has no field for ordering, and both the panel
    /// and the page read without an <c>OrderBy</c>, so the order is the order of
    /// insertion (by Id). The test guards exactly that: if an ordering is ever
    /// added, it shows up here.
    /// </summary>
    [Fact]
    public async Task Редът_на_лекторите_е_по_вмъкване()
    {
        var first  = Guid.NewGuid().ToString("N")[..8];
        var second = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var a = await AddLecturerAsync(admin, first);
        var b = await AddLecturerAsync(admin, second);

        Assert.True(b.Id > a.Id);

        var page = await PublicPageAsync("/Lecturers", "en");
        var indexA = page.IndexOf($"Test Lecturer {first}", StringComparison.Ordinal);
        var indexB = page.IndexOf($"Test Lecturer {second}", StringComparison.Ordinal);

        Assert.True(indexA >= 0 && indexB >= 0, "Единият лектор липсва на страницата.");
        Assert.True(indexA < indexB,
            "Лекторите излизат в друг ред, а страницата не подрежда изрично.");
    }

    // ════════════════════════════════════════════════════════════════════
    // The programme
    // ════════════════════════════════════════════════════════════════════

    private static Dictionary<string, string> SessionForm(string marker, int id = 0) => new()
    {
        ["Id"]            = id.ToString(),
        ["Day"]           = "Day 1 (Oct 29, 2026)",
        ["StartTime"]     = "09:00",
        ["EndTime"]       = "10:30",
        ["TitleEn"]       = $"Test Session {marker}",
        ["TitleBg"]       = $"Тестова сесия {marker}",
        ["SessionType"]   = "Panel",
        ["SpeakerEn"]     = "Test Speaker",
        ["SpeakerBg"]     = "Тестов говорител",
        ["LocationEn"]    = $"Hall {marker}",
        ["LocationBg"]    = $"Зала {marker}",
        ["DescriptionEn"] = "Description.",
        ["DescriptionBg"] = "Описание.",
        ["LiveStreamUrl"] = ""
    };

    private async Task<ScheduleModel> AddSessionAsync(HttpSession admin, string marker)
    {
        var reply = await PostAsync(admin, "SaveSession", SessionForm(marker));
        Assert.True(reply.Success, reply.Message);

        var saved = await App.Db.ReadAsync(db => db.Schedule.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TitleEn == $"Test Session {marker}"));

        Assert.NotNull(saved);
        TrackSession(saved!.Id);
        return saved;
    }

    [Fact]
    public async Task Сесия_се_добавя_с_часове_и_зала_и_излиза_в_програмата()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var session = await AddSessionAsync(admin, marker);

        Assert.Equal("09:00", session.StartTime);
        Assert.Equal("10:30", session.EndTime);
        Assert.Equal($"Зала {marker}", session.LocationBg);

        var page = await PublicPageAsync("/Schedule", "bg");
        Assert.Contains($"Тестова сесия {marker}", page);
        Assert.Contains($"Зала {marker}", page);
        Assert.Contains("09:00", page);
    }

    [Theory]
    [InlineData("", "10:00", "Start time")]
    [InlineData("09:00", "", "End time")]
    [InlineData("11:00", "10:00", "after start time")]
    [InlineData("10:00", "10:00", "after start time")]
    public async Task Невалидни_часове_се_отказват(string start, string end, string expected)
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        var form = SessionForm(marker);
        form["StartTime"] = start;
        form["EndTime"]   = end;

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "SaveSession", form);

        Assert.False(reply.Success);
        Assert.Contains(expected, reply.Message, StringComparison.OrdinalIgnoreCase);

        Assert.False(await App.Db.ReadAsync(db =>
            db.Schedule.AnyAsync(s => s.TitleEn == $"Test Session {marker}")));
    }

    [Theory]
    [InlineData("TitleEn")]
    [InlineData("TitleBg")]
    public async Task Сесия_без_заглавие_се_отказва(string field)
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        var form = SessionForm(marker);
        form[field] = "";

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "SaveSession", form);

        Assert.False(reply.Success);
        Assert.Contains("required", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Сесия_се_редактира()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var session = await AddSessionAsync(admin, marker);

        var form = SessionForm(marker, session.Id);
        form["StartTime"]  = "14:00";
        form["EndTime"]    = "15:00";
        form["LocationBg"] = $"Друга зала {marker}";

        var reply = await PostAsync(admin, "SaveSession", form);
        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.ReadAsync(db => db.Schedule.AsNoTracking()
            .FirstAsync(s => s.Id == session.Id));

        Assert.Equal("14:00", after.StartTime);
        Assert.Equal($"Друга зала {marker}", after.LocationBg);

        var page = await PublicPageAsync("/Schedule", "bg");
        Assert.Contains($"Друга зала {marker}", page);
    }

    [Fact]
    public async Task Връзката_за_живо_излъчване_се_записва_и_изчиства()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var session = await AddSessionAsync(admin, marker);

        var set = await PostAsync(admin, "SaveSessionLiveLink", new Dictionary<string, string>
        {
            ["liveLinkSessionId"] = session.Id.ToString(),
            ["liveLinkUrl"]       = "https://example.test/live"
        });
        Assert.True(set.Success, set.Message);

        var after = await App.Db.ReadAsync(db => db.Schedule.AsNoTracking()
            .FirstAsync(s => s.Id == session.Id));
        Assert.Equal("https://example.test/live", after.LiveStreamUrl);

        // "#" and an empty value both mean "no link" rather than the literal text
        // "#".
        var cleared = await PostAsync(admin, "SaveSessionLiveLink", new Dictionary<string, string>
        {
            ["liveLinkSessionId"] = session.Id.ToString(),
            ["liveLinkUrl"]       = "#"
        });
        Assert.True(cleared.Success, cleared.Message);

        var cleanedUp = await App.Db.ReadAsync(db => db.Schedule.AsNoTracking()
            .FirstAsync(s => s.Id == session.Id));
        Assert.Null(cleanedUp.LiveStreamUrl);
    }

    [Fact]
    public async Task Сесия_се_изтрива_и_изчезва_от_програмата()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();
        var session = await AddSessionAsync(admin, marker);

        var reply = await PostAsync(admin, "DeleteSession", new Dictionary<string, string>
        {
            ["id"] = session.Id.ToString()
        });
        Assert.True(reply.Success, reply.Message);

        Assert.False(await App.Db.ReadAsync(db => db.Schedule.AnyAsync(s => s.Id == session.Id)));

        var page = await PublicPageAsync("/Schedule", "bg");
        Assert.DoesNotContain($"Тестова сесия {marker}", page);
    }

    /// <summary>The programme is ordered by day, then by start time.</summary>
    [Fact]
    public async Task Сесиите_излизат_подредени_по_час()
    {
        var late  = Guid.NewGuid().ToString("N")[..8];
        var early = Guid.NewGuid().ToString("N")[..8];

        using var admin = await SignedInAdminAsync();

        var lateForm = SessionForm(late);
        lateForm["StartTime"] = "16:00";
        lateForm["EndTime"]   = "17:00";
        Assert.True((await PostAsync(admin, "SaveSession", lateForm)).Success);
        TrackSession((await App.Db.ReadAsync(db => db.Schedule.AsNoTracking()
            .FirstAsync(s => s.TitleEn == $"Test Session {late}"))).Id);

        var earlyForm = SessionForm(early);
        earlyForm["StartTime"] = "08:00";
        earlyForm["EndTime"]   = "08:45";
        Assert.True((await PostAsync(admin, "SaveSession", earlyForm)).Success);
        TrackSession((await App.Db.ReadAsync(db => db.Schedule.AsNoTracking()
            .FirstAsync(s => s.TitleEn == $"Test Session {early}"))).Id);

        var page = await PublicPageAsync("/Schedule", "bg");
        var indexEarly = page.IndexOf($"Тестова сесия {early}", StringComparison.Ordinal);
        var indexLate  = page.IndexOf($"Тестова сесия {late}", StringComparison.Ordinal);

        Assert.True(indexEarly >= 0 && indexLate >= 0, "Едната сесия липсва в програмата.");
        Assert.True(indexEarly < indexLate, "Сесиите не са подредени по начален час.");
    }

    // ════════════════════════════════════════════════════════════════════
    // The tiers
    // ════════════════════════════════════════════════════════════════════

    private Dictionary<string, string> TicketForm(TicketTierModel tier) => new()
    {
        ["EditTicket.Id"]              = tier.Id.ToString(),
        ["EditTicket.TierKey"]         = tier.TierKey,
        ["EditTicket.NameEn"]          = tier.NameEn,
        ["EditTicket.NameBg"]          = tier.NameBg,
        ["EditTicket.DescriptionEn"]   = tier.DescriptionEn,
        ["EditTicket.DescriptionBg"]   = tier.DescriptionBg,
        ["EditTicket.RegularPriceEn"]  = tier.RegularPriceEn,
        ["EditTicket.RegularPriceBg"]  = tier.RegularPriceBg,
        ["EditTicket.PromoPriceEn"]    = tier.PromoPriceEn ?? "",
        ["EditTicket.PromoPriceBg"]    = tier.PromoPriceBg ?? "",
        ["EditTicket.RegularPriceEUR"] = tier.RegularPriceEUR?.ToString(CultureInfo.InvariantCulture) ?? "",
        ["EditTicket.PromoPriceEUR"]   = tier.PromoPriceEUR?.ToString(CultureInfo.InvariantCulture) ?? "",
        ["EditTicket.PerksEn"]         = tier.PerksEn,
        ["EditTicket.PerksBg"]         = tier.PerksBg
    };

    private async Task<TicketTierModel> TierAsync(string key) =>
        (await App.Db.TierAsync(key))
        ?? throw new InvalidOperationException($"Няма тарифа „{key}“ в базата.");

    private async Task RestoreTierAsync(HttpSession admin, TicketTierModel original)
    {
        var reply = await PostAsync(admin, "EditTicket", TicketForm(original));
        Assert.True(reply.Success || reply.Status == System.Net.HttpStatusCode.OK,
            "Не можах да върна тарифата такава, каквато беше.");
    }

    [Fact]
    public async Task Смяната_на_цена_се_вижда_на_страницата_за_участие()
    {
        var original = await TierAsync("earlybird");
        using var admin = await SignedInAdminAsync();

        try
        {
            var form = TicketForm(original);
            form["EditTicket.RegularPriceEUR"] = "137";
            form["EditTicket.RegularPriceBg"]  = "137 €";
            form["EditTicket.RegularPriceEn"]  = "€137";

            var reply = await PostAsync(admin, "EditTicket", form);
            Assert.True(reply.Success || reply.Status == System.Net.HttpStatusCode.OK, reply.Message);

            var saved = await TierAsync("earlybird");
            Assert.Equal(137m, saved.RegularPriceEUR);

            var page = await PublicPageAsync("/Attend", "bg");
            Assert.Contains("137", page);
        }
        finally
        {
            await RestoreTierAsync(admin, original);
        }
    }

    [Fact]
    public async Task Промо_цената_се_записва_и_взима_превес()
    {
        var original = await TierAsync("earlybird");
        using var admin = await SignedInAdminAsync();

        try
        {
            var form = TicketForm(original);
            form["EditTicket.RegularPriceEUR"] = "150";
            form["EditTicket.PromoPriceEUR"]   = "99";
            form["EditTicket.PromoPriceBg"]    = "99 €";
            form["EditTicket.PromoPriceEn"]    = "€99";

            Assert.True((await PostAsync(admin, "EditTicket", form)).Status
                        == System.Net.HttpStatusCode.OK);

            var saved = await TierAsync("earlybird");
            Assert.Equal(150m, saved.RegularPriceEUR);
            Assert.Equal(99m, saved.PromoPriceEUR);

            // The promotional price is the one charged, through the same helper the
            // payment page uses.
            Assert.Equal(99m, ConferenceApp.Services.Payments.TicketPricing.PriceEUR(saved));

            var page = await PublicPageAsync("/Attend", "bg");
            Assert.Contains("99", page);
        }
        finally
        {
            await RestoreTierAsync(admin, original);
        }
    }

    [Fact]
    public async Task Махането_на_промо_цената_връща_редовната()
    {
        var original = await TierAsync("earlybird");
        using var admin = await SignedInAdminAsync();

        try
        {
            var withPromo = TicketForm(original);
            withPromo["EditTicket.RegularPriceEUR"] = "150";
            withPromo["EditTicket.PromoPriceEUR"]   = "99";
            await PostAsync(admin, "EditTicket", withPromo);

            var without = TicketForm(original);
            without["EditTicket.RegularPriceEUR"] = "150";
            without["EditTicket.PromoPriceEUR"]   = "";
            without["EditTicket.PromoPriceBg"]    = "";
            without["EditTicket.PromoPriceEn"]    = "";
            await PostAsync(admin, "EditTicket", without);

            var saved = await TierAsync("earlybird");
            Assert.Null(saved.PromoPriceEUR);
            Assert.Equal(150m, ConferenceApp.Services.Payments.TicketPricing.PriceEUR(saved));
        }
        finally
        {
            await RestoreTierAsync(admin, original);
        }
    }

    /// <summary>
    /// The field in the panel is an &lt;input type="number" step="0.01"&gt;, so the
    /// browser submits "99.50" with a full stop. If that is not parsed, the price
    /// actually charged disappears while the display string stays — and the page
    /// lies.
    /// </summary>
    [Fact]
    public async Task Цена_с_десетична_част_се_записва()
    {
        var original = await TierAsync("earlybird");
        using var admin = await SignedInAdminAsync();

        try
        {
            var form = TicketForm(original);
            form["EditTicket.RegularPriceEUR"] = "99.50";
            form["EditTicket.PromoPriceEUR"]   = "";
            form["EditTicket.PromoPriceEn"]    = "";
            form["EditTicket.PromoPriceBg"]    = "";

            Assert.True((await PostAsync(admin, "EditTicket", form)).Status
                        == System.Net.HttpStatusCode.OK);

            var saved = await TierAsync("earlybird");
            Assert.Equal(99.50m, saved.RegularPriceEUR);
            Assert.Equal(99.50m, ConferenceApp.Services.Payments.TicketPricing.PriceEUR(saved));
        }
        finally
        {
            await RestoreTierAsync(admin, original);
        }
    }

    /// <summary>
    /// Text where a number belongs must not pass for an empty field: that would
    /// make the tier free without anyone asking for it.
    /// </summary>
    [Fact]
    public async Task Нечислова_цена_не_изтрива_старата()
    {
        var original = await TierAsync("earlybird");
        using var admin = await SignedInAdminAsync();

        try
        {
            var form = TicketForm(original);
            form["EditTicket.RegularPriceEUR"] = "сто";
            form["EditTicket.NameEn"] = "Променено име";

            await PostAsync(admin, "EditTicket", form);

            var saved = await TierAsync("earlybird");
            Assert.Equal(original.RegularPriceEUR, saved.RegularPriceEUR);

            // "Nothing was saved" means nothing at all, not just the price: a
            // refused request must not smuggle the other fields in through the back
            // door.
            Assert.Equal(original.NameEn, saved.NameEn);
        }
        finally
        {
            await RestoreTierAsync(admin, original);
        }
    }

    /// <summary>
    /// The tiers are shared across the whole suite: this test changes them and the
    /// payment tests read them. The restore is therefore asserted on explicitly —
    /// if RestoreTierAsync does not put the numeric price back, part 2 fails for no
    /// visible reason.
    /// </summary>
    [Fact]
    public async Task Възстановяването_на_тарифата_връща_и_числената_цена()
    {
        var original = await TierAsync("earlybird");
        Assert.NotNull(original.RegularPriceEUR);

        using var admin = await SignedInAdminAsync();

        var form = TicketForm(original);
        form["EditTicket.RegularPriceEUR"] = "1";
        await PostAsync(admin, "EditTicket", form);
        Assert.Equal(1m, (await TierAsync("earlybird")).RegularPriceEUR);

        await RestoreTierAsync(admin, original);

        var back = await TierAsync("earlybird");
        Assert.Equal(original.RegularPriceEUR, back.RegularPriceEUR);
        Assert.Equal(original.PromoPriceEUR, back.PromoPriceEUR);
    }

    [Fact]
    public async Task Редакция_на_несъществуваща_тарифа_не_създава_нова()
    {
        var before = await App.Db.ReadAsync(db => db.TicketTiers.CountAsync());

        using var admin = await SignedInAdminAsync();
        var original = await TierAsync("earlybird");

        var form = TicketForm(original);
        form["EditTicket.Id"] = "999999";

        await PostAsync(admin, "EditTicket", form);

        Assert.Equal(before, await App.Db.ReadAsync(db => db.TicketTiers.CountAsync()));
    }
}
