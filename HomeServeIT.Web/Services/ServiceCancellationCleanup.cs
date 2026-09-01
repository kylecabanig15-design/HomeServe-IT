using HomeServeIT.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Services;

public static class ServiceCancellationCleanup
{
    public static async Task<int> RemoveFinancialArtifactsAsync(
        ApplicationDbContext context,
        int requestId)
    {
        var invoices = await context.Invoices
            .IgnoreQueryFilters()
            .Where(invoice => invoice.RequestID == requestId)
            .ToListAsync();

        if (invoices.Count > 0)
        {
            var invoiceIds = invoices.Select(invoice => invoice.InvoiceID).ToHashSet();
            var financialNotifications = await context.UserNotifications
                .Where(notification => notification.SourceKey.Contains(":quotation:")
                    || notification.SourceKey.Contains(":invoice:"))
                .ToListAsync();

            context.UserNotifications.RemoveRange(financialNotifications.Where(notification =>
                ReferencesInvoice(notification.SourceKey, invoiceIds)));
            context.Invoices.RemoveRange(invoices);
        }

        var unusedReservations = await context.JobInventoryUsages
            .Where(usage => usage.RequestID == requestId && !usage.IsDeducted)
            .ToListAsync();
        context.JobInventoryUsages.RemoveRange(unusedReservations);

        return invoices.Count;
    }

    private static bool ReferencesInvoice(string sourceKey, HashSet<int> invoiceIds)
    {
        var parts = sourceKey.Split(':');
        return parts.Length >= 4
            && (parts[1] == "quotation" || parts[1] == "invoice")
            && int.TryParse(parts[2], out var invoiceId)
            && invoiceIds.Contains(invoiceId);
    }
}
