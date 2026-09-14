using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConferenceApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPageStyleSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomBackgrounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    LayersJson = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomBackgrounds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageStyleRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PageKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", maxLength: 6000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageStyleRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageStyleSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PageKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Background = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Intensity = table.Column<double>(type: "REAL", nullable: true),
                    Ink = table.Column<double>(type: "REAL", nullable: true),
                    Glow = table.Column<double>(type: "REAL", nullable: true),
                    CursorAlpha = table.Column<double>(type: "REAL", nullable: true),
                    GridStep = table.Column<int>(type: "INTEGER", nullable: true),
                    PaperStep = table.Column<int>(type: "INTEGER", nullable: true),
                    BarHeight = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomCss = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CustomCssEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageStyleSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomBackgrounds_Slug",
                table: "CustomBackgrounds",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageStyleRevisions_PageKey",
                table: "PageStyleRevisions",
                column: "PageKey");

            migrationBuilder.CreateIndex(
                name: "IX_PageStyleSettings_PageKey",
                table: "PageStyleSettings",
                column: "PageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomBackgrounds");

            migrationBuilder.DropTable(
                name: "PageStyleRevisions");

            migrationBuilder.DropTable(
                name: "PageStyleSettings");
        }
    }
}
