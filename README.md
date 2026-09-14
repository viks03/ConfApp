# Conference Platform

Web application for an academic conference — attendee registration, identity
verification, three payment methods, automated email, and a full admin
panel. Fully bilingual (Bulgarian and English).

---

## Features

**Public site** — conference info, schedule, lecturers, travel, FAQ. Every
page in both languages.

**Registration** — sign-up with email verification, profile management,
paper submission, and an identity verification workflow for students and
journalists.

**Payments** — card via Stripe, cryptocurrency, and bank transfer. Amounts
are validated server-side on every confirmation path.

**Email** — one-time codes, payment status, verification results. Sent
through a background queue with retries.

**Admin panel** — attendees, lecturers, schedule, ticket tiers, downloadable
files, audit log, health checks, and automated backups.

**Theming** — nine built-in themes, light and dark, switchable from the
panel without a restart. Custom themes can be uploaded.

---

## Tech stack

| | |
| --- | --- |
| Framework | ASP.NET Core 9 — Razor Pages, MVC controllers, Areas |
| Authentication | ASP.NET Core Identity, email one-time codes |
| Data | Entity Framework Core, SQLite, code-first migrations |
| Payments | Stripe API, Go28 for crypto |
| Frontend | Vanilla JS, hand-written CSS, no framework |
| Localization | `.resx` resource files (bg/en) |
| Background work | Hosted queued service |
| Tests | xUnit and Playwright |

---

## Project structure

```
.
├── Areas/          Admin area
├── Controllers/    MVC controllers — payments, webhooks, bug reports
├── Data/           DbContext and database configuration
├── Helpers/        Utility classes
├── Migrations/     EF Core migrations
├── Models/         Domain entities
├── Pages/          Razor Pages
├── Resources/      Localization files (bg/en)
├── Services/       Email, payments, theming, health checks, backups
├── Tests/          Test suite
├── App_Data/       Private uploads — papers and verification documents.
│                   Outside wwwroot on purpose: never served statically,
│                   only through handlers that check who is asking.
├── wwwroot/        Static assets and public uploads
└── docs/           Audit, fixes, and test reports
```

---

## Running locally

**Requirements:** .NET 9 SDK.

```bash
git clone <repository-url>
cd <folder>
dotnet restore
```

### Secrets

Seven values are required. They are never stored in `appsettings.json` — the
application will not start without them.

```bash
dotnet user-secrets set "EmailSettings:Password" "..."
dotnet user-secrets set "Stripe:PublishableKey" "..."
dotnet user-secrets set "Stripe:SecretKey" "..."
dotnet user-secrets set "Stripe:WebhookSecret" "..."
dotnet user-secrets set "Go28:ApiToken" "..."
dotnet user-secrets set "AdminSettings:SystemAdminPassword" "..."
dotnet user-secrets set "RemoteControl:Key" "..."
```

| Secret | What it is |
| --- | --- |
| `EmailSettings:Password` | Mailbox password for outgoing email |
| `Stripe:PublishableKey` | Public Stripe key, used by the browser |
| `Stripe:SecretKey` | Server-side Stripe key |
| `Stripe:WebhookSecret` | Verifies the signature on Stripe callbacks |
| `Go28:ApiToken` | Access to the crypto payment provider |
| `AdminSettings:SystemAdminPassword` | Password for the seeded admin account |
| `RemoteControl:Key` | Shared key for the external configuration service |

Use Stripe **test** keys (`sk_test_`, `pk_test_`) for local work. With live
keys, real money moves.

### Database

```bash
dotnet ef database update
dotnet run
```

The app runs on `https://localhost:5253`.

---

## Tests

```bash
dotnet test
```

Around 900 tests across ten areas — payments, authentication, file access,
admin panel, email, localization, and background services. They cover the
paths where failure is expensive: a foreign `session_id` must not confirm a
payment, a forged webhook must not pass, one participant must not download
another's paper.

---

## Migrations

After changing the models:

```bash
dotnet ef migrations add <Name>
dotnet ef database update
```

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

---

## Deployment

For server setup, folder permissions, and environment variables, see
[DEPLOYMENT.md](DEPLOYMENT.md).

---

## License

This project is **proprietary**. All rights reserved.

No part of this repository may be used, copied, modified, or distributed
without explicit written permission from the author. Making the source
publicly visible does not grant any rights — see [LICENSE.md](LICENSE.md).

Third-party components bundled with the application, and the licences that
apply to them, are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## Author

Viktor Georgiev — viktor.georgiev@icbi.bg