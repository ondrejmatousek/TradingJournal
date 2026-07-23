using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Models;

namespace TradingJournal.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
        public DbSet<JournalEntryImage> JournalEntryImages => Set<JournalEntryImage>();
        public DbSet<InvoiceCompany> InvoiceCompanies => Set<InvoiceCompany>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
        public DbSet<MarketBias> MarketBiases => Set<MarketBias>();
        public DbSet<StrategyNote> StrategyNotes => Set<StrategyNote>();
        public DbSet<InvestmentEma200Snapshot> InvestmentEma200Snapshots => Set<InvestmentEma200Snapshot>();

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<JournalEntry>()
                .HasIndex(e => new { e.UserId, e.CreatedAt });

            // Ceny – typicky 6 desetinných míst (krypto/FX), 8 pokud chceš být safe
            builder.Entity<JournalEntry>()
                .Property(e => e.EntryPrice)
                .HasPrecision(18, 8);

            builder.Entity<JournalEntry>()
                .Property(e => e.ExitPrice)
                .HasPrecision(18, 8);

            // Risk v procentech – stačí třeba 5 desetinných míst
            builder.Entity<JournalEntry>()
                .Property(e => e.RiskPercentage)
                .HasPrecision(9, 5);

            builder.Entity<JournalEntry>()
                .Property(e => e.ImageData)
                .HasColumnType("varbinary(max)");

            builder.Entity<JournalEntryImage>()
                .HasOne(i => i.JournalEntry)
                .WithMany(e => e.Images)
                .HasForeignKey(i => i.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<JournalEntryImage>()
                .Property(i => i.ImageData)
                .HasColumnType("varbinary(max)");

            builder.Entity<InvoiceCompany>()
                .HasIndex(c => new { c.UserId, c.Name });

            builder.Entity<InvoiceCompany>()
                .Property(c => c.LastHourlyRate)
                .HasPrecision(18, 2);

            builder.Entity<Invoice>()
                .HasIndex(i => new { i.UserId, i.IssueDate });

            builder.Entity<Invoice>()
                .HasIndex(i => new { i.UserId, i.InvoiceNumber })
                .IsUnique();

            builder.Entity<Invoice>()
                .HasOne(i => i.SupplierCompany)
                .WithMany()
                .HasForeignKey(i => i.SupplierCompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Invoice>()
                .HasOne(i => i.CustomerCompany)
                .WithMany()
                .HasForeignKey(i => i.CustomerCompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<InvoiceItem>()
                .HasOne(i => i.Invoice)
                .WithMany(i => i.Items)
                .HasForeignKey(i => i.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<InvoiceItem>()
                .Property(i => i.Quantity)
                .HasPrecision(18, 4);

            builder.Entity<InvoiceItem>()
                .Property(i => i.UnitPrice)
                .HasPrecision(18, 2);

            builder.Entity<InvoiceItem>()
                .Property(i => i.VatRate)
                .HasPrecision(5, 2);

            builder.Entity<MarketBias>()
                .HasIndex(b => new { b.Symbol, b.BiasDate })
                .IsUnique();

            builder.Entity<MarketBias>()
                .Property(b => b.LastClose)
                .HasPrecision(18, 8);

            builder.Entity<MarketBias>()
                .Property(b => b.ChangePercent)
                .HasPrecision(9, 4);

            builder.Entity<MarketBias>()
                .Property(b => b.Rsi14)
                .HasPrecision(9, 4);

            builder.Entity<MarketBias>()
                .Property(b => b.Ma20)
                .HasPrecision(18, 8);

            builder.Entity<MarketBias>()
                .Property(b => b.Ma50)
                .HasPrecision(18, 8);

            builder.Entity<StrategyNote>()
                .HasIndex(n => new { n.UserId, n.StrategyKey })
                .IsUnique();

            builder.Entity<StrategyNote>()
                .Property(n => n.StrategyKey)
                .HasMaxLength(64);

            builder.Entity<StrategyNote>()
                .Property(n => n.ContentHtml)
                .HasColumnType("nvarchar(max)");

            builder.Entity<InvestmentEma200Snapshot>()
                .HasIndex(s => s.Symbol)
                .IsUnique();

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.Symbol)
                .HasMaxLength(16);

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.Name)
                .HasMaxLength(160);

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.Sector)
                .HasMaxLength(80);

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.CurrentPrice)
                .HasPrecision(18, 4);

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.Ema200Daily)
                .HasPrecision(18, 4);

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.Ema200Weekly)
                .HasPrecision(18, 4);

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.Performance1YearPercent)
                .HasPrecision(9, 4);

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.Performance3YearPercent)
                .HasPrecision(9, 4);

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.Performance5YearPercent)
                .HasPrecision(9, 4);

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.ErrorMessage)
                .HasMaxLength(1000);

            builder.Entity<InvestmentEma200Snapshot>()
                .Property(s => s.DataSource)
                .HasMaxLength(80);
        }
    }
}
