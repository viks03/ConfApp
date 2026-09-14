using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConferenceApp.Migrations
{
    /// <inheritdoc />
    public partial class DataIntegrityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PaymentGateSettings_GateKey",
                table: "PaymentGateSettings",
                column: "GateKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OtpCodes_Email_Purpose_CreatedAt",
                table: "OtpCodes",
                columns: new[] { "Email", "Purpose", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpCodes_ExpirationTime",
                table: "OtpCodes",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_EmailNotificationSettings_TemplateKey",
                table: "EmailNotificationSettings",
                column: "TemplateKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_IpAddress_Action_Timestamp",
                table: "AuditLogs",
                columns: new[] { "IpAddress", "Action", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserEmail_Action_Timestamp",
                table: "AuditLogs",
                columns: new[] { "UserEmail", "Action", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_ReferenceNumber",
                table: "AspNetUsers",
                column: "ReferenceNumber",
                unique: true,
                filter: "\"ReferenceNumber\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentGateSettings_GateKey",
                table: "PaymentGateSettings");

            migrationBuilder.DropIndex(
                name: "IX_OtpCodes_Email_Purpose_CreatedAt",
                table: "OtpCodes");

            migrationBuilder.DropIndex(
                name: "IX_OtpCodes_ExpirationTime",
                table: "OtpCodes");

            migrationBuilder.DropIndex(
                name: "IX_EmailNotificationSettings_TemplateKey",
                table: "EmailNotificationSettings");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_IpAddress_Action_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserEmail_Action_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_ReferenceNumber",
                table: "AspNetUsers");
        }
    }
}
