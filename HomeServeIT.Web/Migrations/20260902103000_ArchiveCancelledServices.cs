using HomeServeIT.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeServeIT.Web.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260902103000_ArchiveCancelledServices")]
public partial class ArchiveCancelledServices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE `ServiceRequests`
            SET `IsArchived` = TRUE
            WHERE `Status` = 'Cancelled';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE `ServiceRequests`
            SET `IsArchived` = FALSE
            WHERE `Status` = 'Cancelled';
            """);
    }
}
