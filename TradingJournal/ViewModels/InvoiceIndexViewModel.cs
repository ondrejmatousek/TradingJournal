using Microsoft.AspNetCore.Mvc.Rendering;
using TradingJournal.Models;

namespace TradingJournal.ViewModels
{
    public class InvoiceIndexViewModel
    {
        public List<Invoice> Invoices { get; set; } = new();
        public InvoiceFilters Filters { get; set; } = new();
        public InvoiceStats Stats { get; set; } = new();
        public List<SelectListItem> CustomerOptions { get; set; } = new();
        public List<SelectListItem> StatusOptions { get; set; } = new();
    }

    public class InvoiceFilters
    {
        public string? Search { get; set; }
        public InvoiceStatus? Status { get; set; }
        public int? CustomerId { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
    }

    public class InvoiceStats
    {
        public int TotalInvoices { get; set; }
        public int DraftInvoices { get; set; }
        public int PaidInvoices { get; set; }
        public decimal TotalIssued { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Outstanding { get; set; }
    }
}
