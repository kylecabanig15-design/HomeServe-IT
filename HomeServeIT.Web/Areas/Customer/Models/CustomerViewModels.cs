using HomeServeIT.Web.Models;

namespace HomeServeIT.Web.Areas.Customer.Models
{
    public class CustomerDashboardViewModel
    {
        public string FirstName { get; set; } = "Customer";
        public int ActiveRequestCount { get; set; }
        public ServiceRequest? LatestActiveRequest { get; set; }
        public Invoice? OutstandingInvoice { get; set; }
        public List<ServiceRequest> AllRequests { get; set; } = new();
    }

    public class CustomerServiceRequestsViewModel
    {
        public List<ServiceRequest> Requests { get; set; } = new();
        public List<HomeServeIT.Web.Models.Technician> AvailableTechnicians { get; set; } = new();
    }

    public class CustomerBillsViewModel
    {
        public List<Invoice> Invoices { get; set; } = new();
        public decimal TotalOutstanding { get; set; }
        public decimal TotalPaid { get; set; }
    }
}
