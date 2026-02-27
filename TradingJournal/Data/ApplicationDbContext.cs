using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Models;

namespace TradingJournal.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
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
        }
    }
}
