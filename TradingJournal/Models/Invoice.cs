using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TradingJournal.Models
{
    public class Invoice
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [StringLength(40)]
        public string InvoiceNumber { get; set; } = string.Empty;

        public int SupplierCompanyId { get; set; }
        public InvoiceCompany? SupplierCompany { get; set; }

        public int CustomerCompanyId { get; set; }
        public InvoiceCompany? CustomerCompany { get; set; }

        public DateTime IssueDate { get; set; } = DateTime.Today;
        public DateTime DueDate { get; set; } = DateTime.Today.AddDays(14);
        public DateTime? TaxableSupplyDate { get; set; }

        [StringLength(3)]
        public string Currency { get; set; } = "CZK";

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

        [StringLength(1000)]
        public string? Notes { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<InvoiceItem> Items { get; set; } = new();

        [NotMapped]
        public decimal TotalNet => Items.Sum(i => i.TotalNet);

        [NotMapped]
        public decimal TotalVat => Items.Sum(i => i.TotalVat);

        [NotMapped]
        public decimal TotalGross => Items.Sum(i => i.TotalGross);
    }
}
