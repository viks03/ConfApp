using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConferenceApp.Migrations
{
    /// <inheritdoc />
    public partial class MoneyAsNumbersAndTierKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PromoPriceEUR",
                table: "TicketTiers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RegularPriceEUR",
                table: "TicketTiers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TierKey",
                table: "TicketTiers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmountEUR",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TicketTiers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PromoPriceEUR", "RegularPriceEUR", "TierKey" },
                values: new object[] { null, null, "viewer" });

            migrationBuilder.UpdateData(
                table: "TicketTiers",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "PromoPriceEUR", "RegularPriceEUR", "TierKey" },
                values: new object[] { 60m, 100m, "earlybird" });

            migrationBuilder.UpdateData(
                table: "TicketTiers",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "PromoPriceEUR", "RegularPriceEUR", "TierKey" },
                values: new object[] { null, null, "student" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketTiers_TierKey",
                table: "TicketTiers",
                column: "TierKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TicketTiers_TierKey",
                table: "TicketTiers");

            migrationBuilder.DropColumn(
                name: "PromoPriceEUR",
                table: "TicketTiers");

            migrationBuilder.DropColumn(
                name: "RegularPriceEUR",
                table: "TicketTiers");

            migrationBuilder.DropColumn(
                name: "TierKey",
                table: "TicketTiers");

            migrationBuilder.DropColumn(
                name: "PaidAmountEUR",
                table: "AspNetUsers");
        }
    }
}
