using HomeServeIT.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Services;

public static class InvoiceScopes
{
    public static IQueryable<Invoice> CustomerFacingInvoices(this IQueryable<Invoice> invoices) =>
        invoices.Where(invoice => !invoice.IsQuotation
            || invoice.QuotationStatus == "ApprovedByAdmin"
            || invoice.QuotationStatus == "Approved");

    public static IQueryable<Invoice> ActiveFinancialRecords(this IQueryable<Invoice> invoices) =>
        invoices.Where(invoice => invoice.PaymentStatus != "Voided"
            && (!invoice.IsQuotation || invoice.QuotationStatus != "Rejected"));

    public static Task<bool> HasPaidInvoiceAsync(this IQueryable<Invoice> invoices, int requestId) =>
        invoices.CustomerFacingInvoices()
            .AnyAsync(invoice => invoice.RequestID == requestId && invoice.PaymentStatus == "Paid");
}
