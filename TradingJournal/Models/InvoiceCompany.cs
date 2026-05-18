using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Models
{
    public class InvoiceCompany
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

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

        public decimal? LastHourlyRate { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
