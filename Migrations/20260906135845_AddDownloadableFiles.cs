using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConferenceApp.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadableFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadableFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileKey = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    DownloadName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadableFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadableFiles_FileKey",
                table: "DownloadableFiles",
                column: "FileKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadableFiles");
        }
    }
}
