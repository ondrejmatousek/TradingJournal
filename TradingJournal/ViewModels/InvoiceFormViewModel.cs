using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using TradingJournal.Models;

namespace TradingJournal.ViewModels
{
    public class InvoiceFormViewModel
    {
        public int? Id { get; set; }

        [StringLength(40)]
        public string? InvoiceNumber { get; set; }

        [Required(ErrorMessage = "Vyber, kdo fakturuje.")]
        public int? SupplierCompanyId { get; set; }

        [Required(ErrorMessage = "Vyber, komu fakturuješ.")]
        public int? CustomerCompanyId { get; set; }

        public bool IsSupplierVatPayer { get; set; }

        [Required]
        public DateTime IssueDate { get; set; } = DateTime.Today;

        [Required]
        public DateTime DueDate { get; set; } = DateTime.Today.AddDays(14);

        public DateTime? TaxableSupplyDate { get; set; } = DateTime.Today;

        [Required]
        [StringLength(3)]
        public string Currency { get; set; } = "CZK";

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Range(0.0001, 999999, ErrorMessage = "Celkový počet hodin musí být větší než 0.")]
        public decimal? TotalHoursOverride { get; set; }

        public List<InvoiceItemInput> Items { get; set; } = new()
        {
            new InvoiceItemInput()
        };

        public List<SelectListItem> SupplierOptions { get; set; } = new();
        public Dictionary<int, bool> SupplierVatPayerOptions { get; set; } = new();
        public List<SelectListItem> CustomerOptions { get; set; } = new();
        public Dictionary<int, decimal?> CustomerHourlyRates { get; set; } = new();
        public Dictionary<int, string> CustomerInvoiceDescriptions { get; set; } = new();
        public Dictionary<int, decimal> CustomerInvoiceDefaultPrices { get; set; } = new();
    }

    public class InvoiceItemInput
    {
        [Required(ErrorMessage = "Popis položky je povinný.")]
        [StringLength(260)]
        public string Description { get; set; } = string.Empty;

        [StringLength(20)]
        public string Unit { get; set; } = "ks";

        [Range(0.0001, 999999, ErrorMessage = "Množství musí být větší než 0.")]
        public decimal Quantity { get; set; } = 1;

        [Range(0, 999999999, ErrorMessage = "Cena nesmí být záporná.")]
        public decimal UnitPrice { get; set; }

        [Range(0, 100, ErrorMessage = "DPH musí být mezi 0 a 100 %.")]
        public decimal VatRate { get; set; } = 21;

        public decimal TotalNet => Quantity * UnitPrice;
        public decimal TotalGross => TotalNet + TotalNet * VatRate / 100m;
    }

    public class InvoiceCompanyFormViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(160)]
        public string Name { get; set; } = string.Empty;

        [StringLength(40)]
        public string? RegistrationNumber { get; set; }

        [StringLength(40)]
        public string? TaxNumber { get; set; }

        public bool IsVatPayer { get; set; }

        [StringLength(220)]
        public string? Street { get; set; }

        [StringLength(120)]
        public string? City { get; set; }

        [StringLength(30)]
        public string? ZipCode { get; set; }

        [StringLength(80)]
        public string? Country { get; set; } = "Česká republika";

        [StringLength(180)]
        [EmailAddress]
        public string? Email { get; set; }

        [StringLength(60)]
        public string? Phone { get; set; }

        [StringLength(80)]
        public string? BankAccount { get; set; }

        [StringLength(80)]
        public string? Iban { get; set; }

        public bool IsDefaultSupplier { get; set; }
    }

    public class InvoiceImportViewModel
    {
        [Required(ErrorMessage = "Vyber, kdo fakturuje.")]
        public int? SupplierCompanyId { get; set; }

        [Required(ErrorMessage = "Vyber, komu fakturuješ.")]
        public int? CustomerCompanyId { get; set; }

        public bool IsSupplierVatPayer { get; set; }

        public DateTime IssueDate { get; set; } = DateTime.Today;
        public DateTime DueDate { get; set; } = DateTime.Today.AddDays(14);
        public DateTime? TaxableSupplyDate { get; set; } = DateTime.Today;

        public decimal? HourlyRate { get; set; }

        [StringLength(3)]
        public string Currency { get; set; } = "CZK";

        public List<SelectListItem> SupplierOptions { get; set; } = new();
        public Dictionary<int, bool> SupplierVatPayerOptions { get; set; } = new();
        public List<SelectListItem> CustomerOptions { get; set; } = new();
        public Dictionary<int, decimal?> CustomerHourlyRates { get; set; } = new();
        public Dictionary<int, string> CustomerInvoiceDescriptions { get; set; } = new();
        public Dictionary<int, decimal> CustomerInvoiceDefaultPrices { get; set; } = new();
    }
}
