using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeServeIT.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddJobInventoryUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobInventoryUsages",
                columns: table => new
                {
                    UsageID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RequestID = table.Column<int>(type: "int", nullable: false),
                    ItemID = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IsDeducted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobInventoryUsages", x => x.UsageID);
                    table.ForeignKey(
                        name: "FK_JobInventoryUsages_InventoryItems_ItemID",
                        column: x => x.ItemID,
                        principalTable: "InventoryItems",
                        principalColumn: "ItemID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobInventoryUsages_ServiceRequests_RequestID",
                        column: x => x.RequestID,
                        principalTable: "ServiceRequests",
                        principalColumn: "RequestID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_JobInventoryUsages_ItemID",
                table: "JobInventoryUsages",
                column: "ItemID");

            migrationBuilder.CreateIndex(
                name: "IX_JobInventoryUsages_RequestID_ItemID",
                table: "JobInventoryUsages",
                columns: new[] { "RequestID", "ItemID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobInventoryUsages");
        }
    }
}
