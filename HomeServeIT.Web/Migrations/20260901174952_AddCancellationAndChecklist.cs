using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeServeIT.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCancellationAndChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "ServiceRequests",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CancellationRejectReason",
                table: "ServiceRequests",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CancellationStatus",
                table: "ServiceRequests",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "Check1_Diagnostic",
                table: "ServiceRequests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Check2_Hardware",
                table: "ServiceRequests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Check3_Firmware",
                table: "ServiceRequests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Check4_QA",
                table: "ServiceRequests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Check5_Handover",
                table: "ServiceRequests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancellationRequested",
                table: "ServiceRequests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "CancellationRejectReason",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "CancellationStatus",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "Check1_Diagnostic",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "Check2_Hardware",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "Check3_Firmware",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "Check4_QA",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "Check5_Handover",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "IsCancellationRequested",
                table: "ServiceRequests");
        }
    }
}
