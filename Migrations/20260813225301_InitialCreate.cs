using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ConferenceApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", nullable: false),
                    LastName = table.Column<string>(type: "TEXT", nullable: false),
                    Age = table.Column<int>(type: "INTEGER", nullable: false),
                    AcademicTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Workplace = table.Column<string>(type: "TEXT", nullable: false),
                    PartForm = table.Column<string>(type: "TEXT", nullable: false),
                    IsForeigner = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasAcceptedGdpr = table.Column<bool>(type: "INTEGER", nullable: false),
                    GdprConsentDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WantsMarketing = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaperFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    MarketingConsentDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsentToPublishPaper = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishConsentDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "TEXT", nullable: false),
                    PaymentStatus = table.Column<string>(type: "TEXT", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IbanTransferSubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VerificationStatus = table.Column<string>(type: "TEXT", nullable: false),
                    VerificationDocumentPath = table.Column<string>(type: "TEXT", nullable: true),
                    VerificationInstitution = table.Column<string>(type: "TEXT", nullable: true),
                    VerificationSpecialty = table.Column<string>(type: "TEXT", nullable: true),
                    VerificationYear = table.Column<string>(type: "TEXT", nullable: true),
                    VerificationStudentId = table.Column<string>(type: "TEXT", nullable: true),
                    VerificationSubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VerificationRejectionReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    UserEmail = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BugReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    PageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    ReportedByEmail = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedByEmail = table.Column<string>(type: "TEXT", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BugReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommitteeMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FullNameEn = table.Column<string>(type: "TEXT", nullable: false),
                    FullNameBg = table.Column<string>(type: "TEXT", nullable: false),
                    RoleEn = table.Column<string>(type: "TEXT", nullable: true),
                    RoleBg = table.Column<string>(type: "TEXT", nullable: true),
                    OrganizationEn = table.Column<string>(type: "TEXT", nullable: true),
                    OrganizationBg = table.Column<string>(type: "TEXT", nullable: true),
                    CommitteeType = table.Column<string>(type: "TEXT", nullable: false),
                    AvatarImagePath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitteeMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConferenceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WatchOnlineLink = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConferenceSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CookieCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    NameBg = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionEn = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionBg = table.Column<string>(type: "TEXT", nullable: false),
                    IsVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsToggleable = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultOn = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookieCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CookieNoticeContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContentEn = table.Column<string>(type: "TEXT", nullable: false),
                    ContentBg = table.Column<string>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookieNoticeContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CookiePolicyContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContentEn = table.Column<string>(type: "TEXT", nullable: false),
                    ContentBg = table.Column<string>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookiePolicyContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TitleEn = table.Column<string>(type: "TEXT", nullable: false),
                    TitleBg = table.Column<string>(type: "TEXT", nullable: false),
                    LocationEn = table.Column<string>(type: "TEXT", nullable: false),
                    LocationBg = table.Column<string>(type: "TEXT", nullable: false),
                    EventUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Faqs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QuestionEn = table.Column<string>(type: "TEXT", nullable: false),
                    QuestionBg = table.Column<string>(type: "TEXT", nullable: false),
                    AnswerEn = table.Column<string>(type: "TEXT", nullable: false),
                    AnswerBg = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Faqs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HomePageLogos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    PartnerName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomePageLogos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Hotels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    NameBg = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionEn = table.Column<string>(type: "TEXT", nullable: true),
                    DescriptionBg = table.Column<string>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hotels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvitationSendLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientName = table.Column<string>(type: "TEXT", nullable: true),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorCategory = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    SentBody = table.Column<string>(type: "TEXT", nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentByEmail = table.Column<string>(type: "TEXT", nullable: true),
                    TrackingToken = table.Column<Guid>(type: "TEXT", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastOpenedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OpenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ClickedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClickCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenedUserAgent = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationSendLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lecturers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FullNameEn = table.Column<string>(type: "TEXT", nullable: false),
                    FullNameBg = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    RoleEn = table.Column<string>(type: "TEXT", nullable: true),
                    RoleBg = table.Column<string>(type: "TEXT", nullable: true),
                    OrganizationEn = table.Column<string>(type: "TEXT", nullable: true),
                    OrganizationBg = table.Column<string>(type: "TEXT", nullable: true),
                    BiographyEn = table.Column<string>(type: "TEXT", nullable: true),
                    BiographyBg = table.Column<string>(type: "TEXT", nullable: true),
                    ProfileUrl = table.Column<string>(type: "TEXT", nullable: true),
                    AvatarImagePath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lecturers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LinkWatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WatchOnlineLink = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkWatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OtpCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    NameBg = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    LogoImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    WebsiteUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrivacyPolicyContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContentEn = table.Column<string>(type: "TEXT", nullable: false),
                    ContentBg = table.Column<string>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivacyPolicyContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromoSlides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TitleEn = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TitleBg = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DescriptionEn = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DescriptionBg = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoSlides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Schedule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Day = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<string>(type: "TEXT", nullable: false),
                    EndTime = table.Column<string>(type: "TEXT", nullable: false),
                    TitleEn = table.Column<string>(type: "TEXT", nullable: false),
                    TitleBg = table.Column<string>(type: "TEXT", nullable: false),
                    SessionType = table.Column<string>(type: "TEXT", nullable: false),
                    SpeakerEn = table.Column<string>(type: "TEXT", nullable: true),
                    SpeakerBg = table.Column<string>(type: "TEXT", nullable: true),
                    LocationEn = table.Column<string>(type: "TEXT", nullable: true),
                    LocationBg = table.Column<string>(type: "TEXT", nullable: true),
                    DescriptionEn = table.Column<string>(type: "TEXT", nullable: true),
                    DescriptionBg = table.Column<string>(type: "TEXT", nullable: true),
                    LiveStreamUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialLinksSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LinkedInUrl = table.Column<string>(type: "TEXT", nullable: true),
                    XUrl = table.Column<string>(type: "TEXT", nullable: true),
                    InstagramUrl = table.Column<string>(type: "TEXT", nullable: true),
                    FacebookUrl = table.Column<string>(type: "TEXT", nullable: true),
                    TikTokUrl = table.Column<string>(type: "TEXT", nullable: true),
                    YouTubeUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialLinksSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    NameBg = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionEn = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionBg = table.Column<string>(type: "TEXT", nullable: false),
                    RegularPriceEn = table.Column<string>(type: "TEXT", nullable: false),
                    RegularPriceBg = table.Column<string>(type: "TEXT", nullable: false),
                    PromoPriceEn = table.Column<string>(type: "TEXT", nullable: true),
                    PromoPriceBg = table.Column<string>(type: "TEXT", nullable: true),
                    PerksEn = table.Column<string>(type: "TEXT", nullable: false),
                    PerksBg = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketTiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CryptoOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Go28OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    Network = table.Column<string>(type: "TEXT", nullable: false),
                    AmountEUR = table.Column<string>(type: "TEXT", nullable: false),
                    CryptoAmount = table.Column<string>(type: "TEXT", nullable: false),
                    NetAmount = table.Column<string>(type: "TEXT", nullable: false),
                    FeeAmount = table.Column<string>(type: "TEXT", nullable: false),
                    WalletAddress = table.Column<string>(type: "TEXT", nullable: false),
                    QrCode = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CryptoOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CryptoOrders_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "CookieCategories",
                columns: new[] { "Id", "DefaultOn", "DescriptionBg", "DescriptionEn", "DisplayOrder", "IsBuiltIn", "IsToggleable", "IsVisible", "Key", "NameBg", "NameEn" },
                values: new object[,]
                {
                    { 1, true, "Необходими за основната функционалност на сайта, включително сесии за вход, сигурност и балансиране на натоварването. Тези бисквитки не могат да бъдат изключени.", "Required for core website functionality, including login sessions, security features, and load balancing. These cookies cannot be disabled.", 1, true, false, true, "necessary", "Строго необходими", "Strictly Necessary" },
                    { 2, false, "Помагат ни да разберем как посетителите взаимодействат със сайта, като отчитат кои страници се разглеждат и колко време се прекарва в тях, за да можем да го подобряваме.", "Helps us understand how visitors interact with the site, such as pages viewed and time spent, so we can continuously improve the user experience.", 2, true, true, true, "analytics", "Анализи", "Analytics" },
                    { 3, false, "Използват се за измерване на ефективността на нашите маркетингови кампании, както и за показване на подходяща информация за конференцията в други платформи.", "Used to measure the effectiveness of our marketing campaigns and to display relevant information about the conference on other platforms.", 3, true, true, true, "marketing", "Маркетинг", "Marketing" },
                    { 4, false, "Запомнят направените от вас избори, като например предпочитан език, за да осигурят по-персонализирано и последователно изживяване.", "Remembers choices you have made, such as your selected language, to provide a more personalized and consistent experience.", 4, true, true, true, "preferences", "Предпочитания", "Preferences" }
                });

            migrationBuilder.InsertData(
                table: "CookieNoticeContents",
                columns: new[] { "Id", "ContentBg", "ContentEn", "LastUpdatedAt" },
                values: new object[] { 1, "<h2>Ценим вашата поверителност</h2>\n<p>Използваме бисквитки, за да осигурим правилното функциониране на сайта, да анализираме потреблението му и да подобрим вашето изживяване. Можете да изберете своите предпочитания и да ги промените по всяко време. За повече информация, моля, разгледайте нашата <a href=\"/Cookies\">Политика за бисквитки</a>.</p>", "<h2>We value your privacy</h2>\n<p>We use cookies to ensure the site functions properly, analyze usage, and enhance your overall experience. You can choose your preferences and update them at any time. For more details, please review our <a href=\"/Cookies\">Cookie Policy</a>.</p>", new DateTime(2026, 4, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "CookiePolicyContents",
                columns: new[] { "Id", "ContentBg", "ContentEn", "LastUpdatedAt" },
                values: new object[] { 1, "<p>В сила от: Април 2026</p>\n<p>Тази Политика за бисквитки обяснява какво представляват бисквитките, как уебсайтът на конференцията Blockchain Education 2026 ги използва и как можете да управлявате своите предпочитания по всяко време.</p>\n<h2>Какво са бисквитките?</h2>\n<p>Бисквитките са малки текстови файлове, които се запазват на вашето устройство при посещение на даден уебсайт. Те се използват широко, за да осигурят по-ефективната работа на сайтовете, да запомнят вашите предпочитания между отделните посещения и да предоставят информация на собствениците за начина на потребление.</p>\n<h2>Как използваме бисквитките</h2>\n<p>Използваме бисквитки, разпределени в няколко категории, вариращи от строго необходими за работата на сайта до опционални, които ни помагат да анализираме трафика и да показваме подходяща информация за конференцията. В секцията „Категории бисквитки, които използваме“ по-долу можете да видите точно кои категории са активни в момента и да управлявате своите предпочитания.</p>\n<h2>Бисквитки от трети страни</h2>\n<p>Определени бисквитки могат да бъдат поставени от доверени трети страни, с които си партнираме, като например платежни оператори, доставчици на анализи или платформи за вградено съдържание. Тези организации носят отговорност за собствените си практики за поверителност и защита на данните.</p>\n<h2>Управление на бисквитките в браузъра</h2>\n<p>Освен чрез центъра за предпочитания на този сайт, повечето браузъри ви позволяват да контролирате или изтривате бисквитките чрез собствените си настройки. Имайте предвид, че ограничаването на бисквитките може да повлияе на пълноценното функциониране на този и други уебсайтове.</p>\n<h2>Промени в тази политика</h2>\n<p>Възможно е периодично да актуализираме тази Политика за бисквитки, за да отразим промени в нашите практики или поради законови изисквания. Всички промени ще бъдат публикувани на тази страница.</p>\n<h2>Свържете се с нас</h2>\n<p>Ако имате въпроси относно използването на бисквитки, моля, свържете се с нас на conference.education@unwe.bg.</p>", "<p>Effective Date: April 2026</p>\n<p>This Cookie Policy explains what cookies are, how the Blockchain Education 2026 conference website uses them, and how you can control your preferences at any time.</p>\n<h2>What Are Cookies?</h2>\n<p>Cookies are small text files placed on your device when you visit a website. They are widely used to make websites work more efficiently, remember your preferences between visits, and provide information to site owners about how the site is used.</p>\n<h2>How We Use Cookies</h2>\n<p>We use cookies across several categories, ranging from those strictly necessary for the site to function, to optional ones that help us analyze traffic and display relevant information about the conference. You can see exactly which categories are currently in use and control your preferences in the \"Cookie categories we use\" section below.</p>\n<h2>Third-Party Cookies</h2>\n<p>Certain cookies may be set by trusted third parties we work with, such as payment processors, analytics providers, or platforms providing embedded content. These third parties are responsible for their own privacy and data protection practices.</p>\n<h2>Managing Cookies in Your Browser</h2>\n<p>In addition to the preference center on this site, most browsers allow you to control or delete cookies through their settings. Please note that disabling cookies may affect the functionality of this and other websites you visit.</p>\n<h2>Changes to This Policy</h2>\n<p>We may update this Cookie Policy periodically to reflect changes in our practices or for legal reasons. Any updates will be posted directly on this page.</p>\n<h2>Contact Us</h2>\n<p>If you have any questions about our use of cookies, please contact us at conference.education@unwe.bg.</p>", new DateTime(2026, 4, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "Faqs",
                columns: new[] { "Id", "AnswerBg", "AnswerEn", "DisplayOrder", "IsActive", "QuestionBg", "QuestionEn" },
                values: new object[,]
                {
                    { 1, "Абсолютно. Макар да е сакадемични корени, Blockchain Education 2026 е специално проектирана като мост между университетите, регулаторните и индустрията. Ден 2 е силно фокусиран върху практически Web3 технологии, DeFi, токенизация на реални активи (RWA) и навигиране в регулаторната рамка MiCA, което я прави жизненно стратегически център за професионалисти и есперти.", "Absolutely. While rooted in academia, Blockchain Education 2026 is specifically designed as a bridge between universities, regulators, and the industry. Day 2 is heavily focused on practical Web3 technologies, DeFi, RWA tokenization, and navigating the MiCA regulatory framework, making it a vital strategic hub for industry professionals and policymakers.", 1, true, "Аз съм от Web3 / FinTech индустрията. Подходяща ли е тази конференция за мен?", "I am from the Web3 / FinTech industry. Is this conference suitable for me?" },
                    { 2, "Blockchain Education 2026 е водеща академичне и институционален форум, посветен на бъдещето на криптоикономика и Web3. Той надхвърля традиционната конференция, преодолявайки пропаства между европейските университети, регулаторните органи и индустрията за дигитални финанси с цел установяване на единни образователни стандарти и изследователски рамки.", "Blockchain Education 2026 is a premier academic and institutional forum dedicated to the future of cryptoeconomics and Web3. It goes beyond a traditional conference by bridging the gap between European universities, regulatory bodies, and the digital finance industry to establish unified educational standards and research frameworks.", 2, true, "Какво представлява Blockchain Education 2026?", "What is Blockchain Education 2026?" },
                    { 3, "Основният език на конференцията е английски, което позволява на местни и международни участници да си сътрудничат лесно по време на всички сесии.", "The main conference language is English, allowing both local and international participants to collaborate easily across sessions.", 3, true, "Какъв е работният език на конференцията?", "What is the conference language?" },
                    { 4, "Можете да изпратите своето резюме и пълен доклад чрез официалния портал на конференцията на този уебсайт. Подробни указания за подаване, включително шаблони за форматиране и изисквания за рецензиране, са налични в секцията за конференция.", "You can submit your abstract and full paper through the official conference portal on this website. Detailed submission guidelines, including formatting templates and peer-review requirements, are available in the conference section.", 4, true, "Как да изпратя доклад?", "How do I submit a paper?" },
                    { 5, "За да насърчим младите академични таланти, предлагаме специално субсидирани такси за регистрация за студенти, магистри и докторанти, които желаят да представят доклад и да участват активно в конференцията. За да се възползвате от тези тарифи,ще рябва да прикачите валидна студентска книжка или уверение по време на процеса на регистрация. Моля, посетете страницата \"Билети\", за да видите точните студентски такси.", "To encourage young academic talent, we offer specially subsidized registration tiers for undergraduate, Master's, and PhD students who wish to present a paper and actively participate in the conference. To access these rates, you will need to upload a valid student ID or enrollment certificate during the registration and submission process. Please visit the Registration page to view the exact student rates.", 5, true, "Безплатно ли е участието за студенти?", "Is participation free for students?" },
                    { 6, "Софийската декларация е крайъгълният камък на Blockchain Education 2026. Тя представлява официален академичен ангажимент към споделени европейски стандарти в образованието по блокчейн и криптоикономика. Подписана в Ден 3 от участващите университети и институционални партньори, тя установява единна рамка за разработване на учебни програми, съвместни изследвания и регулаторно съгласуване в академичния сектор.", "The Sofia Declaration is the cornerstone outcome of Blockchain Education 2026. It represents a formal academic commitment to shared European standards in blockchain and cryptoeconomics education. Signed on Day 3 by participating universities and institutional partners, it establishes a unified framework for curriculum development, joint research, and regulatory alignment across the academic sector.", 6, true, "Какво представлява Софийската декларация?", "What is the Sofia Declaration?" },
                    { 7, "Конференцията се провежда официално в кампуса на УНиверситета за национално и световно стопанство (УНСС) в София, България. Подробни ръководства за навигация, включително маршрути на градския транспорт, информация за паркиране и опции за трансфер от летището, са налични в специалната <a href=\"/Travel\" style=\"text-decoration: underline;\">секция за пътуване</a> на нашия уебсайт.", "The conference is officially hosted at the University of National and World Economy (UNWE) campus in Sofia, Bulgaria. Detailed navigation guides, including public transport routes, parking information, and airport transfer options, are available in the dedicated <a href=\"/Travel\" style=\"text-decoration: underline;\">Travel section</a> of our website.", 7, true, "Как да стигна до мястото на събитието?", "How do I get to the venue?" },
                    { 8, "Да. Blockchain Education 2026 е организирана като хибридно събитие, което предоставя възможност на участниците, които не могат да пътуват, да се включат онлайн в избрани сесии. Авторите на научни доклади също могат да имат възможност да представят своите разработки дистанционно, в зависимост от окончателния формат на конференцията и техническите условия за провеждането ѝ.<br><br>Макар онлайн участието да предоставя достъп до ключови елементи от програмата на конференцията, присъстват на място предлага най-пълноценото преживяване, включително директен контакт с лекторите, възможности за професионално общуване, неформални дискусии, съпътстващи дейности в рамките на конференцията и участние в официалното подписване на Софийската декларация. Пради тази причина силно насърчаваме участниците да присъстват на място, когато това е възможно.", "Yes. Blockchain Education 2026 is designed as a hybrid event, allowing participants who are unable to travel to join selected sessions online. Presenting authors may also have the opportunity to deliver their presentations remotely, subject to the conference format and technical arrangements.<br><br>While online participation provides access to key conference activities, attending in person offers the most complete experience, including direct interaction with speakers, networking opportunities, informal discussions, exhibition activities, and the official signing of the Sofia Declaration. We therefore strongly encourage participants to attend on-site whenever possible.", 8, true, "Мога ли да присъствам онлайн?", "Can I attend online?" },
                    { 9, "Конференцията се организира официално от Института по криптоикономика, блокчейн и иновации (ICBI). ICBI е високо специализиран академичен и изследователски институт към Университета за национално и световно стопанство (УНСС), създаден с Постановление на Министерския съвет на Република България.", "The conference is officially organized by the Institute of Cryptoeconomics, Blockchain and Innovation (ICBI). ICBI is a highly specialized academic and research institute within the University of National and World Economy (UNWE), established by a Decree of the Bulgarian Council of Ministers.", 9, true, "Кой организира конференцията?", "Who organizes the conference?" }
                });

            migrationBuilder.InsertData(
                table: "HomePageLogos",
                columns: new[] { "Id", "ImagePath", "PartnerName" },
                values: new object[,]
                {
                    { 1, "/uploads/homepagelogos/GO28.png", "GO28" },
                    { 2, "/uploads/homepagelogos/UNWE.png", "NABI" },
                    { 3, "/uploads/homepagelogos/NABI.png", "UNWE" },
                    { 4, "/uploads/homepagelogos/BurgasUNI.png", "Bourgas University" }
                });

            migrationBuilder.InsertData(
                table: "PrivacyPolicyContents",
                columns: new[] { "Id", "ContentBg", "ContentEn", "LastUpdatedAt" },
                values: new object[] { 1, "<p>ПОЛИТИКА ЗА ПОВЕРИТЕЛНОСТ</p>\n<p>В сила от: Април 2026</p>\n\n<p>1. Въведение Добре дошли на официалния уебсайт на конференцията Blockchain Education 2026. Ние уважаваме вашата поверителност и се ангажираме да защитаваме вашите лични данни в съответствие с Общия регламента за защита на данните (GDPR). Тази Политика за поверителност обяснява как събираме, обработваме и съхраняваме вашата информация, когато посещавате нашия уебсайт, регистрирате за събитието, правите покупки или се абонирате за нашите съобщения.</p>\n<p>2. Данните, които събираме за вас Можем да събираме, използваме и съхраняваме различни видове лични данни за вас, включително:<br>Данни за идентичност и контакт: Име, фамилия, имейл адрес, телефонен номер и професионална/академична принадлежност, предоставени по време на регистрация.<br>Финансови и транзакционни данни: Ако направите покупка, събираме подробности за плащанията към и от вас и други подробности за продукти или услуги, които сте закупили от нас. Ние не съхраняваме пълни номера на кредитни карти или частни ключове на крипто портфейли; всички финансови транзакции се обработват сигурно от нашите оторизирани платежни портали на трети страни.<br>Технически данни: IP адрес, тип и версия на браузъра, настройка на часовата зона и операционна система, събирани автоматично, когато използвате нашия сайт. Маркетингови и комуникационни данни: Вашите предпочитания за получаване на маркетингови съобщения и бюлетини от нас.</p>\n<p>3. Как използваме вашите лични данни Ще използваме вашите лични данни само когато законът ни позволява. Най-често използваме вашите данни, за да: Ви регистрираме като присъстващ, обработваме вашите покупки и управляваме логистиката на събитието. Изпращаме ви административна информация, актуализации на графика и практическа информация. Изпращаме ви подходящи бюлетини и маркетингови съобщения относно бъдещи събития, при условие че изрично сте се съгласили (активно съгласие). Можете да се откажете по всяко време, като използвате връзката „отписване“ в нашите имейли.<br><br>Подобряваме оформлението на нашия уебсайт и потребителското изживяване чрез анализи.</p>\n<p>4. Споделяне на данни и трети страни Ние не продаваме вашите лични данни. Можем да споделяме вашите данни с доверени трети страни единствено за улесняване на операциите по събитието:<br>Платежни оператори: Споделяме необходимите данни за транзакциите с оторизирани платежни портали (напр. картови оператори, доставчици на крипто плащания) стриктно с цел завършване на вашите покупки и предотвратяване на измами.<br>Услуги за събития: Доставчици на услуги за имейл маркетинг и сигурни платформи за продажба на билети. Тези трети страни са обвързани от строги договори за обработка на данни и спазват стандартите на GDPR.</p>\n<p>5. Политика за бисквитки Нашият уебсайт използва бисквитки, за да ви отличи от другите потребители, осигурявайки по-добро изживяване при сърфиране и позволявайки ни да подобряваме нашия сайт. Основни бисквитки: Необходими за работата на нашия уебсайт (напр. изпращане на формуляри, обработка на плащания в количката).<br>Аналитични бисквитки: Позволяват ни да разпознаваме и преброяваме броя на посетителите и да виждаме как посетителите се движат из нашия уебсайт. Продължавайки да разглеждате сайта, вие се съгласявате с използването на основни бисквитки. Можете да деактивирате бисквитките в настройките на вашия браузър, но някои части от сайта могат да станат недостъпни.</p>\n<p>6. Вашите законни права (GDPR) При определени обстоятелства имате права съгласно законите за защита на данните във връзка с вашите лични данни, включително правото на: Изискване на достъп до вашите лични данни.<br>Изискване на коригиране на непълни или неточни данни.<br>Изискване на изтриване на вашите лични данни („Право да бъдеш забравен“). Възражение срещу обработването на вашите данни за целите на директния маркетинг. Оттегляне на вашето съгласие по всяко време. За да упражните някое от тези права, моля, свържете се с нас на conference.education@unwe.bg.</p>\n<p>7. Данни за контакт Ако имате въпроси относно тази Политика за поверителност или нашите практики за поверителност, моля, свържете се с нас на:<br>Имейл: conference.education@unwe.bg</p>", "<p>PRIVACY POLICY</p>\n<p>Effective Date: April 2026</p>\n\n<p>1. Introduction Welcome to the official website of the Blockchain Education 2026 conference. We respect your privacy and are committed to protecting your personal data in compliance with the General Data Protection Regulation (GDPR). This Privacy Policy explains how we collect, process, and safeguard your information when you visit our website, register for the event, make purchases, or subscribe to our communications.</p>\n<p>2. The Data We Collect About You We may collect, use, and store different kinds of personal data about you, including:<br>Identity & Contact Data: First name, last name, email address, phone number, and professional/academic affiliation provided during registration.<br>Financial & Transaction Data: If you make a purchase, we collect details about payments to and from you and other details of products or services you have purchased from us. We do not store full credit card numbers or crypto wallet private keys; all financial transactions are securely processed by our authorized third-party payment gateways.<br>Technical Data: Internet protocol (IP) address, browser type and version, time zone setting, and operating system collected automatically when you use our site. Marketing & Communications Data: Your preferences in receiving marketing and newsletters from us.</p>\n<p>3. How We Use Your Personal Data We will only use your personal data when the law allows us to. Most commonly, we use your data to: Register you as an attendee, process your purchases, and manage event logistics. Send you administrative information, schedule updates, and practical information. Send you relevant newsletters and marketing communications regarding future events, provided you have explicitly opted in (Active Consent). You may opt out at any time using the \"unsubscribe\" link in our emails.<br><br>Improve our website layout and user experience through analytics.</p>\n<p>4. Data Sharing and Third Parties We do not sell your personal data. We may share your data with trusted third parties strictly to facilitate event operations:<br>Payment Processors: We share necessary transaction data with authorized payment gateways (e.g., card processors, crypto payment providers) strictly for the purpose of completing your purchases and preventing fraud.<br>Event Services: Email marketing service providers and secure ticketing platforms. These third parties are bound by strict data processing agreements and comply with GDPR standards.</p>\n<p>5. Cookie Policy Our website uses cookies to distinguish you from other users, providing a better browsing experience and allowing us to improve our site. Essential Cookies: Required for the operation of our website (e.g., submitting forms, processing cart payments).<br>Analytical Cookies: Allow us to recognize and count the number of visitors and see how visitors move around our website. By continuing to browse the site, you consent to our use of essential cookies. You may disable cookies in your browser settings, but some parts of the site may become inaccessible.</p>\n<p>6. Your Legal Rights (GDPR) Under certain circumstances, you have rights under data protection laws in relation to your personal data, including the right to: Request access to your personal data.<br>Request correction of incomplete or inaccurate data.<br>Request erasure of your personal data (\"Right to be forgotten\"). Object to processing of your data for direct marketing purposes. Withdraw your consent at any time. To exercise any of these rights, please contact us at conference.education@unwe.bg.</p>\n<p>7. Contact Details If you have any questions about this Privacy Policy or our privacy practices, please contact us at:<br>Email: conference.education@unwe.bg</p>", new DateTime(2026, 4, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "SocialLinksSettings",
                columns: new[] { "Id", "FacebookUrl", "InstagramUrl", "LinkedInUrl", "TikTokUrl", "XUrl", "YouTubeUrl" },
                values: new object[] { 1, null, null, null, null, null, null });

            migrationBuilder.InsertData(
                table: "TicketTiers",
                columns: new[] { "Id", "DescriptionBg", "DescriptionEn", "NameBg", "NameEn", "PerksBg", "PerksEn", "PromoPriceBg", "PromoPriceEn", "RegularPriceBg", "RegularPriceEn" },
                values: new object[,]
                {
                    { 1, "Присъствен достъп до основната програма на конференцията в УНСС. Присъединете се към публиката, за да слушате, да научите нови неща и да проследите презентациите на място", "On-site access to the core conference program at UNWE. Join the audience to listen, learn, and experience the presentations in person.", "Пропуск за зрител", "Viewer Pass", "- Присъствен достъп до всички презентационни сесии\r\n- Достъп до модерираните дискусии с въпроси и отговори (Q&A)\r\n- Достъп до отворените нетуъркинг зони", "- On-site access to all presentation sessions\r\n- Access to the moderated Q&A discussions\r\n- Access to open networking areas", null, null, "Безплатно", "Free" },
                    { 2, "Пълен присъствен достъп за автори, академици и професионалисти от индустрията.", "Full on-site experience for presenting authors, academics, and industry professionals.", "Билет за ранно записване", "Early Bird Ticket", "- Слот за представяне на доклад (на място или онлайн)\r\n- Възможност за публикуване в официалния сборник с доклади от конференцията\r\n- Пълен достъп на място и възможности за нетуъркинг\r\n- Включени конференция материали, кетъринг и кафе-паузи", "- Paper presentation slot (On-site or Online)\r\n- Publication opportunity in the official conference proceedings\r\n- Full on-site access & networking opportunities\r\n- Conference materials, catering & coffee breaks included", "€60", "€60", "€100", "€100" },
                    { 3, "Преференциален достъп за млади изследователи, представящи доклад. Изисква се валидна студентска лична карта/книжка.", "Subsidized access for young researchers presenting a paper. Valid student ID required.", "Студенти и докторанти", "Students & PhD", "- Слот за представяне на доклад (на място или онлайн)\r\n- Възможност за публикуване в официалния сборник с доклади от конференцията\r\n- Пълен достъп на място и възможности за нетуъркинг\r\n- Включени конферентни материали, кетъринг и кафе-паузи", "- Paper presentation slot (On-site or Online)\r\n- Publication opportunity in the official conference proceedings\r\n- Full on-site access & networking opportunities\r\n- Conference materials, catering & coffee breaks included", null, null, "Напълно субсидиран", "Fully Subsidized" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BugReports_CreatedAt",
                table: "BugReports",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BugReports_Status",
                table: "BugReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CookieCategories_DisplayOrder",
                table: "CookieCategories",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_CookieCategories_Key",
                table: "CookieCategories",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CryptoOrders_Go28OrderId",
                table: "CryptoOrders",
                column: "Go28OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CryptoOrders_UserId",
                table: "CryptoOrders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvitationSendLogs_BatchId",
                table: "InvitationSendLogs",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InvitationSendLogs_SentAt",
                table: "InvitationSendLogs",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_InvitationSendLogs_TrackingToken",
                table: "InvitationSendLogs",
                column: "TrackingToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BugReports");

            migrationBuilder.DropTable(
                name: "CommitteeMembers");

            migrationBuilder.DropTable(
                name: "ConferenceSettings");

            migrationBuilder.DropTable(
                name: "CookieCategories");

            migrationBuilder.DropTable(
                name: "CookieNoticeContents");

            migrationBuilder.DropTable(
                name: "CookiePolicyContents");

            migrationBuilder.DropTable(
                name: "CryptoOrders");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Faqs");

            migrationBuilder.DropTable(
                name: "HomePageLogos");

            migrationBuilder.DropTable(
                name: "Hotels");

            migrationBuilder.DropTable(
                name: "InvitationSendLogs");

            migrationBuilder.DropTable(
                name: "Lecturers");

            migrationBuilder.DropTable(
                name: "LinkWatches");

            migrationBuilder.DropTable(
                name: "OtpCodes");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropTable(
                name: "PrivacyPolicyContents");

            migrationBuilder.DropTable(
                name: "PromoSlides");

            migrationBuilder.DropTable(
                name: "Schedule");

            migrationBuilder.DropTable(
                name: "SocialLinksSettings");

            migrationBuilder.DropTable(
                name: "TicketTiers");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
