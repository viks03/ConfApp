# ConfApp

A full-featured web application for managing an academic conference end-to-end, built with **ASP.NET Core**. ConfApp handles everything from public-facing event information to attendee registration, payments, document/paper submission, identity verification, and admin management — with full **Bulgarian/English** localization.

## Features

**Public site**
- Conference info, schedule, lecturers, travel info, and FAQ pages
- Multi-language content (Bulgarian & English) across every page

**Registration & Attendance**
- Attendee registration and profile management
- Ticket tiers
- Document/paper submission (with support for authorship declarations, copyright agreements, and official paper templates)
- Identity verification workflow for students and journalists (document upload, approval/rejection, status notifications)

**Payments**
- Card payments via **Stripe**
- Cryptocurrency payments
- Payment status pages (confirmed/pending) with automated email notifications

**Communication**
- Automated email notifications (OTP codes, payment status, verification results) with HTML templates
- Bulk invitation sending with send-log tracking
- In-app bug report widget

**Admin**
- Dedicated Admin area for managing site content: footer, social links, cookie policy/notice, terms of use, privacy policy, home page logos, promo slides
- Audit logging of admin actions
- Health checks (including SMTP probe) and automated database backups
- Scheduled cleanup service (background hosted service)

**Compliance**
- Cookie consent banner and configurable cookie policy
- Terms of Use / Privacy Policy pages

## Tech Stack

- **Framework:** ASP.NET Core (Razor Pages + MVC Controllers, Areas)
- **Authentication:** ASP.NET Core Identity
- **ORM / Data access:** Entity Framework Core (Code-First Migrations)
- **Database:** SQLite
- **Payments:** Stripe API, custom crypto payment integration
- **Frontend:** Bootstrap 5, jQuery, jQuery Validation
- **Localization:** .resx resource files (bg/en)
- **Background processing:** Hosted queued background service
- **Data protection:** ASP.NET Core Data Protection (persisted keys)

## Project Structure

```
ConfApp/
├── Areas/          # Feature/admin areas (Razor Pages areas)
├── Controllers/    # MVC controllers
├── Data/           # DbContext and database configuration
├── Helpers/        # Utility/helper classes
├── Migrations/     # EF Core migrations
├── Models/         # Domain models / entities
├── Pages/          # Razor Pages
├── Resources/       # Localization resource files (bg/en)
├── Services/       # Business logic / application services (email, payments, health checks, audit, backups)
├── wwwroot/        # Static assets (CSS, JS, images) and static uploads
│   └── uploads/    # Note: submitted-documents/ and papers26/ are gitignored (user-submitted content)
├── Program.cs
└── appsettings.json
```

## Getting Started

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (version [X.X] or later)
- (Optional) [Visual Studio](https://visualstudio.microsoft.com/) or [Visual Studio Code](https://code.visualstudio.com/)
- EF Core CLI tools:
  ```bash
  dotnet tool install --global dotnet-ef
  ```

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/viks03/ConfApp.git
   cd ConfApp
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Configure the database connection**
   Check `appsettings.json` and adjust the connection string if needed (defaults to a local SQLite file).

4. **Apply database migrations**
   ```bash
   dotnet ef database update
   ```

5. **Run the application**
   ```bash
   dotnet run
   ```
   The app will be available at the URL shown in the console (typically `https://localhost:5001` or similar).

### Running from Visual Studio

1. Open `ConferenceApp.csproj` (or the solution file) in Visual Studio.
2. Set it as the startup project.
3. Press `F5` (or `Ctrl+F5` to run without debugging).

## Database Migrations

To create a new migration after changing the models:
```bash
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

## Versioning & Changelog

This project follows a simple versioning scheme. See [CHANGELOG.md](CHANGELOG.md) for a history of changes between releases. New releases are also tagged and published under [GitHub Releases](https://github.com/viks03/ConfApp/releases), each with English release notes describing what's new.

## Contributing

This is currently a personal/solo project. Issues and suggestions are welcome via the [Issues](https://github.com/viks03/ConfApp/issues) tab.

## License

This project is **proprietary**. All rights reserved — see [LICENSE](LICENSE). No part of this repository may be used, copied, modified, or distributed without explicit written permission from the author.
