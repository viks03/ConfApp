// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.RegularExpressions;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Emails;

/// <summary>
/// Part 8: "the links point at the right address".
/// <para>
/// A broken message shows up in no log at all: the recipient presses a button,
/// gets nowhere and gives up. So every link and every image of every message is
/// taken out of the message as it was sent, and opened.
/// </para>
/// <para>
/// The address comes from <c>AppSettings:BaseUrl</c> by way of
/// <c>MailContext.BaseUrl</c>. Under test it points at the application itself,
/// so "the right address" is something that can really be checked rather than a
/// string compared with itself.
/// </para>
/// </summary>
public class EmailLinkTests : EmailTestBase
{
    public EmailLinkTests(AppFixture app) : base(app, "mail-lnk") { }

    /// <summary>Every address in the message: the buttons and the logos in the frame alike.</summary>
    private static List<string> UrlsIn(CapturedMail mail) =>
        Regex.Matches(mail.Body, @"(?:href|src)\s*=\s*[""'](?<url>[^""']+)[""']",
                RegexOptions.IgnoreCase)
            .Select(m => m.Groups["url"].Value.Trim())
            .Where(u => !u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                     && !u.StartsWith("data:",   StringComparison.OrdinalIgnoreCase)
                     && !u.StartsWith("#",       StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static TheoryData<MailKind> Kinds
    {
        get
        {
            var data = new TheoryData<MailKind>();
            foreach (var kind in MailKinds.All) data.Add(kind);
            return data;
        }
    }

    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A relative address in a message leads nowhere: a mail client has nothing to
    /// resolve it against. Every address has to be absolute and point at our own
    /// site — not at localhost, not at Stripe, not at Go28.
    /// </summary>
    [Theory, MemberData(nameof(Kinds))]
    public async Task Всеки_адрес_в_писмото_сочи_към_приложението(MailKind kind)
    {
        var mail = await SampleAsync(kind);
        var urls = UrlsIn(mail);

        Assert.NotEmpty(urls);      // the frame always carries at least the logos

        foreach (var url in urls)
            Assert.True(url.StartsWith(App.BaseUrl, StringComparison.Ordinal),
                $"{kind}: адресът „{url}“ не тръгва от {App.BaseUrl}.");
    }

    /// <summary>
    /// Opens every address in the message. A 404 here means a button leading to an
    /// error page: exactly the kind of thing nobody notices until a participant
    /// reports it.
    /// </summary>
    [Theory, MemberData(nameof(Kinds))]
    public async Task Всеки_адрес_от_писмото_се_отваря(MailKind kind)
    {
        var mail = await SampleAsync(kind);

        using var client = App.NewClient();

        foreach (var url in UrlsIn(mail))
        {
            using var response = await client.GetAsync(url);

            Assert.True(response.StatusCode != HttpStatusCode.NotFound,
                $"{kind}: {url} връща 404.");

            Assert.True((int)response.StatusCode < 500,
                $"{kind}: {url} връща {(int)response.StatusCode}.");
        }
    }

    /// <summary>
    /// The logos in the frame are <c>&lt;img&gt;</c> elements, and for those "not a
    /// 404" is not enough: a broken image in a message looks exactly like empty
    /// space.
    /// </summary>
    [Fact]
    public async Task Изображенията_в_рамката_се_свалят()
    {
        var mail = await SampleAsync(MailKind.OtpLogin);

        var images = UrlsIn(mail)
            .Where(u => u.Contains("/images/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(images);

        using var client = App.NewClient();

        foreach (var url in images)
        {
            using var response = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Content.Headers.ContentLength > 0, $"{url} е празно.");
            Assert.StartsWith("image/", response.Content.Headers.ContentType?.MediaType ?? string.Empty);
        }
    }

    /// <summary>
    /// The button in each kind of message leads somewhere different: the payment
    /// one to the payment page, the document one to the submission page. The same
    /// address everywhere would mean someone had copied a template.
    /// </summary>
    [Theory, MemberData(nameof(Kinds))]
    public async Task Бутонът_води_на_мястото_за_този_вид_писмо(MailKind kind)
    {
        var expected = MailKinds.ButtonPath(kind);
        if (expected == null) return;      // the sign-in codes have no button

        var mail = await SampleAsync(kind);

        Assert.Contains(App.BaseUrl + expected, UrlsIn(mail));
    }

    /// <summary>
    /// An unreplaced placeholder means a literal <c>{Profile}</c> in the
    /// recipient's mailbox. The renderer has a check of its own and throws in
    /// Development; what is checked here is the result, not the intention.
    /// </summary>
    [Theory, MemberData(nameof(Kinds))]
    public async Task В_писмото_няма_незаместен_плейсхолдър(MailKind kind)
    {
        var mail = await SampleAsync(kind);

        var leftovers = Regex.Matches(mail.Body, @"\{[A-Za-z][A-Za-z0-9_]*\}")
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(leftovers.Count == 0,
            $"{kind}: в писмото останаха {string.Join(", ", leftovers)}.");

        Assert.DoesNotContain("{BaseUrl}", mail.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The message also goes out as plain text, for spam filters and for clients
    /// without HTML. If the address lives only in the <c>href</c>, that version
    /// carries nothing but the words of the button and the recipient has nowhere
    /// to go.
    /// </summary>
    [Fact]
    public async Task Текстовата_версия_носи_самия_адрес_а_не_само_надписа()
    {
        var mail = await SampleAsync(MailKind.PaymentConfirmed);

        var label = Subject("Email_Common_ButtonProfile");
        var link  = App.BaseUrl + "/Profile";

        // In the text part the address sits in brackets after the label; see
        // EmailSender.StripHTML. The arrow from the template stands between them,
        // which is why the two are checked separately.
        Assert.Contains(label, mail.Body, StringComparison.Ordinal);
        Assert.Contains($"({link})", mail.Body, StringComparison.Ordinal);

        // And the label comes BEFORE the address; otherwise the line does not
        // read.
        Assert.True(mail.Body.IndexOf(label, StringComparison.Ordinal)
                    < mail.Body.IndexOf($"({link})", StringComparison.Ordinal),
            "Адресът излиза преди надписа на бутона.");
    }

    /// <summary>
    /// A webhook arrives with a <c>Host</c> of its own, Stripe's rather than ours.
    /// The address in the message must not be built from the request; otherwise
    /// every link in the confirmation points at <c>stripe.com</c>.
    /// </summary>
    [Fact]
    public async Task Писмото_от_webhook_сочи_към_нашия_сайт_а_не_към_подателя()
    {
        var user = await NewParticipantAsync();

        var payload = StripeWebhook.CheckoutSessionCompleted(
            $"cs_test_{Guid.NewGuid():N}"[..30], user.Id, 6000, user.Email);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature",
            StripeWebhook.Sign(payload, AppFixture.StripeWebhookSecret));

        using var client = App.NewClient();
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mail = await ExpectAsync(user.Email!, "Email_PayConfirmed_Subject");

        foreach (var url in UrlsIn(mail))
            Assert.StartsWith(App.BaseUrl, url, StringComparison.Ordinal);

        Assert.DoesNotContain("stripe.com", mail.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// [T-27] The button in the bank-transfer message points at <c>/Payment</c>
    /// with no tier: the message does not know the recipient's tier. The check is
    /// made from their side — sign in with their own session, follow the link from
    /// the message, and land on the page of the tier they OWE rather than on a
    /// 404.
    /// </summary>
    [Fact]
    public async Task Получателят_стига_от_бутона_до_своята_тарифа()
    {
        var user = await NewParticipantAsync();

        using var session = await SignedInAsync(user);
        using (var submit = await session.PostHandlerAsync("/Payment/earlybird", "SubmitIban"))
            submit.EnsureSuccessStatusCode();

        var mail = await ExpectAsync(user.Email!, "Email_PayPending_Subject");
        var button = Assert.Single(UrlsIn(mail), u => u.EndsWith("/Payment", StringComparison.Ordinal));

        using var response = await session.Client.GetAsync(button);
        response.EnsureSuccessStatusCode();

        // Not merely "not a 404", but precisely the page of their own tier.
        Assert.EndsWith("/Payment/earlybird",
            response.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);

        var tier = await App.Db.TierAsync("earlybird");
        var due  = (tier!.PromoPriceEUR ?? tier.RegularPriceEUR)!.Value
            .ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains(due, await response.ReadPageAsync());
    }

    /// <summary>
    /// [T-28] The amount is written into the message in five places. The two
    /// Stripe paths used to format it under the culture of the request
    /// ("60,00 EUR") while the other three formatted it invariantly
    /// ("60.00 EUR"). Which path reaches the message first is a race ([T-01]), so
    /// one and the same payment produced now one form and now the other.
    /// </summary>
    [Fact]
    public async Task Сумата_изглежда_еднакво_от_всеки_път()
    {
        var tier = await App.Db.TierAsync("earlybird");
        var due  = (tier!.PromoPriceEUR ?? tier.RegularPriceEUR)!.Value;
        var expected = due.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " EUR";

        // 1. A bank transfer: the participant, in Bulgarian.
        var pending = await SampleAsync(MailKind.PaymentPending);
        Assert.Contains(expected, pending.Body, StringComparison.Ordinal);

        // 2. A manual confirmation from the admin panel.
        var manual = await SampleAsync(MailKind.PaymentConfirmed);
        Assert.Contains(expected, manual.Body, StringComparison.Ordinal);

        // 3. A webhook from Stripe: here there is no request culture at all.
        var user = await NewParticipantAsync();
        var payload = StripeWebhook.CheckoutSessionCompleted(
            $"cs_test_{Guid.NewGuid():N}"[..30], user.Id, (long)(due * 100), user.Email);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature",
            StripeWebhook.Sign(payload, AppFixture.StripeWebhookSecret));

        using var client = App.NewClient();
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await ExpectAsync(user.Email!, "Email_PayConfirmed_Subject");

        Assert.Contains(expected, card.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(due.ToString("F2", new System.Globalization.CultureInfo("bg-BG")) + " EUR",
            card.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sender is the address from the settings rather than something assembled
    /// on the spot. The subject must not be empty: a message without one goes
    /// straight to spam.
    /// </summary>
    [Theory, MemberData(nameof(Kinds))]
    public async Task Писмото_има_тема_и_получател(MailKind kind)
    {
        var mail = await SampleAsync(kind);

        Assert.False(string.IsNullOrWhiteSpace(mail.Subject), $"{kind}: празна тема.");
        Assert.Equal(Subject(MailKinds.SubjectKey(kind)).Trim(), mail.Subject.Trim());
        Assert.Contains("@", mail.To, StringComparison.Ordinal);
    }
}
