using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConferenceApp.Migrations
{
    /// <inheritdoc />
    public partial class AddShowOnMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowOnMobile",
                table: "PageStyleSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowOnMobile",
                table: "PageStyleSettings");
        }
    }
}
