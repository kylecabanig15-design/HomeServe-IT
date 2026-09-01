using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeServeIT.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdvancedWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EstimatedDeadline",
                table: "ServiceRequests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BreakdownDetails",
                table: "Invoices",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsQuotation",
                table: "Invoices",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "QuotationStatus",
                table: "Invoices",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "JobDeliverables",
                columns: table => new
                {
                    DeliverableID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RequestID = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImagePath = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobDeliverables", x => x.DeliverableID);
                    table.ForeignKey(
                        name: "FK_JobDeliverables_ServiceRequests_RequestID",
                        column: x => x.RequestID,
                        principalTable: "ServiceRequests",
                        principalColumn: "RequestID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ServiceMessages",
                columns: table => new
                {
                    MessageID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RequestID = table.Column<int>(type: "int", nullable: false),
                    SenderID = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceMessages", x => x.MessageID);
                    table.ForeignKey(
                        name: "FK_ServiceMessages_AspNetUsers_SenderID",
                        column: x => x.SenderID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceMessages_ServiceRequests_RequestID",
                        column: x => x.RequestID,
                        principalTable: "ServiceRequests",
                        principalColumn: "RequestID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_JobDeliverables_RequestID",
                table: "JobDeliverables",
                column: "RequestID");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceMessages_RequestID",
                table: "ServiceMessages",
                column: "RequestID");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceMessages_SenderID",
                table: "ServiceMessages",
                column: "SenderID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobDeliverables");

            migrationBuilder.DropTable(
                name: "ServiceMessages");

            migrationBuilder.DropColumn(
                name: "EstimatedDeadline",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "BreakdownDetails",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IsQuotation",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "QuotationStatus",
                table: "Invoices");
        }
    }
}
