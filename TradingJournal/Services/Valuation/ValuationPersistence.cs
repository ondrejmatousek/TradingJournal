using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Services.Screening;

namespace TradingJournal.Services.Valuation;

public sealed class ValuationInputEntity
{
    public string Id { get; set; } = "";
    public string Json { get; set; } = "";
}
public sealed class ValuationRunEntity
{
    public string Id { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string InputHash { get; set; } = "";
    public string ModelVersion { get; set; } = "ValuationV2.0";
    public DateTimeOffset CreatedAt { get; set; }
    public decimal? BaseFairValue { get; set; }
    public string Status { get; set; } = "";
    public string Json { get; set; } = "";
}
public sealed class InvestmentListing
{
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sector { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
public sealed class InvestmentCatalog(ApplicationDbContext db)
{
    public async Task<IReadOnlyList<InvestmentSymbol>> AllAsync(CancellationToken ct)
    {
        var extra=await db.Set<InvestmentListing>().AsNoTracking().ToListAsync(ct);
        var overridden=extra.Select(x=>x.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return InvestmentUniverse.All.Where(x=>!overridden.Contains(x.Symbol)).Concat(extra.Where(x=>x.Enabled)
            .Select(x=>new InvestmentSymbol(x.Symbol.ToUpperInvariant(),x.Name,x.Sector))).OrderBy(x=>x.Symbol).ToArray();
    }
}
public sealed class ValuationPersistence(ApplicationDbContext db)
{
    public Task<List<ValuationRunEntity>> HistoryAsync(string symbol, DateTimeOffset from, CancellationToken ct)
        =>db.Set<ValuationRunEntity>().AsNoTracking().Where(x=>x.Symbol==symbol && x.CreatedAt>=from).OrderBy(x=>x.CreatedAt).ToListAsync(ct);
    public async Task SaveAsync(ValuationReport report, FinancialDataset? data, CancellationToken ct)
    {
        if(await db.Set<ValuationRunEntity>().AsNoTracking().AnyAsync(x=>x.Id==report.Id,ct))return;
        // Append-only content-addressed input and report. Existing runs are never overwritten.
        if(!await db.Set<ValuationInputEntity>().AnyAsync(x=>x.Id==report.InputHash,ct))
            db.Add(new ValuationInputEntity{Id=report.InputHash,Json=ValuationJson.Serialize(data)});
        db.Add(new ValuationRunEntity{Id=report.Id,Symbol=report.Symbol,InputHash=report.InputHash,ModelVersion=report.ModelVersion,
            CreatedAt=report.CreatedAt,BaseFairValue=report.BaseFairValue,Status=report.Status,Json=ValuationJson.Serialize(report)});
        try {await db.SaveChangesAsync(ct);}
        catch(DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if(!await db.Set<ValuationRunEntity>().AsNoTracking().AnyAsync(x=>x.Id==report.Id,ct))throw;
        }
    }
    public async Task<ValuationReport?> ReadAsync(string id, CancellationToken ct)
    {
        var json=await db.Set<ValuationRunEntity>().AsNoTracking().Where(x=>x.Id==id).Select(x=>x.Json).SingleOrDefaultAsync(ct);
        return json==null?null:System.Text.Json.JsonSerializer.Deserialize<ValuationReport>(json,ValuationJson.Options);
    }
    public async Task<FinancialDataset?> InputAsync(string hash, CancellationToken ct)
    {
        var json=await db.Set<ValuationInputEntity>().AsNoTracking().Where(x=>x.Id==hash).Select(x=>x.Json).SingleOrDefaultAsync(ct);
        return json==null?null:System.Text.Json.JsonSerializer.Deserialize<FinancialDataset>(json,ValuationJson.Options);
    }
}
public static class ValuationRegistration
{
    public static IServiceCollection AddValuationV2(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ValuationOptions>(configuration.GetSection("ValuationV2"));
        services.AddSingleton<IFinancialNormalizationService,FinancialNormalizationService>();
        services.AddSingleton<IWaccCalculator,WaccCalculator>();
        services.AddSingleton<IValuationStrategy,FinancialInstitutionStrategy>();
        services.AddSingleton<IValuationStrategy,ReitStrategy>();
        services.AddSingleton<IValuationStrategy,StandardNonFinancialStrategy>();
        services.AddSingleton<IValuationEngine,ValuationEngine>();
        services.AddScoped<ValuationPersistence>();services.AddScoped<InvestmentCatalog>();
        services.AddScoped<PriceLabService>();
        return services;
    }
    public static ValuationResult? Summary(ValuationReport report)
    {
        if(report.Scenarios.Count!=3)return null;
        var b=report.Scenarios.Single(s=>s.Name=="Base");
        return new(report.Scenarios.Single(s=>s.Name=="Bear").FairValue,b.FairValue,report.Scenarios.Single(s=>s.Name=="Bull").FairValue,
            b.Years[0].RevenueGrowth*100,b.Wacc*100,b.TerminalGrowth*100)
        {Model=report.ModelVersion,BuyBelow=report.BuyBelow??0,MarginOfSafety=report.MarginOfSafety*100,DcfFairValue=b.FairValue,
            Confidence=report.DataQuality,DataQuality=report.Status,Explanation=string.Join(" ",report.Warnings),
            Cash=b.ExcessCash,Debt=b.Debt,DilutedShares=b.Shares,Uncertainty=report.Uncertainty,RunId=report.Id};
    }
}
