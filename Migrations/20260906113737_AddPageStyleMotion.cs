using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConferenceApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPageStyleMotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Motion",
                table: "PageStyleSettings",
                type: "TEXT",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Motion",
                table: "PageStyleSettings");
        }
    }
}
