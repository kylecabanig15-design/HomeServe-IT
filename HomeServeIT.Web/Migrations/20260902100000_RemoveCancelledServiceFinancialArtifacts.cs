using HomeServeIT.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeServeIT.Web.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260902100000_RemoveCancelledServiceFinancialArtifacts")]
public partial class RemoveCancelledServiceFinancialArtifacts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE un
            FROM `UserNotifications` AS un
            INNER JOIN `Invoices` AS inv
                ON un.`SourceKey` LIKE CONCAT('%:quotation:', inv.`InvoiceID`, ':%')
                OR un.`SourceKey` LIKE CONCAT('%:invoice:', inv.`InvoiceID`, ':%')
            INNER JOIN `ServiceRequests` AS sr
                ON sr.`RequestID` = inv.`RequestID`
            WHERE sr.`Status` = 'Cancelled';
            """);

        migrationBuilder.Sql("""
            DELETE jiu
            FROM `JobInventoryUsages` AS jiu
            INNER JOIN `ServiceRequests` AS sr
                ON sr.`RequestID` = jiu.`RequestID`
            WHERE sr.`Status` = 'Cancelled'
                AND jiu.`IsDeducted` = FALSE;
            """);

        migrationBuilder.Sql("""
            DELETE inv
            FROM `Invoices` AS inv
            INNER JOIN `ServiceRequests` AS sr
                ON sr.`RequestID` = inv.`RequestID`
            WHERE sr.`Status` = 'Cancelled';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deleted financial artifacts cannot be reconstructed safely.
    }
}
