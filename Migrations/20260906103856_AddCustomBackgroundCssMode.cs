using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConferenceApp.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBackgroundCssMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "CustomBackgrounds",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawCss",
                table: "CustomBackgrounds",
                type: "TEXT",
                maxLength: 6000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "CustomBackgrounds");

            migrationBuilder.DropColumn(
                name: "RawCss",
                table: "CustomBackgrounds");
        }
    }
}
