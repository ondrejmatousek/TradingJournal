using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TradingJournal.Models
{
    public class InvoiceItem
    {
        public int Id { get; set; }

        public int InvoiceId { get; set; }
        public Invoice? Invoice { get; set; }

        [Required]
        [StringLength(260)]
        public string Description { get; set; } = string.Empty;

        [StringLength(20)]
        public string Unit { get; set; } = "ks";

        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        public decimal VatRate { get; set; } = 21;

        [NotMapped]
        public decimal TotalNet => Quantity * UnitPrice;

        [NotMapped]
        public decimal TotalVat => TotalNet * VatRate / 100m;

        [NotMapped]
        public decimal TotalGross => TotalNet + TotalVat;
    }
}
