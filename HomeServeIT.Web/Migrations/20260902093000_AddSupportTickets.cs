using HomeServeIT.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeServeIT.Web.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260902093000_AddSupportTickets")]
public partial class AddSupportTickets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SupportTickets",
            columns: table => new
            {
                SupportTicketID = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                CustomerID = table.Column<int>(type: "int", nullable: false),
                Subject = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Message = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                AdminResponse = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                RespondedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SupportTickets", x => x.SupportTicketID);
                table.ForeignKey(
                    name: "FK_SupportTickets_Customers_CustomerID",
                    column: x => x.CustomerID,
                    principalTable: "Customers",
                    principalColumn: "CustomerID",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_SupportTickets_CustomerID",
            table: "SupportTickets",
            column: "CustomerID");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SupportTickets");
    }
}
