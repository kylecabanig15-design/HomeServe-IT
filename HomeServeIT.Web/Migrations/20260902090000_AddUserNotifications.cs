using HomeServeIT.Web.Data;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeServeIT.Web.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260902090000_AddUserNotifications")]
public partial class AddUserNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserNotifications",
            columns: table => new
            {
                NotificationID = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                RecipientUserID = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                AudienceRole = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Category = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Icon = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Title = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Message = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ActionUrl = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                SourceKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false),
                ReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserNotifications", x => x.NotificationID);
                table.ForeignKey(
                    name: "FK_UserNotifications_AspNetUsers_RecipientUserID",
                    column: x => x.RecipientUserID,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_RecipientUserID_SourceKey",
            table: "UserNotifications",
            columns: new[] { "RecipientUserID", "SourceKey" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserNotifications");
    }
}
