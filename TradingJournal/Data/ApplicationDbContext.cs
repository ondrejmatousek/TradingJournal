using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Models;

namespace TradingJournal.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
        public DbSet<TradingJournalBook> TradingJournals => Set<TradingJournalBook>();
        public DbSet<JournalEntryImage> JournalEntryImages => Set<JournalEntryImage>();
        public DbSet<MarketBias> MarketBiases => Set<MarketBias>();
        public DbSet<StrategyNote> StrategyNotes => Set<StrategyNote>();
        public DbSet<InvestmentEma200Snapshot> InvestmentEma200Snapshots => Set<InvestmentEma200Snapshot>();
        public DbSet<TradeSetupReview> TradeSetupReviews => Set<TradeSetupReview>();
        public DbSet<TradeSetupReviewImage> TradeSetupReviewImages => Set<TradeSetupReviewImage>();
        public DbSet<MarketPriceEntity> MarketPrices => Set<MarketPriceEntity>();
        public DbSet<ScreeningDocument> ScreeningDocuments => Set<ScreeningDocument>();
        public DbSet<InsiderFiling> InsiderFilings => Set<InsiderFiling>();
        public DbSet<InsiderReportingOwner> InsiderReportingOwners => Set<InsiderReportingOwner>();
        public DbSet<InsiderTransaction> InsiderTransactions => Set<InsiderTransaction>();
        public DbSet<InsiderImportRun> InsiderImportRuns => Set<InsiderImportRun>();
        public DbSet<CompanyValuation> CompanyValuations => Set<CompanyValuation>();

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.ReplaceService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly, ProviderMigrationsAssembly>();
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<TradingJournalBook>().ToTable("TradingJournals");
            builder.Entity<TradingJournalBook>().Property(j => j.UserId).HasMaxLength(450);
            builder.Entity<TradingJournalBook>().HasIndex(j => new { j.UserId, j.Name });
            builder.Entity<JournalEntry>().HasOne(e => e.Journal).WithMany(j => j.Entries)
                .HasForeignKey(e => e.JournalId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<JournalEntry>().HasIndex(e => new { e.UserId, e.JournalId });
            builder.Entity<TradingJournal.Services.Valuation.ValuationInputEntity>().ToTable("ValuationInputs").HasKey(x=>x.Id);
            builder.Entity<TradingJournal.Services.Valuation.ValuationInputEntity>().Property(x=>x.Id).HasMaxLength(64);
            var vr=builder.Entity<TradingJournal.Services.Valuation.ValuationRunEntity>();
            vr.ToTable("ValuationRuns").HasKey(x=>x.Id);
            vr.Property(x=>x.Id).HasMaxLength(64);vr.Property(x=>x.InputHash).HasMaxLength(64);
            vr.Property(x=>x.Symbol).HasMaxLength(32);vr.Property(x=>x.BaseFairValue).HasPrecision(28,10);
            vr.HasIndex(x=>new {x.Symbol,x.CreatedAt});
            vr.HasOne<TradingJournal.Services.Valuation.ValuationInputEntity>().WithMany().HasForeignKey(x=>x.InputHash).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<TradingJournal.Services.Valuation.InvestmentListing>().ToTable("InvestmentListings").HasKey(x=>x.Symbol);
            builder.Entity<TradingJournal.Services.Valuation.InvestmentListing>().Property(x=>x.Symbol).HasMaxLength(32);
            builder.Entity<ScreeningDocument>().Property(x => x.Id).HasMaxLength(64).IsUnicode(false);
            builder.Entity<ScreeningDocument>().Property(x => x.Json).IsRequired();

            builder.Entity<InsiderFiling>().HasIndex(x => x.AccessionNumber).IsUnique();
            builder.Entity<InsiderFiling>().HasIndex(x => new { x.Symbol, x.FiledDate });
            builder.Entity<InsiderFiling>().Property(x => x.AccessionNumber).HasMaxLength(24).IsUnicode(false);
            builder.Entity<InsiderFiling>().Property(x => x.IssuerCik).HasMaxLength(10).IsUnicode(false);
            builder.Entity<InsiderFiling>().Property(x => x.Symbol).HasMaxLength(16).IsUnicode(false);
            builder.Entity<InsiderFiling>().Property(x => x.IssuerName).HasMaxLength(240);
            builder.Entity<InsiderFiling>().Property(x => x.FormType).HasMaxLength(8).IsUnicode(false);
            builder.Entity<InsiderFiling>().Property(x => x.SourceUrl).HasMaxLength(1000).IsUnicode(false);
            builder.Entity<InsiderReportingOwner>().Property(x => x.OwnerCik).HasMaxLength(10).IsUnicode(false);
            builder.Entity<InsiderReportingOwner>().Property(x => x.Name).HasMaxLength(240);
            builder.Entity<InsiderReportingOwner>().Property(x => x.OfficerTitle).HasMaxLength(160);
            builder.Entity<InsiderReportingOwner>().HasOne(x => x.Filing).WithMany(x => x.ReportingOwners)
                .HasForeignKey(x => x.InsiderFilingId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<InsiderTransaction>().HasIndex(x => new { x.InsiderFilingId, x.SourceIndex }).IsUnique();
            builder.Entity<InsiderTransaction>().HasIndex(x => x.TransactionDate);
            builder.Entity<InsiderTransaction>().Property(x => x.SecurityTitle).HasMaxLength(240);
            builder.Entity<InsiderTransaction>().Property(x => x.TransactionCode).HasMaxLength(4).IsUnicode(false);
            builder.Entity<InsiderTransaction>().Property(x => x.AcquisitionDisposition).HasMaxLength(1).IsUnicode(false);
            builder.Entity<InsiderTransaction>().Property(x => x.DirectIndirect).HasMaxLength(1).IsUnicode(false);
            builder.Entity<InsiderTransaction>().Property(x => x.Shares).HasPrecision(28, 6);
            builder.Entity<InsiderTransaction>().Property(x => x.PricePerShare).HasPrecision(28, 6);
            builder.Entity<InsiderTransaction>().Property(x => x.SharesOwnedAfter).HasPrecision(28, 6);
            builder.Entity<InsiderTransaction>().HasOne(x => x.Filing).WithMany(x => x.Transactions)
                .HasForeignKey(x => x.InsiderFilingId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<InsiderImportRun>().HasIndex(x => x.ScheduledDate);
            builder.Entity<InsiderImportRun>().Property(x => x.ErrorSummary).HasMaxLength(2000);

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
                ;

            builder.Entity<JournalEntryImage>()
                .HasOne(i => i.JournalEntry)
                .WithMany(e => e.Images)
                .HasForeignKey(i => i.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<JournalEntryImage>()
                .Property(i => i.ImageData)
                ;

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
                ;

            builder.Entity<TradeSetupReview>()
                .HasIndex(r => new { r.UserId, r.TradeDate });

            builder.Entity<TradeSetupReview>()
                .Property(r => r.StrategyKey)
                .HasMaxLength(64);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.Market)
                .HasMaxLength(120);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.Direction)
                .HasMaxLength(16);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.Session)
                .HasMaxLength(120);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.Status)
                .HasMaxLength(40);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.Verdict)
                .HasMaxLength(80);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.SetupRating)
                .HasMaxLength(20);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.ProcessScore)
                .HasPrecision(5, 2);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.AiReviewJson)
                ;

            builder.Entity<TradeSetupReview>()
                .Property(r => r.Outcome)
                .HasMaxLength(120);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.Entry)
                .HasPrecision(18, 8);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.StopLoss)
                .HasPrecision(18, 8);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.TakeProfit)
                .HasPrecision(18, 8);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.PlannedRr)
                .HasPrecision(9, 4);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.RMultiple)
                .HasPrecision(9, 4);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.HtfContextScore)
                .HasPrecision(3, 2);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.ZoneScore)
                .HasPrecision(3, 2);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.LocalStructureScore)
                .HasPrecision(3, 2);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.EntryConfirmationScore)
                .HasPrecision(3, 2);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.TimingScore)
                .HasPrecision(3, 2);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.RiskRewardScore)
                .HasPrecision(3, 2);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.SessionScore)
                .HasPrecision(3, 2);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.ManagementScore)
                .HasPrecision(3, 2);

            builder.Entity<TradeSetupReview>()
                .Property(r => r.MissingInfo)
                ;

            builder.Entity<TradeSetupReview>()
                .Property(r => r.Notes)
                ;

            builder.Entity<TradeSetupReview>()
                .Property(r => r.Aligned)
                ;

            builder.Entity<TradeSetupReview>()
                .Property(r => r.Issues)
                ;

            builder.Entity<TradeSetupReviewImage>()
                .HasOne(i => i.TradeSetupReview)
                .WithMany(r => r.Images)
                .HasForeignKey(i => i.TradeSetupReviewId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TradeSetupReviewImage>()
                .Property(i => i.ImageData)
                ;

            builder.Entity<TradeSetupReviewImage>()
                .Property(i => i.Label)
                .HasMaxLength(80);

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

            builder.Entity<MarketPriceEntity>()
                .HasIndex(p => new { p.AssetKey, p.Date })
                .IsUnique();

            builder.Entity<MarketPriceEntity>()
                .Property(p => p.AssetKey)
                .HasMaxLength(32);

            builder.Entity<MarketPriceEntity>()
                .Property(p => p.Source)
                .HasMaxLength(40);

            builder.Entity<MarketPriceEntity>()
                .Property(p => p.Close)
                .HasPrecision(20, 8);

            builder.Entity<CompanyValuation>().HasIndex(x => new { x.Symbol, x.ValuationDate }).IsUnique();
            builder.Entity<CompanyValuation>().Property(x => x.Symbol).HasMaxLength(16).IsUnicode(false);
            builder.Entity<CompanyValuation>().Property(x => x.Status).HasMaxLength(40);
            builder.Entity<CompanyValuation>().Property(x => x.Model).HasMaxLength(80);
            builder.Entity<CompanyValuation>().Property(x => x.DataQuality).HasMaxLength(240);
            builder.Entity<CompanyValuation>().Property(x => x.FairValueLow).HasPrecision(20, 8);
            builder.Entity<CompanyValuation>().Property(x => x.FairValueBase).HasPrecision(20, 8);
            builder.Entity<CompanyValuation>().Property(x => x.FairValueHigh).HasPrecision(20, 8);
            builder.Entity<CompanyValuation>().Property(x => x.CurrentPrice).HasPrecision(20, 8);
            builder.Entity<CompanyValuation>().Property(x => x.UpsidePercent).HasPrecision(12, 4);
            builder.Entity<CompanyValuation>().Property(x => x.MarginOfSafetyPercent).HasPrecision(12, 4);
            builder.Entity<CompanyValuation>().Property(x => x.ValuationScore).HasPrecision(8, 3);
            builder.Entity<CompanyValuation>().Property(x => x.Confidence).HasPrecision(8, 3);
        }
    }
}

