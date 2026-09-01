using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeServeIT.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRequestAnalyticsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedDate",
                table: "ServiceRequests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceCategory",
                table: "ServiceRequests",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedDate",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "ServiceCategory",
                table: "ServiceRequests");
        }
    }
}
