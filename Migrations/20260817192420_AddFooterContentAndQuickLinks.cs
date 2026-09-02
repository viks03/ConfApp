using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ConferenceApp.Migrations
{
    /// <inheritdoc />
    public partial class AddFooterContentAndQuickLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FooterContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BrandTaglineEn = table.Column<string>(type: "TEXT", nullable: false),
                    BrandTaglineBg = table.Column<string>(type: "TEXT", nullable: false),
                    OrgNoteEn = table.Column<string>(type: "TEXT", nullable: false),
                    OrgNoteBg = table.Column<string>(type: "TEXT", nullable: false),
                    ContactLocationEn = table.Column<string>(type: "TEXT", nullable: false),
                    ContactLocationBg = table.Column<string>(type: "TEXT", nullable: false),
                    ContactEmail = table.Column<string>(type: "TEXT", nullable: false),
                    ContactPhone = table.Column<string>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FooterContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FooterQuickLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LabelEn = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    LabelBg = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    IconSvg = table.Column<string>(type: "TEXT", nullable: false),
                    IsVisible = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FooterQuickLinks", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "FooterContents",
                columns: new[] { "Id", "BrandTaglineBg", "BrandTaglineEn", "ContactEmail", "ContactLocationBg", "ContactLocationEn", "ContactPhone", "LastUpdatedAt", "OrgNoteBg", "OrgNoteEn" },
                values: new object[] { 1, "Оформя бъдещето на финансовото образование", "Shapes the future of finance education", "conference.education@unwe.bg", "София, България", "Sofia, Bulgaria", "+359 98 871 1801", new DateTime(2026, 8, 17, 0, 0, 0, 0, DateTimeKind.Utc), "Организирано от Института по криптоикономика, блокчейн и иновации (ICBI) към Университета за национално и световно стопанство (УНСС).", "Organized by the Institute of Cryptoeconomics, Blockchain and Innovations (ICBI) within the University of National and World Economy (UNWE)." });

            migrationBuilder.InsertData(
                table: "FooterQuickLinks",
                columns: new[] { "Id", "IconSvg", "IsVisible", "LabelBg", "LabelEn", "Url" },
                values: new object[,]
                {
                    { 1, "<path d='M3 11l9-8 9 8'></path><path d='M5 10v10a1 1 0 0 0 1 1h4v-6h4v6h4a1 1 0 0 0 1-1V10'></path>", true, "Начало", "Home", "/Index" },
                    { 2, "<circle cx='9' cy='8' r='3'></circle><path d='M2 21v-1a6 6 0 0 1 6-6h2a6 6 0 0 1 6 6v1'></path><circle cx='17' cy='8' r='2.5'></circle><path d='M17 13.5c2.5.3 4.5 2.4 4.5 5V21'></path>", true, "Конференция", "Conference", "/Conference" },
                    { 3, "<line x1='3' y1='22' x2='21' y2='22'></line><line x1='6' y1='18' x2='6' y2='11'></line><line x1='10' y1='18' x2='10' y2='11'></line><line x1='14' y1='18' x2='14' y2='11'></line><line x1='18' y1='18' x2='18' y2='11'></line><polygon points='12 2 21 8 3 8'></polygon>", true, "За ICBI", "About ICBI", "/ICBI" },
                    { 4, "<circle cx='12' cy='8' r='4'></circle><path d='M4 21c0-4.4 3.6-8 8-8s8 3.6 8 8'></path>", true, "Лектори", "Lecturers", "/Lecturers" },
                    { 5, "<rect x='3' y='5' width='18' height='16' rx='2'></rect><line x1='3' y1='10' x2='21' y2='10'></line><line x1='8' y1='2' x2='8' y2='6'></line><line x1='16' y1='2' x2='16' y2='6'></line>", true, "Програма", "Schedule", "/Schedule" },
                    { 6, "<circle cx='9' cy='8' r='4'></circle><path d='M2 21c0-4.4 3.1-8 7-8s7 3.6 7 8'></path><line x1='18' y1='6' x2='18' y2='12'></line><line x1='15' y1='9' x2='21' y2='9'></line>", true, "Участие", "Attend", "/Attend" },
                    { 7, "<line x1='22' y1='2' x2='11' y2='13'></line><polygon points='22 2 15 22 11 13 2 9 22 2'></polygon>", true, "Пътуване", "Travel", "/Travel" },
                    { 8, "<circle cx='12' cy='12' r='9'></circle><path d='M9.5 9a2.5 2.5 0 1 1 3.4 2.3c-.9.4-1.4 1-1.4 2'></path><line x1='12' y1='17' x2='12.01' y2='17'></line>", true, "FAQ", "FAQ", "/FAQ" },
                    { 9, "<path d='M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z'></path><polyline points='14 3 14 9 20 9'></polyline><line x1='12' y1='12' x2='12' y2='18'></line><polyline points='9.5 14.5 12 12 14.5 14.5'></polyline>", true, "Покана за доклади", "Call for Papers", "/SubmitDocuments" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FooterContents");

            migrationBuilder.DropTable(
                name: "FooterQuickLinks");
        }
    }
}
